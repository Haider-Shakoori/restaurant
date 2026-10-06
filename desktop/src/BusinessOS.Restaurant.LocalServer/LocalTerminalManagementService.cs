using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.LocalServer;

public sealed record LocalTerminalSnapshot(
    string DeviceId,
    string DisplayName,
    string ClientType,
    string? AppVersion,
    long UserId,
    string UserName,
    string UserRole,
    string Status,
    bool IsEnabled,
    DateTimeOffset LastSeenAtUtc,
    string? LastIpAddress,
    string? LastUserAgent);

public sealed record LocalDiagnosticsSnapshot(
    string TenantId,
    string NetworkMode,
    bool LocalOperationsAllowed,
    bool SyncEnabled,
    bool OfflineLeaseValid,
    DateTimeOffset? OfflineValidUntil,
    DateTimeOffset? LastCloudSuccessAtUtc,
    string? LastCloudError,
    int PendingCloudMutations,
    int OpenCloudConflicts,
    int OnlineTerminals,
    int StaleTerminals,
    int OfflineTerminals,
    int DisabledTerminals);

public sealed class LocalTerminalManagementService
{
    private static readonly string[] PendingCloudStatuses = ["pending", "retry", "sending"];
    private readonly LocalDatabaseFactory _databaseFactory;
    private readonly ConnectionSettingsStore _settingsStore;
    private readonly WindowsActivationStore _activationStore;

    public LocalTerminalManagementService(
        LocalDatabaseFactory databaseFactory,
        ConnectionSettingsStore settingsStore,
        WindowsActivationStore activationStore)
    {
        _databaseFactory = databaseFactory;
        _settingsStore = settingsStore;
        _activationStore = activationStore;
    }

