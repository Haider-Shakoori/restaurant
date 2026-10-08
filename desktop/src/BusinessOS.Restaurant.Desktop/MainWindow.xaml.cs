using System.Windows;
using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.Desktop.Appearance;
using BusinessOS.Restaurant.Licensing;

namespace BusinessOS.Restaurant.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private bool _initialized;

    public MainWindow(AuthSession session)
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel(session);
        DataContext = _viewModel;
        Loaded += OnLoadedAsync;
        Closed += OnClosed;
        ThemeManager.ThemeChanged += OnThemeChanged;
        UpdateBackdropVisibility(ThemeManager.Current);
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsLoaded) ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        // A narrow cashier display must keep route/actions accessible without
        // top-bar collisions. The full details return automatically when resized.
        var compact = ActualWidth < 1240;
        OperatorBadge.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        HeaderSubtitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SwitchOperatorButton.Content = compact ? "Switch" : "Switch operator";
        RefreshButton.Content = compact ? "↻" : "↻  Refresh";
        RefreshButton.ToolTip = "Refresh the current workspace";
    }

    private void OnThemeChanged(AppearanceTheme theme)
    {
        // Both modes share the same layout; only Glass renders the photographic backdrop.
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateBackdropVisibility(theme));
            return;
        }

        UpdateBackdropVisibility(theme);
    }

    private void UpdateBackdropVisibility(AppearanceTheme theme)
    {
        RestaurantBackdrop.Visibility = theme == AppearanceTheme.Glass
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private async void OnSwitchOperatorClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var activation = await new WindowsActivationStore().LoadAsync()
                ?? throw new InvalidOperationException("Restaurant activation could not be verified.");
            var connection = await new ConnectionSettingsStore().LoadAsync();
            var signIn = new OperatorSignInWindow(
                activation.Snapshot.TenantId, connection?.TenantBaseUrl)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            if (signIn.ShowDialog() != true || signIn.SignedInSession is null)
                return;

            // Never reuse a view model, navigation authorization or cached pages
            // across operators on a shared cashier workstation.
            var next = new MainWindow(signIn.SignedInSession);
            System.Windows.Application.Current.MainWindow = next;
            next.Show();
            Close();
        }
        catch (Exception exception)
        {
            App.LogRecoverableException("switch-operator", exception);
            MessageBox.Show(
                "Could not switch operator. Your existing workspace is unchanged.",
                "BusinessOS Restaurant", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        ApplyResponsiveLayout();

        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            _initialized = false;
            App.LogRecoverableException("Main window initialization", exception);
            MessageBox.Show(
                "Restaurant Desktop could not finish loading this screen. The application will remain open.\n\n" +
                "A diagnostic log was written under LocalAppData\\BusinessOS\\Restaurant\\logs.",
                "BusinessOS Restaurant",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
