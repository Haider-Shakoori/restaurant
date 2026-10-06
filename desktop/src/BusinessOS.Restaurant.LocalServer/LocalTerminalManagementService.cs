using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.LocalServer;

public sealed record LocalTerminalStatus(string DeviceId, string UserName, string UserRole, DateTimeOffset ValidatedAtUtc, DateTimeOffset LastSeenAtUtc, string Connectivity, bool IsRevoked);
public sealed record LocalNetworkDiagnostics(string Health, string Mode, int OnlineTerminals, int StaleTerminals, int RevokedTerminals, int PendingCloudMutations, int CloudConflicts, DateTimeOffset? LastCloudSuccessAtUtc, string? LastCloudError, DateTimeOffset GeneratedAtUtc);

public sealed class LocalTerminalManagementService
{
    private readonly LocalDatabaseFactory _databaseFactory;
    public LocalTerminalManagementService(LocalDatabaseFactory databaseFactory) => _databaseFactory = databaseFactory;

    public async Task<IReadOnlyList<LocalTerminalStatus>> ListAsync(CancellationToken token = default)
    {
        await _databaseFactory.EnsureCreatedAsync(token);
        await using var db = _databaseFactory.Create();
        var now = DateTimeOffset.UtcNow;
        return await db.PairedTerminals.OrderByDescending(x => x.LastSeenAtUtc)
            .Select(x => new LocalTerminalStatus(x.DeviceId, x.UserName, x.UserRole, x.ValidatedAtUtc, x.LastSeenAtUtc,
                x.IsRevoked ? "revoked" : x.LastSeenAtUtc >= now.AddMinutes(-2) ? "online" : x.LastSeenAtUtc >= now.AddMinutes(-15) ? "stale" : "offline", x.IsRevoked))
            .ToListAsync(token);
    }

    public async Task<bool> RevokeAsync(string deviceId, CancellationToken token = default)
    {
        await _databaseFactory.EnsureCreatedAsync(token);
        await using var db = _databaseFactory.Create();
        var terminal = await db.PairedTerminals.SingleOrDefaultAsync(x => x.DeviceId == deviceId, token);
        if (terminal is null) return false;
        terminal.IsRevoked = true; terminal.RevokedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token); return true;
    }

    public async Task<bool> RestoreAsync(string deviceId, CancellationToken token = default)
    {
        await _databaseFactory.EnsureCreatedAsync(token);
        await using var db = _databaseFactory.Create();
        var terminal = await db.PairedTerminals.SingleOrDefaultAsync(x => x.DeviceId == deviceId, token);
        if (terminal is null) return false;
        terminal.IsRevoked = false; terminal.RevokedAtUtc = null;
        await db.SaveChangesAsync(token); return true;
    }

    public async Task<LocalNetworkDiagnostics> DiagnoseAsync(CancellationToken token = default)
    {
        await _databaseFactory.EnsureCreatedAsync(token);
        await using var db = _databaseFactory.Create();
        var now = DateTimeOffset.UtcNow;
        var terminals = await db.PairedTerminals.AsNoTracking().ToListAsync(token);
        var state = await db.CloudSyncStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, token);
        var pending = await db.CloudOutbox.CountAsync(x => x.Status == "pending" || x.Status == "retry", token);
        var conflicts = await db.CloudConflicts.CountAsync(x => x.Status == "open", token);
        var online = terminals.Count(x => !x.IsRevoked && x.LastSeenAtUtc >= now.AddMinutes(-2));
        var stale = terminals.Count(x => !x.IsRevoked && x.LastSeenAtUtc < now.AddMinutes(-2));
        var revoked = terminals.Count(x => x.IsRevoked);
        var cloudHealthy = state?.LastSuccessAtUtc is not null && state.LastSuccessAtUtc >= now.AddMinutes(-5);
        var health = conflicts > 0 ? "attention" : pending > 0 || !cloudHealthy ? "degraded" : "healthy";
        return new LocalNetworkDiagnostics(health, cloudHealthy ? "lan+cloud" : "lan-local-first", online, stale, revoked, pending, conflicts, state?.LastSuccessAtUtc, state?.LastError, now);
    }
}