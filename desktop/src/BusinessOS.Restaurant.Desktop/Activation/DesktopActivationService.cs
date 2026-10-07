using System.Net.Http;
using System.Reflection;
using BusinessOS.Restaurant.Licensing;

namespace BusinessOS.Restaurant.Desktop.Activation;

internal sealed record DesktopActivationStatus(
    bool IsActivated,
    string PlanName,
    string PlanCode,
    int DaysRemaining,
    DateTimeOffset? SubscriptionEndsAt,
    string TenantBaseUrl,
    string Message);

internal sealed class DesktopActivationService
{
    public static readonly Uri PlatformBaseUri = new("https://restaurant.businessos.af/", UriKind.Absolute);
    public const string TrialWebsiteUrl = "https://restaurant.businessos.af/start-trial";

    private readonly WindowsActivationStore _store = new();
    private readonly ConnectionSettingsStore _settingsStore = new();

    public async Task<ActivationState?> LoadUsableAsync(
        bool refreshWhenCloseToExpiry = true,
        CancellationToken cancellationToken = default)
    {
        ActivationState? state;
        try
        {
            state = await _store.LoadAsync(cancellationToken);
        }
        catch
        {
            return null;
        }

        if (state is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (refreshWhenCloseToExpiry &&
            state.Snapshot.OfflineValidUntil - now <= TimeSpan.FromDays(1))
        {
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                var manager = new LicenseManager(
                    new LicenseApiClient(httpClient),
                    new SignedLeaseVerifier(),
                    _store,
                    new InstallationIdentityProvider());
                state = await manager.RefreshAsync(state, AppVersion(), cancellationToken);
            }
            catch
            {
                // Restaurant Desktop is local-first. A still-valid signed lease
                // must not be rejected only because internet is unavailable.
            }
        }

        return LicenseManager.CanRunOffline(state, now) ? state : null;
    }

    public async Task<ActivationState> ActivateAsync(
        string licenseKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            throw new ArgumentException("Enter the Restaurant license key.", nameof(licenseKey));
        }

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        var manager = new LicenseManager(
            new LicenseApiClient(httpClient),
            new SignedLeaseVerifier(),
            _store,
            new InstallationIdentityProvider());

        var state = await manager.ActivateFromPlatformAsync(
            PlatformBaseUri,
            licenseKey.Trim(),
            AppVersion(),
            cancellationToken);

        await _settingsStore.SaveAsync(
            new ConnectionSettings(
                state.TenantBaseUrl,
                SyncEnabled: true,
                LocalServerEnabled: true,
                LocalServerPort: 8787),
            cancellationToken);

        return state;
    }

    public static DesktopActivationStatus Describe(ActivationState? state)
    {
        if (state is null)
        {
            return new DesktopActivationStatus(
                false,
                "Not activated",
                string.Empty,
                0,
                null,
                string.Empty,
                "A Restaurant license key is required.");
        }

        var remaining = state.Snapshot.SubscriptionEndsAt - DateTimeOffset.UtcNow;
        var days = Math.Max(0, (int)Math.Ceiling(remaining.TotalDays));
        var usable = LicenseManager.CanRunOffline(state, DateTimeOffset.UtcNow);

        return new DesktopActivationStatus(
            usable,
            state.Snapshot.PlanName,
            state.Snapshot.PlanCode,
            days,
            state.Snapshot.SubscriptionEndsAt,
            state.TenantBaseUrl,
            usable
                ? $"{state.Snapshot.PlanName} • {days} day{(days == 1 ? string.Empty : "s")} remaining"
                : "The signed Restaurant license has expired.");
    }

    private static string AppVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
}
