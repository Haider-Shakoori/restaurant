namespace BusinessOS.Restaurant.Licensing;

public sealed record RestaurantLicenseStatus(
    bool HasActivation,
    bool IsValid,
    string PlanName,
    DateTimeOffset? SubscriptionEndsAt,
    DateTimeOffset? OfflineValidUntil,
    int DaysRemaining,
    string TenantBaseUrl)
{
    public static RestaurantLicenseStatus Missing { get; } =
        new(false, false, "Not activated", null, null, 0, string.Empty);
}

public sealed class RestaurantLicenseCoordinator
{
    public static readonly Uri DefaultCentralBaseUri = new("https://restaurant.businessos.af/");

    private readonly WindowsActivationStore _store;
    private readonly ConnectionSettingsStore _settingsStore;
    private readonly InstallationIdentityProvider _identity;

    public RestaurantLicenseCoordinator(
        WindowsActivationStore? store = null,
        ConnectionSettingsStore? settingsStore = null,
        InstallationIdentityProvider? identity = null)
    {
        _store = store ?? new WindowsActivationStore();
        _settingsStore = settingsStore ?? new ConnectionSettingsStore();
        _identity = identity ?? new InstallationIdentityProvider();
    }

    public async Task<ActivationState> ActivateAsync(
        string licenseKey,
        Uri? centralBaseUri = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            throw new ArgumentException("Enter a Restaurant license key.", nameof(licenseKey));
        }

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
        };

        var resolver = new CentralLicenseResolver(httpClient);
        var resolved = await resolver.ResolveAsync(
            centralBaseUri ?? DefaultCentralBaseUri,
            licenseKey,
            cancellationToken);

        if (!Uri.TryCreate(resolved.TenantBaseUrl, UriKind.Absolute, out var tenantBaseUri) ||
            (tenantBaseUri.Scheme != Uri.UriSchemeHttps && tenantBaseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new LicenseApiException(
                "The licensing server returned an invalid restaurant workspace URL.",
                false);
        }

        var manager = new LicenseManager(
            new LicenseApiClient(httpClient),
            new SignedLeaseVerifier(),
            _store,
            _identity);

        var version = typeof(RestaurantLicenseCoordinator).Assembly.GetName().Version?.ToString(3);
        var state = await manager.ActivateAsync(
            tenantBaseUri,
            licenseKey,
            version,
            cancellationToken);

        var existing = await _settingsStore.LoadAsync(cancellationToken);
        await _settingsStore.SaveAsync(existing is null
            ? new ConnectionSettings(state.TenantBaseUrl,
                SyncEnabled: !DesktopOperatingMode.IsStandalone(state))
            : existing with
            {
                TenantBaseUrl = state.TenantBaseUrl,
                SyncEnabled = !DesktopOperatingMode.IsStandalone(state) && existing.SyncEnabled,
            }, cancellationToken);

        return state;
    }

    /// <summary>
    /// Explicit online operation to adopt a changed platform desktop mode.
    /// A standalone lease never refreshes itself in the background.
    /// </summary>
    public async Task<ActivationState> RefreshModeAsync(CancellationToken cancellationToken = default)
    {
        var current = await _store.LoadAsync(cancellationToken)
            ?? throw new InvalidOperationException("Activate the desktop before refreshing its mode.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var manager = new LicenseManager(new LicenseApiClient(http),
            new SignedLeaseVerifier(), _store, _identity);
        var version = typeof(RestaurantLicenseCoordinator).Assembly.GetName().Version?.ToString(3);
        var refreshed = await manager.RefreshAsync(current, version, cancellationToken);

        var settings = await _settingsStore.LoadAsync(cancellationToken);
        // Never automatically turn cloud sync on when leaving standalone: those
        // local transactions may not match cloud data. Explicit reconciliation
        // and a backup are required before enabling it.
        if (settings is not null)
        {
            await _settingsStore.SaveAsync(settings with
            {
                SyncEnabled = !DesktopOperatingMode.IsStandalone(refreshed) &&
                    !DesktopOperatingMode.IsStandalone(current) && settings.SyncEnabled,
            }, cancellationToken);
        }

        return refreshed;
    }

    public async Task<RestaurantLicenseStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var state = await _store.LoadAsync(cancellationToken);

        if (state is null)
        {
            return RestaurantLicenseStatus.Missing;
        }

        var now = DateTimeOffset.UtcNow;

        // A stored activation is enough for reinstall/upgrade while its signed
        // offline lease is valid. Only contact the server when that lease has
        // expired; this keeps installation and startup fast on poor internet.
        if (!LicenseManager.CanRunOffline(state, now) &&
            state.Snapshot.SubscriptionEndsAt.ToUniversalTime() > now)
        {
            try
            {
                using var httpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(20),
                };

                var manager = new LicenseManager(
                    new LicenseApiClient(httpClient),
                    new SignedLeaseVerifier(),
                    _store,
                    _identity);

                var version = typeof(RestaurantLicenseCoordinator).Assembly
                    .GetName().Version?.ToString(3);

                state = await manager.RefreshAsync(
                    state,
                    version,
                    cancellationToken);
                now = DateTimeOffset.UtcNow;
            }
            catch (LicenseApiException)
            {
                // Local-first behavior: if the server is unreachable, the
                // signed lease below remains the authority for offline access.
            }
        }

        var remaining = state.Snapshot.SubscriptionEndsAt.ToUniversalTime() - now;
        var days = remaining <= TimeSpan.Zero
            ? 0
            : Math.Max(1, (int)Math.Ceiling(remaining.TotalDays));

        return new RestaurantLicenseStatus(
            true,
            LicenseManager.CanRunOffline(state, now),
            state.Snapshot.PlanName,
            state.Snapshot.SubscriptionEndsAt,
            state.Snapshot.OfflineValidUntil,
            days,
            state.TenantBaseUrl);
    }
}
