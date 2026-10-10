using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.Persistence;

namespace BusinessOS.Restaurant.Sync;

public sealed class OperationalDataRefreshService
{
    private readonly WindowsActivationStore _activationStore;
    private readonly WindowsSessionStore _sessionStore;
    private readonly ConnectionSettingsStore _settingsStore;
    private readonly CloudOperationalDataClient _client;
    private readonly OperationalSnapshotStore _snapshotStore;

    public OperationalDataRefreshService(
        WindowsActivationStore activationStore,
        WindowsSessionStore sessionStore,
        ConnectionSettingsStore settingsStore,
        CloudOperationalDataClient client,
        OperationalSnapshotStore snapshotStore)
    {
        _activationStore = activationStore;
        _sessionStore = sessionStore;
        _settingsStore = settingsStore;
        _client = client;
        _snapshotStore = snapshotStore;
    }

    public async Task<bool> RefreshIfPossibleAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);

        if (settings is null || !settings.SyncEnabled)
        {
            return false;
        }

        var activation = await _activationStore.LoadAsync(cancellationToken);
        var session = await _sessionStore.LoadAsync(cancellationToken);

        if (activation is null || session is null ||
            !DesktopOperatingMode.CloudAllowed(activation, settings))
        {
            return false;
        }

        if (!string.Equals(
                activation.Snapshot.TenantId,
                session.TenantId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The signed-in restaurant user does not belong to the activated tenant.");
        }

        try
        {
            var snapshot = await _client.FetchBootstrapAsync(
                activation,
                session,
                cancellationToken);

            if (!string.Equals(
                    snapshot.TenantId,
                    activation.Snapshot.TenantId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The cloud bootstrap tenant does not match the activated restaurant.");
            }

            await _snapshotStore.ApplyAsync(snapshot, cancellationToken);
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
