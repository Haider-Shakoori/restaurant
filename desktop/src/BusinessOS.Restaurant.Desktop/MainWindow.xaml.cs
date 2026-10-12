using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
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
    private string? _fullScreenRoute;
    private WindowState _originalWindowState;
    private WindowStyle _originalWindowStyle;
    private ResizeMode _originalResizeMode;

    public MainWindow(AuthSession session)
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel(session);
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnWorkspaceChanged;
        OperationalWorkspaceChrome.FullScreenToggleRequested += OnChromeFullScreenToggle;
        OperationalWorkspaceChrome.ShortcutsRequested += OnChromeShortcuts;
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
        // A narrow cashier display collapses the primary route links into one
        // menu; grouped dropdowns, the account menu and All Modules stay
        // reachable right of it without overlapping.
        var compact = ActualWidth < 1400;
        BrandText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        OperatorBadge.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        TopNavLinks.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        TopNavCompactButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        if (!compact) TopNavCompactMenu.IsOpen = false;
        // The old page header row is gone: its status and actions now live in
        // the top menu bar, so the workspace keeps the full content height.
        NetworkModeBadge.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        RefreshButton.Content = compact ? "↻" : "↻  Refresh";
        RefreshButton.ToolTip = "Refresh the current workspace";
    }

    private void OnUserMenuClick(object sender, RoutedEventArgs e) =>
        UserMenuPopup.IsOpen = !UserMenuPopup.IsOpen;

    private void OnNavMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var popup = button.Tag?.ToString() switch
        {
            "menu,recipes" => CatalogMenuPopup,
            "inventory,purchases,expenses,closing" => StockMenuPopup,
            "reports,users,settings" => ManageMenuPopup,
            _ => TopNavCompactMenu,
        };
        var open = !popup.IsOpen;
        CatalogMenuPopup.IsOpen = false;
        StockMenuPopup.IsOpen = false;
        ManageMenuPopup.IsOpen = false;
        TopNavCompactMenu.IsOpen = false;
        popup.IsOpen = open;
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
        OperationalWorkspaceChrome.FullScreenToggleRequested -= OnChromeFullScreenToggle;
        OperationalWorkspaceChrome.ShortcutsRequested -= OnChromeShortcuts;
        _viewModel.ReleaseNotifications();
    }

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.CurrentRoute)) return;
        // Both pages open in the ordinary resizable Windows shell.
        if (_posFullScreen && _viewModel.CurrentRoute != _fullScreenRoute)
            SetPosFullScreen(false);
        UpdateOperationalButtons();
    }

    private static bool IsOperationalRoute(string route) => route is "pos" or "kitchen";

    private void UpdateOperationalButtons()
    {
        var active = IsOperationalRoute(_viewModel.CurrentRoute);
        ExitPosButton.Visibility = active && _posFullScreen
            ? Visibility.Visible : Visibility.Collapsed;
        ExitPosButton.ToolTip = "Return to normal window (F11 or Esc)";
    }

    private void SetPosFullScreen(bool enabled)
    {
        if (_posFullScreen == enabled) return;
        if (enabled && !IsOperationalRoute(_viewModel.CurrentRoute)) return;

        if (enabled)
        {
            _originalWindowState = WindowState;
            _originalWindowStyle = WindowStyle;
            _originalResizeMode = ResizeMode;
            TopNavigationBar.Visibility = Visibility.Collapsed;
            WorkspaceContent.MaxWidth = double.PositiveInfinity;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            _fullScreenRoute = _viewModel.CurrentRoute;
            _posFullScreen = true;
        }
        else
        {
            TopNavigationBar.Visibility = Visibility.Visible;
            WorkspaceContent.MaxWidth = 1900;
            WindowState = WindowState.Normal;
            WindowStyle = _originalWindowStyle;
            ResizeMode = _originalResizeMode;
            WindowState = _originalWindowState;
            _fullScreenRoute = null;
            _posFullScreen = false;
        }
        UpdateOperationalButtons();
    }

    private void OnExitPosClick(object sender, RoutedEventArgs e) =>
        SetPosFullScreen(false);

    private void OnChromeFullScreenToggle()
    {
        // Keys/Full screen now ride with the POS and Kitchen action rows.
        if (IsOperationalRoute(_viewModel.CurrentRoute))
        {
            SetPosFullScreen(!_posFullScreen);
        }
    }

    private void OnChromeShortcuts() => ShowOperationalShortcuts();

    private void ShowOperationalShortcuts()
    {
        if (!IsOperationalRoute(_viewModel.CurrentRoute)) return;
        var pos = _viewModel.CurrentRoute == "pos";
        var keys = pos
            ? "F1  Shortcut guide\nF2  New order\nF3  Active orders\nF4  View ticket\nF5  Refresh POS\nF6  Item options\nF7  KOT actions\nF8  Payments\nF9  Send KOT\nF11  Full screen / restore\nCtrl+F  Search menu\nEsc  Close dialog / restore window"
            : "F1  Shortcut guide\nF2  Advance selected KOT dish (Ready / Start / Expo)\nF5  Refresh Kitchen board\nF11  Full screen / restore\nEsc  Restore normal window";
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = pos ? "POS keyboard shortcuts" : "Kitchen / KOT keyboard shortcuts",
            FontSize = 20, FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 14),
        });
        panel.Children.Add(new TextBlock
        {
            Text = keys, FontSize = 15, LineHeight = 28,
            TextWrapping = TextWrapping.Wrap,
        });
        var dialog = new Window
        {
            Title = "Keyboard shortcuts",
            Width = 445, Height = pos ? 510 : 390,
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Owner = this,
        };
        DesktopGlassDialog.ShowDialog(dialog);
    }

    private static T? FindDescendant<T>(DependencyObject parent, Func<T, bool> predicate)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found && predicate(found)) return found;
            var next = FindDescendant(child, predicate);
            if (next is not null) return next;
        }
        return null;
    }

    private bool TryOperationalClick(string? name = null, string? caption = null)
    {
        if (_viewModel.CurrentPage is not FrameworkElement page) return false;
        var button = FindDescendant<Button>(page, x =>
            (name is null || x.Name == name) &&
            (caption is null || x.Content?.ToString() == caption));
        if (button is not { IsEnabled: true, IsVisible: true }) return false;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        return true;
    }

    private async void OnWorkspaceKeyDown(object sender, KeyEventArgs e)
    {
        var route = _viewModel.CurrentRoute;
        if (!IsOperationalRoute(route)) return;
        if (e.Key == Key.Escape)
        {
            if (route == "pos" &&
                _viewModel.CurrentPage is FrameworkElement page &&
                page.Tag is Func<bool> closeModal && closeModal())
            {
                e.Handled = true;
                return;
            }
            if (_posFullScreen)
            {
                SetPosFullScreen(false);
                e.Handled = true;
            }
            return; // normal window: let Esc behave normally, never leave page
        }
        if (e.Key == Key.F11)
        {
            SetPosFullScreen(!_posFullScreen);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F1)
        {
            ShowOperationalShortcuts();
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F &&
            route == "pos" && _viewModel.CurrentPage is FrameworkElement searchPage)
        {
            var search = FindDescendant<TextBox>(searchPage, x => x.Name == "PosMenuSearchBox");
            if (search is not null)
            {
                search.Focus();
                search.SelectAll();
                e.Handled = true;
            }
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.None) return;

        if (e.Key == Key.F5)
        {
            e.Handled = true;
            await _viewModel.RefreshCommand.ExecuteAsync(null);
            return;
        }

        var action = route == "pos" ? e.Key switch
        {
            Key.F2 => "PosOpenNewOrderModal",
            Key.F3 => "PosOpenActiveOrdersModal",
            Key.F4 => "PosOpenTicketModal",
            Key.F6 => "PosOpenItemOptionsModal",
            Key.F7 => "PosOpenKitchenModal",
            Key.F8 => "PosOpenPaymentModal",
            Key.F9 => "PosSendKotButton",
            _ => null,
        } : null;
        if (action is not null)
        {
            e.Handled = TryOperationalClick(name: action);
            return;
        }
        if (route == "kitchen")
        {
            if (e.Key == Key.F2)
                e.Handled = TryOperationalClick(name: "KotTopReadyButton");
        }
    }

    private async void OnSwitchOperatorClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await new WindowsSessionStore().ClearAsync();
            IsEnabled = false;
            var activation = await new WindowsActivationStore().LoadAsync()
                ?? throw new InvalidOperationException("Restaurant activation could not be verified.");
            var connection = await new ConnectionSettingsStore().LoadAsync();
            var signIn = new OperatorSignInWindow(
                activation.Snapshot.TenantId, activation.TenantBaseUrl, activation.Snapshot.TenantName)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            if (DesktopGlassDialog.ShowDialog(signIn) != true || signIn.SignedInSession is null)
            { Close(); return; }

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
            DesktopGlassDialog.Notice(
                "You have been logged out. Restart to sign in again.",
                "BusinessOS Restaurant", this);
            Close();
        }
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        ApplyResponsiveLayout();
        UpdateOperationalButtons();

        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            _initialized = false;
            App.LogRecoverableException("Main window initialization", exception);
            DesktopGlassDialog.Notice(
                "Restaurant Desktop could not finish loading this screen. The application will remain open.\n\n" +
                "A diagnostic log was written under LocalAppData\\BusinessOS\\Restaurant\\logs.",
                "BusinessOS Restaurant", this);
        }
    }
}
