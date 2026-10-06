using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.LocalServer;

namespace BusinessOS.Restaurant.Desktop;

public sealed class LocalHostBootstrapper : IAsyncDisposable
{
    private readonly WindowsActivationStore _activationStore;
    private readonly LocalRestaurantServer _server;
    private readonly ConnectionSettingsStore _settingsStore;

    public LocalHostBootstrapper(
        WindowsActivationStore activationStore,
        LocalRestaurantServer server,
        ConnectionSettingsStore settingsStore)
    {
        _activationStore = activationStore;
        _server = server;
        _settingsStore = settingsStore;
    }

    public async Task<LocalServerDescriptor?> StartIfConfiguredAsync(
        CancellationToken cancellationToken = default)
    {
        var activation = await _activationStore.LoadAsync(cancellationToken);

        if (activation is null || !LicenseManager.CanRunOffline(activation, DateTimeOffset.UtcNow))
        {
            return null;
        }

        var settings = await _settingsStore.LoadAsync(cancellationToken);

        if (settings is null || !settings.LocalServerEnabled)
        {
            return null;
        }

        await _server.StartAsync(
            new LocalServerOptions(
                activation.Snapshot.TenantId,
                settings.LocalServerPort,
                settings.LocalServerEnabled),
            cancellationToken);

        return _server.Descriptor;
    }

    public ValueTask DisposeAsync() => _server.DisposeAsync();
}
