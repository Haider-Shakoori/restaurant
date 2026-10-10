using System.ComponentModel;
using System.Windows.Input;
using System.Windows;
using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.Desktop.Appearance;
using BusinessOS.Restaurant.Licensing;

namespace BusinessOS.Restaurant.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private bool _initialized;
    private bool _posFullScreen;
    private WindowState _originalWindowState;
    private WindowStyle _originalWindowStyle;
    private ResizeMode _originalResizeMode;

    public MainWindow(AuthSession session)
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel(session);
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnWorkspaceChanged;
        PreviewKeyDown += OnWorkspaceKeyDown;
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
        HeaderSubtitle.Visibility = Visibility.Collapsed;
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
        _viewModel.PropertyChanged -= OnWorkspaceChanged;
        PreviewKeyDown -= OnWorkspaceKeyDown;
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _viewModel.ReleaseNotifications();
    }

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentRoute))
            SetPosFullScreen(_viewModel.CurrentRoute == "pos");
    }

    private void SetPosFullScreen(bool enabled)
    {
        if (_posFullScreen == enabled)
            return;

        if (enabled)
        {
            _originalWindowState = WindowState;
            _originalWindowStyle = WindowStyle;
            _originalResizeMode = ResizeMode;
            SidebarContainer.Visibility = Visibility.Collapsed;
            SidebarColumn.Width = new GridLength(0);
            ShellHeader.Visibility = Visibility.Collapsed;
            TopBarRow.Height = new GridLength(0);
            ExitPosButton.Visibility = Visibility.Visible;
            WorkspaceContent.MaxWidth = double.PositiveInfinity;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            _posFullScreen = true;
        }
        else
        {
            SidebarContainer.Visibility = Visibility.Visible;
            SidebarColumn.Width = new GridLength(252);
            ShellHeader.Visibility = Visibility.Visible;
            TopBarRow.Height = new GridLength(76);
            ExitPosButton.Visibility = Visibility.Collapsed;
            WorkspaceContent.MaxWidth = 1900;
            WindowState = WindowState.Normal;
            WindowStyle = _originalWindowStyle;
            ResizeMode = _originalResizeMode;
            WindowState = _originalWindowState;
            _posFullScreen = false;
        }
    }

    private async void OnExitPosClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.NavigateCommand.ExecuteAsync("dashboard");
    }

    private async void OnWorkspaceKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.CurrentRoute != "pos")
            return;
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            await _viewModel.NavigateCommand.ExecuteAsync("dashboard");
        }
        else if (e.Key == Key.F11)
        {
            e.Handled = true;
            SetPosFullScreen(!_posFullScreen);
        }
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
