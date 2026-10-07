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

        await _settingsStore.SaveAsync(
            new ConnectionSettings(state.TenantBaseUrl),
            cancellationToken);

        return state;
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
        var remaining = state.Snapshot.SubscriptionEndsAt.ToUniversalTime() - now;
        var days = remaining <= TimeSpan.Zero
            ? 0
            : Math.Max(1, (int)Math.Ceiling(remaining.TotalDays));

        return new RestaurantLicenseStatus(
            true,
            state.Snapshot.SubscriptionEndsAt.ToUniversalTime() > now,
            state.Snapshot.PlanName,
            state.Snapshot.SubscriptionEndsAt,
            state.Snapshot.OfflineValidUntil,
            days,
            state.TenantBaseUrl);
    }
}
