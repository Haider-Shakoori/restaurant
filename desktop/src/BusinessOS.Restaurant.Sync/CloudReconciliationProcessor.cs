using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.Licensing;

namespace BusinessOS.Restaurant.Sync;

public sealed class CloudReconciliationProcessor : IAsyncDisposable
{
    private readonly WindowsActivationStore _activationStore;
    private readonly WindowsSessionStore _sessionStore;
    private readonly ConnectionSettingsStore _settingsStore;
    private readonly CloudReconciliationService _service;
    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    public CloudReconciliationProcessor(
        WindowsActivationStore activationStore,
        WindowsSessionStore sessionStore,
        ConnectionSettingsStore settingsStore,
        CloudReconciliationService service)
    {
        _activationStore = activationStore;
        _sessionStore = sessionStore;
        _settingsStore = settingsStore;
        _service = service;
    }

    public Task StartAsync()
    {
        if (!OperatingSystem.IsWindows() || _loop is not null)
        {
            return Task.CompletedTask;
        }

        _cancellation = new CancellationTokenSource();
        _loop = RunAsync(_cancellation.Token);
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(30);

            try
            {
                var settings = await _settingsStore.LoadAsync(cancellationToken);

                if (settings?.SyncEnabled == true)
                {
                    var activation = await _activationStore.LoadAsync(cancellationToken);
                    var session = await _sessionStore.LoadAsync(cancellationToken);

                    if (activation is not null && session is not null)
                    {
                        await _service.RunOnceAsync(activation, session, cancellationToken);

                        // Cloud is the live fallback transport when waiter devices
                        // cannot reach this Desktop over LAN. Keep this poll short
                        // enough for KOT/order hand-off to remain operational.
                        delay = TimeSpan.FromSeconds(5);
                    }
                    else
                    {
                        delay = TimeSpan.FromSeconds(10);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Local operations remain authoritative. Back off while the cloud
                // is unhealthy, then recover automatically without user action.
                delay = TimeSpan.FromSeconds(15);
            }

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        var cancellation = _cancellation;
        var loop = _loop;

        _cancellation = null;
        _loop = null;

        if (cancellation is null)
        {
            return;
        }

        await cancellation.CancelAsync();

        if (loop is not null)
        {
            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation.Dispose();
        GC.SuppressFinalize(this);
    }
}
