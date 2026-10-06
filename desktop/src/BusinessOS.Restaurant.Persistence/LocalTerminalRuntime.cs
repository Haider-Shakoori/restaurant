namespace BusinessOS.Restaurant.Persistence;

public sealed class LocalTerminalRuntime
{
    public required string DeviceId { get; set; }
    public string? DisplayName { get; set; }
    public string? ClientType { get; set; }
    public string? AppVersion { get; set; }
    public string? LastIpAddress { get; set; }
    public string? LastUserAgent { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset FirstSeenAtUtc { get; set; }
    public DateTimeOffset LastHeartbeatAtUtc { get; set; }
    public DateTimeOffset? DisabledAtUtc { get; set; }
    public long? DisabledByUserId { get; set; }
}

public static class LocalTerminalStatus
{
    public const string Online = "online";
    public const string Stale = "stale";
    public const string Offline = "offline";
    public const string Disabled = "disabled";

    public static string Resolve(
        bool isEnabled,
        DateTimeOffset lastHeartbeatAtUtc,
        DateTimeOffset nowUtc,
        TimeSpan? onlineWindow = null,
        TimeSpan? staleWindow = null)
    {
        if (!isEnabled)
        {
            return Disabled;
        }

        var age = nowUtc - lastHeartbeatAtUtc;
        var online = onlineWindow ?? TimeSpan.FromSeconds(45);
        var stale = staleWindow ?? TimeSpan.FromMinutes(3);

        if (age <= online)
        {
            return Online;
        }

        return age <= stale ? Stale : Offline;
    }
}

public static class LocalNetworkMode
{
    public const string Healthy = "healthy";
    public const string Degraded = "degraded";
    public const string IsolatedLocal = "isolated_local";

    public static string Resolve(
        DateTimeOffset? lastCloudSuccessAtUtc,
        string? lastCloudError,
        int pendingOutboxCount,
        DateTimeOffset nowUtc)
    {
        if (lastCloudSuccessAtUtc is null)
        {
            return pendingOutboxCount > 0 || !string.IsNullOrWhiteSpace(lastCloudError)
                ? IsolatedLocal
                : Degraded;
        }

        var age = nowUtc - lastCloudSuccessAtUtc.Value;

        if (age <= TimeSpan.FromMinutes(2) && string.IsNullOrWhiteSpace(lastCloudError))
        {
            return Healthy;
        }

        if (age <= TimeSpan.FromMinutes(10))
        {
            return Degraded;
        }

        return IsolatedLocal;
    }
}
