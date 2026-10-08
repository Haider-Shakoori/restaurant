using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.Desktop.Appearance;
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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            if (await InstallerLicenseBridge.TryHandleAsync(e.Args))
            {
                Shutdown(0);
                return;
            }
        }
        catch (Exception exception)
        {
            WriteCrashLog(exception, "installer-license-bridge");
            Shutdown(2);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        ApplyStoredAppearance();

        var licensing = new RestaurantLicenseCoordinator();
        RestaurantLicenseStatus licenseStatus;

        try
        {
            licenseStatus = await licensing.GetStatusAsync();
        }
        catch (Exception exception)
        {
            WriteCrashLog(exception, "license-state");
            licenseStatus = RestaurantLicenseStatus.Missing;
        }

        if (!licenseStatus.IsValid)
        {
            var activation = new ActivationWindow(licensing);
            var activated = activation.ShowDialog();

            if (activated != true)
            {
                Shutdown();
                return;
            }
        }

        // Activation authorizes this computer; an operator still needs a tenant-bound
        // identity. A protected prior session allows working during Internet outages.
        var activationState = await new WindowsActivationStore().LoadAsync();
        if (activationState is null)
        {
            MessageBox.Show("Restaurant activation is missing. Activate this computer before signing in.",
                "BusinessOS Restaurant", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        var sessions = new WindowsSessionStore();
        AuthSession? session = null;
        try
        {
            session = await sessions.LoadAsync();
        }
        catch (Exception exception)
        {
            WriteCrashLog(exception, "operator-session-load");
        }

        var licenseTenant = activationState.Snapshot.TenantId;
        if (session is not null &&
            (!string.Equals(session.TenantId, licenseTenant, StringComparison.Ordinal) ||
             session.User is null ||
             string.IsNullOrEmpty(RestaurantWorkspaceRoutes.DefaultRoute(session.User.Role))))
        {
            session = null;
            await sessions.ClearAsync();
        }

        if (session is null)
        {
            var configured = await new ConnectionSettingsStore().LoadAsync();
            var signIn = new OperatorSignInWindow(licenseTenant, configured?.TenantBaseUrl);
            if (signIn.ShowDialog() != true || signIn.SignedInSession is null)
            {
                Shutdown();
                return;
            }

            session = signIn.SignedInSession;
        }

        var window = new MainWindow(session);
        MainWindow = window;
        window.Show();

        _ = InitializeServicesSafelyAsync();
    }

    private static void ApplyStoredAppearance()
    {
        var settings = new AppearanceSettingsStore().Load();
        var theme = Enum.TryParse<AppearanceTheme>(settings.Theme, true, out var parsed)
            ? parsed
            : AppearanceTheme.Glass;
        ThemeManager.Apply(theme);
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
