using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.LocalServer;

public sealed record LocalPairingCodeRequest(
    [System.Text.Json.Serialization.JsonPropertyName("staff_user_id")] long StaffUserId);

public sealed record LocalPairingCode(
    string Code,
    long StaffUserId,
    string StaffName,
    DateTimeOffset ExpiresAtUtc);

public sealed record LocalPairRequest(
    string PairingCode,
    string DeviceUid,
    string DeviceName);

public sealed record LocalPairedDevice(
    string Id,
    string DeviceUid,
    string DeviceName,
    long StaffUserId);

public sealed record LocalPairResponse(
    LocalPairedDevice Device,
    string DeviceSecret,
    string AccessToken,
    string TokenType,
    string TenantId,
    string PublicKey,
    SignedLease Lease);

public sealed record LocalAuthContext(
    LocalDevice Device,
    LocalStaffUser Staff,
    ActivationState HostActivation);

public sealed class LocalPairingService
{
    private sealed record PendingCode(long StaffUserId, DateTimeOffset ExpiresAtUtc);

    private readonly LocalDatabaseFactory _databaseFactory;
    private readonly WindowsActivationStore _activationStore;
    private readonly ConcurrentDictionary<string, PendingCode> _codes =
        new(StringComparer.Ordinal);

    public LocalPairingService(
        LocalDatabaseFactory databaseFactory,
        WindowsActivationStore activationStore)
    {
        _databaseFactory = databaseFactory;
        _activationStore = activationStore;
    }

    public async Task<LocalPairingCode> CreatePairingCodeAsync(
        long staffUserId,
        CancellationToken cancellationToken = default)
    {
        var activation = await RequireValidHostActivationAsync(cancellationToken);
        _ = activation;

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        var staff = await db.StaffUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == staffUserId && value.IsActive,
                cancellationToken)
            ?? throw new InvalidOperationException("The selected restaurant user is not active locally.");

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        _codes[code] = new PendingCode(staff.Id, expiresAt);

        return new LocalPairingCode(code, staff.Id, staff.Name, expiresAt);
    }

    public async Task<LocalPairResponse> PairAsync(
        LocalPairRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_codes.TryRemove(request.PairingCode.Trim(), out var pending) ||
            pending.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("The local pairing code is invalid or expired.");
        }

        var activation = await RequireValidHostActivationAsync(cancellationToken);
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var staff = await db.StaffUsers
            .SingleOrDefaultAsync(
                value => value.Id == pending.StaffUserId && value.IsActive,
                cancellationToken)
            ?? throw new InvalidOperationException("The paired restaurant user is no longer active.");

        var deviceSecret = RandomToken();
        var accessToken = RandomToken();
        var now = DateTimeOffset.UtcNow;
        var device = await db.Devices.SingleOrDefaultAsync(
            value => value.DeviceUid == request.DeviceUid,
            cancellationToken);

        if (device is null)
        {
            device = new LocalDevice
            {
                Id = Guid.NewGuid().ToString("D"),
                DeviceUid = request.DeviceUid,
                DeviceName = request.DeviceName,
                StaffUserId = staff.Id,
                SecretHash = Hash(deviceSecret),
                IsActive = true,
                PairedAtUtc = now,
                LastSeenAtUtc = now,
            };
            db.Devices.Add(device);
        }
        else
        {
            device.DeviceName = request.DeviceName;
            device.StaffUserId = staff.Id;
            device.SecretHash = Hash(deviceSecret);
            device.IsActive = true;
            device.PairedAtUtc = now;
            device.LastSeenAtUtc = now;

            var oldSessions = await db.Sessions
                .Where(value => value.DeviceId == device.Id)
                .ToListAsync(cancellationToken);
            db.Sessions.RemoveRange(oldSessions);
        }

        var session = new LocalSession
        {
            Id = Guid.NewGuid().ToString("D"),
            DeviceId = device.Id,
            StaffUserId = staff.Id,
            TokenHash = Hash(accessToken),
            ExpiresAtUtc = activation.Snapshot.OfflineValidUntil,
        };
        db.Sessions.Add(session);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new LocalPairResponse(
            new LocalPairedDevice(device.Id, device.DeviceUid, device.DeviceName, staff.Id),
            deviceSecret,
            accessToken,
            "Bearer",
            activation.Snapshot.TenantId,
            activation.PublicKey,
            activation.Lease);
    }

    public async Task<LocalAuthContext> AuthenticateAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = await AuthenticateDeviceAsync(request, cancellationToken);
        var authorization = request.Headers.Authorization.ToString();

        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("A local restaurant session token is required.");
        }

        var token = authorization["Bearer ".Length..].Trim();

        await using var db = _databaseFactory.Create();
        var tokenHash = Hash(token);
        var session = await db.Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value =>
                    value.DeviceId == context.Device.Id &&
                    value.StaffUserId == context.Staff.Id &&
                    value.TokenHash == tokenHash &&
                    value.ExpiresAtUtc > DateTimeOffset.UtcNow,
                cancellationToken);

        return session is null
            ? throw new UnauthorizedAccessException("The local restaurant session is invalid or expired.")
            : context;
    }

    public async Task<LocalAuthContext> AuthenticateDeviceAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        var activation = await RequireValidHostActivationAsync(cancellationToken);
        var deviceId = request.Headers["X-Device-Id"].ToString();
        var deviceSecret = request.Headers["X-Device-Secret"].ToString();

        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(deviceSecret))
        {
            throw new UnauthorizedAccessException("Local device credentials are required.");
        }

        await using var db = _databaseFactory.Create();
        var device = await db.Devices.SingleOrDefaultAsync(
            value => value.Id == deviceId && value.IsActive,
            cancellationToken);

        if (device is null || !FixedHashEquals(device.SecretHash, Hash(deviceSecret)))
        {
            throw new UnauthorizedAccessException("The local device credentials are invalid.");
        }

        var staff = await db.StaffUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == device.StaffUserId && value.IsActive,
                cancellationToken)
            ?? throw new UnauthorizedAccessException("The paired restaurant user is inactive.");

        device.LastSeenAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return new LocalAuthContext(device, staff, activation);
    }

    private async Task<ActivationState> RequireValidHostActivationAsync(
        CancellationToken cancellationToken)
    {
        var activation = await _activationStore.LoadAsync(cancellationToken);

        if (activation is null ||
            !LicenseManager.CanRunOffline(activation, DateTimeOffset.UtcNow))
        {
            throw new UnauthorizedAccessException(
                "The desktop restaurant license is not currently valid for offline operation.");
        }

        return activation;
    }

    private static string RandomToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedHashEquals(string left, string right)
    {
        var a = Convert.FromHexString(left);
        var b = Convert.FromHexString(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