    public async Task<bool> RecordHeartbeatAsync(
        LocalPairedTerminal terminal,
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var runtime = await db.TerminalRuntimes
            .SingleOrDefaultAsync(value => value.DeviceId == terminal.DeviceId, cancellationToken);

        if (runtime is not null && !runtime.IsEnabled)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var displayName = Header(request, "X-Device-Name");
        var clientType = Header(request, "X-Terminal-Type");
        var appVersion = Header(request, "X-App-Version");

        if (runtime is null)
        {
            runtime = new LocalTerminalRuntime
            {
                DeviceId = terminal.DeviceId,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? terminal.UserName : displayName,
                ClientType = string.IsNullOrWhiteSpace(clientType) ? DefaultClientType(terminal.UserRole) : clientType,
                AppVersion = appVersion,
                LastIpAddress = request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                LastUserAgent = request.Headers.UserAgent.ToString(),
                IsEnabled = true,
                FirstSeenAtUtc = now,
                LastHeartbeatAtUtc = now,
            };
            db.TerminalRuntimes.Add(runtime);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                runtime.DisplayName = displayName;
            }

            if (!string.IsNullOrWhiteSpace(clientType))
            {
                runtime.ClientType = clientType;
            }

            if (!string.IsNullOrWhiteSpace(appVersion))
            {
                runtime.AppVersion = appVersion;
            }

            runtime.LastIpAddress = request.HttpContext.Connection.RemoteIpAddress?.ToString();
            runtime.LastUserAgent = request.Headers.UserAgent.ToString();
            runtime.LastHeartbeatAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<LocalTerminalSnapshot>> GetTerminalsAsync(
        DateTimeOffset? nowUtc = null,
        CancellationToken cancellationToken = default)
    {
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var paired = await db.PairedTerminals
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var runtimes = await db.TerminalRuntimes
            .AsNoTracking()
            .ToDictionaryAsync(value => value.DeviceId, StringComparer.Ordinal, cancellationToken);
        var now = nowUtc ?? DateTimeOffset.UtcNow;

        return paired.Select(terminal =>
        {
            runtimes.TryGetValue(terminal.DeviceId, out var runtime);
            var lastSeen = runtime?.LastHeartbeatAtUtc ?? terminal.LastSeenAtUtc;
            var enabled = runtime?.IsEnabled ?? true;
            var status = LocalTerminalStatus.Resolve(enabled, lastSeen, now);

            return new LocalTerminalSnapshot(
                terminal.DeviceId,
                runtime?.DisplayName ?? terminal.UserName,
                runtime?.ClientType ?? DefaultClientType(terminal.UserRole),
                runtime?.AppVersion,
                terminal.UserId,
                terminal.UserName,
                terminal.UserRole,
                status,
                enabled,
                lastSeen,
                runtime?.LastIpAddress,
                runtime?.LastUserAgent);
        })
        .OrderByDescending(value => value.LastSeenAtUtc)
        .ToArray();
    }

    public async Task<bool> SetEnabledAsync(
        string deviceId,
        bool enabled,
        long actorUserId,
        CancellationToken cancellationToken = default)
    {
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var terminal = await db.PairedTerminals
            .SingleOrDefaultAsync(value => value.DeviceId == deviceId, cancellationToken);

        if (terminal is null)
        {
            return false;
        }

        var runtime = await db.TerminalRuntimes
            .SingleOrDefaultAsync(value => value.DeviceId == deviceId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (runtime is null)
        {
            runtime = new LocalTerminalRuntime
            {
                DeviceId = deviceId,
                DisplayName = terminal.UserName,
                ClientType = DefaultClientType(terminal.UserRole),
                IsEnabled = enabled,
                FirstSeenAtUtc = terminal.ValidatedAtUtc,
                LastHeartbeatAtUtc = terminal.LastSeenAtUtc,
            };
            db.TerminalRuntimes.Add(runtime);
        }

        runtime.IsEnabled = enabled;
        runtime.DisabledAtUtc = enabled ? null : now;
        runtime.DisabledByUserId = enabled ? null : actorUserId;

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UnpairAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var terminal = await db.PairedTerminals
            .SingleOrDefaultAsync(value => value.DeviceId == deviceId, cancellationToken);

        if (terminal is null)
        {
            return false;
        }

        var runtime = await db.TerminalRuntimes
            .SingleOrDefaultAsync(value => value.DeviceId == deviceId, cancellationToken);

        if (runtime is not null)
        {
            db.TerminalRuntimes.Remove(runtime);
        }

        db.PairedTerminals.Remove(terminal);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<LocalDiagnosticsSnapshot> GetDiagnosticsAsync(
        string tenantId,
        DateTimeOffset? nowUtc = null,
        CancellationToken cancellationToken = default)
    {
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var state = await db.CloudSyncStates
            .AsNoTracking()
            .OrderBy(value => value.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var pending = await db.CloudOutbox
            .AsNoTracking()
            .CountAsync(value => PendingCloudStatuses.Contains(value.Status), cancellationToken);
        var openConflicts = await db.CloudConflicts
            .AsNoTracking()
            .CountAsync(value => value.Status == "open", cancellationToken);

        var settings = await _settingsStore.LoadAsync(cancellationToken);
        ActivationState? activation = null;

        try
        {
            activation = await _activationStore.LoadAsync(cancellationToken);
        }
        catch (PlatformNotSupportedException)
        {
        }

        var leaseValid = activation is not null &&
                         string.Equals(activation.Snapshot.TenantId, tenantId, StringComparison.Ordinal) &&
                         LicenseManager.CanRunOffline(activation, now);
        var terminals = await GetTerminalsAsync(now, cancellationToken);
        var networkMode = settings?.SyncEnabled == true
            ? LocalNetworkMode.Resolve(state?.LastSuccessAtUtc, state?.LastError, pending, now)
            : LocalNetworkMode.IsolatedLocal;

        return new LocalDiagnosticsSnapshot(
            tenantId,
            networkMode,
            leaseValid,
            settings?.SyncEnabled == true,
            leaseValid,
            activation?.Snapshot.OfflineValidUntil,
            state?.LastSuccessAtUtc,
            state?.LastError,
            pending,
            openConflicts,
            terminals.Count(value => value.Status == LocalTerminalStatus.Online),
            terminals.Count(value => value.Status == LocalTerminalStatus.Stale),
            terminals.Count(value => value.Status == LocalTerminalStatus.Offline),
            terminals.Count(value => value.Status == LocalTerminalStatus.Disabled));
    }

    private static string Header(HttpRequest request, string name) =>
        request.Headers[name].ToString().Trim();

    private static string DefaultClientType(string userRole) =>
        string.Equals(userRole, "kitchen", StringComparison.OrdinalIgnoreCase)
            ? "kitchen"
            : "android";
}
