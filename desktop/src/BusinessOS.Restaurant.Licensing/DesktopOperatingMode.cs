namespace BusinessOS.Restaurant.Licensing;

/// <summary>
/// A signed lease, not an editable local JSON flag, determines whether the
/// licensed terminal may communicate with the tenant cloud.
/// </summary>
public static class DesktopOperatingMode
{
    public const string CloudSync = "cloud_sync";
    public const string StandaloneOffline = "standalone_offline";

    public static bool IsStandalone(ActivationState? activation) =>
        string.Equals(activation?.Snapshot.DesktopMode, StandaloneOffline, StringComparison.Ordinal);

    public static bool CloudAllowed(ActivationState? activation, ConnectionSettings? settings) =>
        activation is not null && !IsStandalone(activation) && settings?.SyncEnabled == true;
}
