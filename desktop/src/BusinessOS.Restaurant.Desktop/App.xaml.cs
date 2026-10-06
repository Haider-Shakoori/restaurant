using System.IO;
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

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogStartupFailure("UnhandledAppDomain", args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            LogStartupFailure("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogStartupFailure("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        var activationStore = new WindowsActivationStore();
        var settingsStore = new ConnectionSettingsStore();
        var databaseFactory = new LocalDatabaseFactory();

        await TryStartupStageAsync("LocalDatabase", () => databaseFactory.EnsureCreatedAsync());

        _printQueue = new KotPrintQueueProcessor(databaseFactory);
        await TryStartupStageAsync("KotPrintQueue", () => _printQueue.StartAsync());

        _receiptPrintQueue = new ReceiptPrintQueueProcessor(databaseFactory);
        await TryStartupStageAsync("ReceiptPrintQueue", () => _receiptPrintQueue.StartAsync());

        await TryStartupStageAsync("OperationalRefresh", async () =>
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
        });

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

        await TryStartupStageAsync("CloudReconciliation", () => _cloudReconciliation.StartAsync());

        _localHost = new LocalHostBootstrapper(
            activationStore,
            new LocalRestaurantServer(databaseFactory),
            settingsStore);

        await TryStartupStageAsync("LocalHost", () => _localHost.StartIfConfiguredAsync());
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        TryShutdown("LocalHost", () => _localHost?.DisposeAsync().AsTask().GetAwaiter().GetResult());
        TryShutdown("KotPrintQueue", () => _printQueue?.DisposeAsync().AsTask().GetAwaiter().GetResult());
        TryShutdown("ReceiptPrintQueue", () => _receiptPrintQueue?.DisposeAsync().AsTask().GetAwaiter().GetResult());
        TryShutdown("CloudReconciliation", () => _cloudReconciliation?.DisposeAsync().AsTask().GetAwaiter().GetResult());

        _cloudReconciliationHttpClient?.Dispose();

        base.OnExit(e);
    }

    private static async Task TryStartupStageAsync(string stage, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            LogStartupFailure(stage, exception);
        }
    }

    private static void TryShutdown(string stage, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            LogStartupFailure($"Shutdown:{stage}", exception);
        }
    }

    private static void LogStartupFailure(string stage, Exception? exception)
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BusinessOS",
                "Restaurant",
                "logs");

            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "desktop-startup.log");

            File.AppendAllText(
                path,
                $"[{DateTimeOffset.UtcNow:O}] {stage}{Environment.NewLine}" +
                $"{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Startup diagnostics must never crash the application.
        }
    }
}
