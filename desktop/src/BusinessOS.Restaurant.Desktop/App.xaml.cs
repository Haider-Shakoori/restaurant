using System.Net.Http;
using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using BusinessOS.Restaurant.Printing;
using BusinessOS.Restaurant.Sync;

namespace BusinessOS.Restaurant.Desktop;

public partial class App : System.Windows.Application
{
    private LocalHostBootstrapper? _localHost;
    private KotPrintQueueProcessor? _printQueue;
    private ReceiptPrintQueueProcessor? _receiptPrintQueue;
    private CloudReconciliationProcessor? _cloudReconciliation;
    private HttpClient? _cloudReconciliationHttpClient;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        var activationStore = new WindowsActivationStore();
        var settingsStore = new ConnectionSettingsStore();
        var databaseFactory = new LocalDatabaseFactory();
        await databaseFactory.EnsureCreatedAsync();

        _printQueue = new KotPrintQueueProcessor(databaseFactory);
        await _printQueue.StartAsync();

        _receiptPrintQueue = new ReceiptPrintQueueProcessor(databaseFactory);
        await _receiptPrintQueue.StartAsync();

        try
        {
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15),
            };

            var refresh = new OperationalDataRefreshService(
                activationStore,
                new WindowsSessionStore(),
                settingsStore,
                new CloudOperationalDataClient(httpClient),
                new OperationalSnapshotStore(databaseFactory));

            await refresh.RefreshIfPossibleAsync();
        }
        catch
        {
            // Cached reference data remains available while cloud synchronization is unavailable.
        }

        _cloudReconciliationHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        _cloudReconciliation = new CloudReconciliationProcessor(
            activationStore,
            new WindowsSessionStore(),
            settingsStore,
            new CloudReconciliationService(
                databaseFactory,
                new CloudReconciliationClient(_cloudReconciliationHttpClient)));
        await _cloudReconciliation.StartAsync();

        _localHost = new LocalHostBootstrapper(
            activationStore,
            new LocalRestaurantServer(databaseFactory),
            settingsStore);

        try
        {
            await _localHost.StartIfConfiguredAsync();
        }
        catch
        {
            // The desktop remains usable when the LAN host cannot bind.
            // Diagnostics/UI reporting are added with the local-host management surface.
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_localHost is not null)
        {
            _localHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (_printQueue is not null)
        {
            _printQueue.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (_receiptPrintQueue is not null)
        {
            _receiptPrintQueue.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (_cloudReconciliation is not null)
        {
            _cloudReconciliation.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _cloudReconciliationHttpClient?.Dispose();

        base.OnExit(e);
    }
}
