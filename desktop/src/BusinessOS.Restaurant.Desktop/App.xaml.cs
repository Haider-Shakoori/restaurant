using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        _ = InitializeServicesSafelyAsync();
    }

    private async Task InitializeServicesSafelyAsync()
    {
        try
        {
            await InitializeServicesAsync();
        }
        catch (Exception exception)
        {
            WriteCrashLog(exception);
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show(
                    "BusinessOS Restaurant could not initialize every background service. The desktop will remain open so you can review Settings and diagnostics.\n\n" + exception.Message,
                    "BusinessOS Restaurant",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning));
        }
    }

    private async Task InitializeServicesAsync()
    {
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
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var refresh = new OperationalDataRefreshService(
                activationStore,
                new WindowsSessionStore(),
                settingsStore,
                new CloudOperationalDataClient(httpClient),
                new OperationalSnapshotStore(databaseFactory));
            await refresh.RefreshIfPossibleAsync();
        }
        catch (Exception exception)
        {
            WriteCrashLog(exception, "cloud-refresh");
        }

        _cloudReconciliationHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
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
        catch (Exception exception)
        {
            WriteCrashLog(exception, "lan-host");
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception, "ui");
        MessageBox.Show(
            "A Restaurant Desktop error was captured instead of closing the application.\n\n" + e.Exception.Message +
            "\n\nA diagnostic log was saved under LocalAppData\\BusinessOS\\Restaurant\\logs.",
            "BusinessOS Restaurant",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception) WriteCrashLog(exception, "fatal");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception, "background");
        e.SetObserved();
    }

    internal static void LogRecoverableException(string area, Exception exception) => WriteCrashLog(exception, area);

    private static void WriteCrashLog(Exception exception, string area = "startup")
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BusinessOS", "Restaurant", "logs");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, $"desktop-{DateTime.UtcNow:yyyyMMdd}.log");
            File.AppendAllText(path,
                $"[{DateTime.UtcNow:O}] {area}\n{exception}\n\n");
        }
        catch
        {
            // Crash logging must never become a second failure.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _localHost?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        try { _printQueue?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        try { _receiptPrintQueue?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        try { _cloudReconciliation?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        _cloudReconciliationHttpClient?.Dispose();
        base.OnExit(e);
    }
}
