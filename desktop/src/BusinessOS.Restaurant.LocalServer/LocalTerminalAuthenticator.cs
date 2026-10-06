using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessOS.Restaurant.Application.OperationalData;
using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.LocalServer;

public sealed record LocalTerminalPrincipal(
    string DeviceId,
    long UserId,
    string UserPublicId,
    string UserName,
    string UserRole,
    string TenantId);

public sealed class LocalTerminalAuthenticator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalDatabaseFactory _databaseFactory;
    private readonly WindowsActivationStore _activationStore;
    private readonly ConnectionSettingsStore _settingsStore;
    private readonly OperationalSnapshotStore _snapshotStore;
    private readonly LocalTerminalManagementService _terminalManagement;
    private readonly HttpClient _httpClient;

    public LocalTerminalAuthenticator(
        LocalDatabaseFactory databaseFactory,
        WindowsActivationStore activationStore,
        ConnectionSettingsStore settingsStore,
        OperationalSnapshotStore snapshotStore,
        LocalTerminalManagementService terminalManagement,
        HttpClient httpClient)
    {
        _databaseFactory = databaseFactory;
        _activationStore = activationStore;
        _settingsStore = settingsStore;
        _snapshotStore = snapshotStore;
        _terminalManagement = terminalManagement;
        _httpClient = httpClient;
    }

    public async Task<LocalTerminalPrincipal?> AuthenticateAsync(
        HttpRequest request,
        LocalServerOptions serverOptions,
        bool allowCloudPairing,
        CancellationToken cancellationToken)
    {
        var activation = await _activationStore.LoadAsync(cancellationToken);

        if (activation is null ||
            !string.Equals(activation.Snapshot.TenantId, serverOptions.TenantId, StringComparison.Ordinal) ||
            !LicenseManager.CanRunOffline(activation, DateTimeOffset.UtcNow))
        {
            return null;
        }

        if (!TryCredentials(request, out var deviceId, out var deviceSecret, out var accessToken))
        {
            return null;
        }

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var terminal = await db.PairedTerminals
            .SingleOrDefaultAsync(value => value.DeviceId == deviceId, cancellationToken);

        var deviceHash = Hash(deviceSecret);
        var tokenHash = Hash(accessToken);

        if (terminal is not null &&
            string.Equals(terminal.TenantId, serverOptions.TenantId, StringComparison.Ordinal) &&
            SecureEquals(terminal.DeviceSecretHash, deviceHash) &&
            SecureEquals(terminal.AccessTokenHash, tokenHash))
        {
            if (!await _terminalManagement.RecordHeartbeatAsync(
                    terminal,
                    request,
                    cancellationToken))
            {
                return null;
            }

            terminal.LastSeenAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return ToPrincipal(terminal);
        }

        if (!allowCloudPairing)
        {
            return null;
        }

        return await ValidateAndPairThroughCloudAsync(
            request,
            deviceId,
            deviceSecret,
            accessToken,
            deviceHash,
            tokenHash,
            serverOptions,
            cancellationToken);
    }

    private async Task<LocalTerminalPrincipal?> ValidateAndPairThroughCloudAsync(
        HttpRequest localRequest,
        string deviceId,
        string deviceSecret,
        string accessToken,
        string deviceHash,
        string tokenHash,
        LocalServerOptions serverOptions,
        CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);

        if (settings is null)
        {
            return null;
        }

        var baseUri = new Uri(settings.TenantBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(baseUri, "api/v1/sync/bootstrap"));

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("X-Device-Id", deviceId);
        request.Headers.TryAddWithoutValidation("X-Device-Secret", deviceSecret);
        request.Headers.TryAddWithoutValidation("X-App-Version", "1.0.0");

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));

            var root = document.RootElement;
            var data = root.GetProperty("data");
            var tenantId = data.GetProperty("tenant_id").GetString();

            if (!string.Equals(tenantId, serverOptions.TenantId, StringComparison.Ordinal))
            {
                return null;
            }

            var user = data.GetProperty("user");
            var userId = user.GetProperty("id").GetInt64();
            var publicId = user.GetProperty("public_id").GetString() ?? string.Empty;
            var name = user.GetProperty("name").GetString() ?? string.Empty;
            var role = user.GetProperty("role").GetString() ?? string.Empty;

            await _databaseFactory.EnsureCreatedAsync(cancellationToken);
            await using var db = _databaseFactory.Create();

            var staff = await db.StaffUsers
                .SingleOrDefaultAsync(value => value.Id == userId, cancellationToken);

            if (staff is null || !staff.IsActive ||
                !string.Equals(staff.PublicId, publicId, StringComparison.Ordinal) ||
                !string.Equals(staff.Role, role, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var existing = await db.PairedTerminals
                .SingleOrDefaultAsync(value => value.DeviceId == deviceId, cancellationToken);

            var now = DateTimeOffset.UtcNow;

            if (existing is null)
            {
                existing = new LocalPairedTerminal
                {
                    DeviceId = deviceId,
                    TenantId = serverOptions.TenantId,
                    DeviceSecretHash = deviceHash,
                    AccessTokenHash = tokenHash,
                    UserId = userId,
                    UserPublicId = publicId,
                    UserName = name,
                    UserRole = role,
                    ValidatedAtUtc = now,
                    LastSeenAtUtc = now,
                };
                db.PairedTerminals.Add(existing);
            }
            else
            {
                existing.TenantId = serverOptions.TenantId;
                existing.DeviceSecretHash = deviceHash;
                existing.AccessTokenHash = tokenHash;
                existing.UserId = userId;
                existing.UserPublicId = publicId;
                existing.UserName = name;
                existing.UserRole = role;
                existing.ValidatedAtUtc = now;
                existing.LastSeenAtUtc = now;
            }

            await db.SaveChangesAsync(cancellationToken);

            if (!await _terminalManagement.RecordHeartbeatAsync(
                    existing,
                    localRequest,
                    cancellationToken))
            {
                return null;
            }

            try
            {
                var envelope = JsonSerializer.Deserialize<OperationalBootstrapEnvelope>(
                    root.GetRawText(),
                    JsonOptions);

                if (envelope is not null)
                {
                    await _snapshotStore.ApplyAsync(envelope.Data, cancellationToken);
                }
            }
            catch (JsonException)
            {
                // Pairing is valid even if reference-data refresh cannot be parsed.
            }

            return ToPrincipal(existing);
        }
    }

    private static bool TryCredentials(
        HttpRequest request,
        out string deviceId,
        out string deviceSecret,
        out string accessToken)
    {
        deviceId = request.Headers["X-Device-Id"].ToString();
        deviceSecret = request.Headers["X-Device-Secret"].ToString();
        accessToken = string.Empty;

        var authorization = request.Headers.Authorization.ToString();

        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            accessToken = authorization["Bearer ".Length..].Trim();
        }

        return !string.IsNullOrWhiteSpace(deviceId) &&
               !string.IsNullOrWhiteSpace(deviceSecret) &&
               !string.IsNullOrWhiteSpace(accessToken);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool SecureEquals(string expectedHex, string actualHex)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expectedHex),
                Convert.FromHexString(actualHex));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static LocalTerminalPrincipal ToPrincipal(LocalPairedTerminal terminal) =>
        new(
            terminal.DeviceId,
            terminal.UserId,
            terminal.UserPublicId,
            terminal.UserName,
            terminal.UserRole,
            terminal.TenantId);
}
