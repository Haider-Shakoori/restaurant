using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.Desktop.Appearance;
using BusinessOS.Restaurant.Licensing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Restaurant.Desktop;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly LanDiagnosticsViewModel _diagnostics = new();
    private readonly RestaurantLicenseCoordinator _licenses = new();
    private readonly AuthSession _session;
    // Every navigation and refresh shares one generation; stale loads cannot replace a newer workspace.
    private readonly NavigationRequestGate _pageRequests = new();
    private string? _failedWorkspaceRoute;
    private readonly DesktopNoticeFeed _noticeFeed = new();
    private readonly List<DispatcherTimer> _noticeTimers = new();

    public string OperatorLabel => $"{_session.User.Name} · {_session.User.Role}";
    public bool CanViewDashboard => CanView("dashboard");
    public bool CanViewPos => CanView("pos");
    public bool CanViewTables => CanView("tables");
    public bool CanViewKitchen => CanView("kitchen");
    public bool CanViewMenu => CanView("menu");
    public bool CanViewInventory => CanView("inventory");
    public bool CanViewPurchases => CanView("purchases");
    public bool CanViewExpenses => CanView("expenses");
    public bool CanViewClosing => CanView("closing");
    public bool CanViewReports => CanView("reports");
    public bool CanViewUsers => CanView("users");
    public bool CanViewSettings => CanView("settings");

    private bool CanView(string route) =>
        RestaurantWorkspaceRoutes.CanOpen(_session.User.Role, route);

    [ObservableProperty] private string _pageTitle = "Dashboard";
    [ObservableProperty] private string _pageSubtitle = "Restaurant overview and today's operations";
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _currentRoute = "dashboard";
    [ObservableProperty] private string _licenseText = "Checking license…";
    [ObservableProperty] private bool _hasWorkspaceError;
    [ObservableProperty] private string _workspaceErrorMessage = string.Empty;
    [ObservableProperty] private bool _notificationsOpen;
    [ObservableProperty] private int _unreadNotificationCount;

    public ObservableCollection<DesktopNotice> NotificationHistory { get; } = new();
    public ObservableCollection<DesktopNotice> ToastNotifications { get; } = new();
    public string NotificationBellLabel => UnreadNotificationCount > 0
        ? $"🔔 {UnreadNotificationCount}" : "🔔";

    public string NetworkMode => _diagnostics.NetworkMode;

    public string ThemeButtonText =>
        ThemeManager.Current == AppearanceTheme.Glass ? "◐  Glass" : "◐  Classic";

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand<string> NavigateCommand { get; }

    public IRelayCommand ToggleThemeCommand { get; }

    public IAsyncRelayCommand RetryWorkspaceCommand { get; }
    public IRelayCommand ToggleNotificationsCommand { get; }
    public IRelayCommand MarkNotificationsReadCommand { get; }

    public MainWindowViewModel(AuthSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        CurrentRoute = RestaurantWorkspaceRoutes.DefaultRoute(_session.User.Role);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        NavigateCommand = new AsyncRelayCommand<string>(NavigateAsync);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        RetryWorkspaceCommand = new AsyncRelayCommand(RetryWorkspaceAsync);
        ToggleNotificationsCommand = new RelayCommand(() => NotificationsOpen = !NotificationsOpen);
        MarkNotificationsReadCommand = new RelayCommand(MarkNotificationsRead);
        DesktopNoticeEvents.Posted += OnDesktopNotice;
    }

    partial void OnNotificationsOpenChanged(bool value)
    {
        if (value) MarkNotificationsRead();
    }

    partial void OnUnreadNotificationCountChanged(int value) =>
        OnPropertyChanged(nameof(NotificationBellLabel));

    private void MarkNotificationsRead()
    {
        _noticeFeed.MarkAllRead();
        NotificationHistory.Clear();
        foreach (var notice in _noticeFeed.History)
            NotificationHistory.Add(notice);
        UnreadNotificationCount = _noticeFeed.UnreadCount;
    }

    private void OnDesktopNotice(DesktopNoticeLevel level, string message)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
            return;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => OnDesktopNotice(level, message)));
            return;
        }

        var notice = _noticeFeed.Publish(level, message, DateTimeOffset.Now);
        if (notice is null)
            return;

        NotificationHistory.Insert(0, notice);
        if (NotificationHistory.Count > 100)
            NotificationHistory.RemoveAt(NotificationHistory.Count - 1);
        UnreadNotificationCount = _noticeFeed.UnreadCount;
        ToastNotifications.Insert(0, notice);
        while (ToastNotifications.Count > 3)
            ToastNotifications.RemoveAt(ToastNotifications.Count - 1);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(level == DesktopNoticeLevel.Error ? 9 : 5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _noticeTimers.Remove(timer);
            ToastNotifications.Remove(notice);
        };
        _noticeTimers.Add(timer);
        timer.Start();
    }

    public void ReleaseNotifications()
    {
        DesktopNoticeEvents.Posted -= OnDesktopNotice;
        foreach (var timer in _noticeTimers)
            timer.Stop();
        _noticeTimers.Clear();
    }

    public async Task InitializeAsync()
    {
        // Startup loads the dashboard once; the toolbar refresh additionally reloads the active page.
        await RefreshDiagnosticsAsync();
        await NavigateAsync(RestaurantWorkspaceRoutes.DefaultRoute(_session.User.Role));
    }

    private async Task RefreshAsync()
    {
        // Capture intent before diagnostics perform any I/O, so this refresh cannot
        // later overwrite a newer workspace navigation.
        var requestId = _pageRequests.Begin();
        await RefreshDiagnosticsAsync();
        if (!_pageRequests.IsCurrent(requestId))
            return;

        var route = CurrentRoute;
        try
        {
            var refreshedPage = await RestaurantOperationalPages.CreateAsync(route, _diagnostics);
            if (_pageRequests.IsCurrent(requestId) && CurrentRoute == route && CanView(route))
            {
                CurrentPage = refreshedPage;
                ClearWorkspaceError();
            }
        }
        catch (Exception exception)
        {
            HandleWorkspaceLoadFailure(route, PageTitle, requestId, exception);
        }
    }

    private async Task RefreshDiagnosticsAsync()
    {
        // Diagnostics are advisory. Intermittent LAN/cloud failures must not stop
        // the local POS/KDS page from reopening when its SQLite data is available.
        try
        {
            await _diagnostics.RefreshCommand.ExecuteAsync(null);
            OnPropertyChanged(nameof(NetworkMode));
        }
        catch (Exception exception)
        {
            App.LogRecoverableException("workspace-network-diagnostics", exception);
        }

        try
        {
            var license = await _licenses.GetStatusAsync();
            LicenseText = license.IsValid
                ? $"{license.PlanName} · {license.DaysRemaining} day(s)"
                : "License expired";
        }
        catch (Exception exception)
        {
            // Keep the last known display label; never fabricate a fresh valid lease.
            App.LogRecoverableException("workspace-license-diagnostics", exception);
            LicenseText = "License status unavailable";
        }
    }

    private Task RetryWorkspaceAsync()
    {
        // Retry exactly the failed workspace through the same role-aware route
        // policy; do not accidentally refresh a different visible page instead.
        var failedRoute = _failedWorkspaceRoute;
        return failedRoute is null ? Task.CompletedTask : NavigateAsync(failedRoute);
    }

    private void ClearWorkspaceError()
    {
        _failedWorkspaceRoute = null;
        WorkspaceErrorMessage = string.Empty;
        HasWorkspaceError = false;
    }

    private void HandleWorkspaceLoadFailure(
        string route, string pageTitle, long requestId, Exception exception)
    {
        if (!_pageRequests.IsCurrent(requestId))
            return; // Stale errors should never hide the newer successful page.

        App.LogRecoverableException("workspace-load-" + route, exception);
        _failedWorkspaceRoute = route;
        WorkspaceErrorMessage = $"Could not load {pageTitle}. Your current screen is unchanged. " +
            "Retry the workspace or check the local diagnostic log.";
        HasWorkspaceError = true;
        DesktopNoticeEvents.Publish(DesktopNoticeLevel.Warning, WorkspaceErrorMessage);
    }

    private void ToggleTheme()
    {
        var next = ThemeManager.Toggle();
        new AppearanceSettingsStore().Save(next);
        OnPropertyChanged(nameof(ThemeButtonText));
    }

    private async Task NavigateAsync(string? key)
    {
        var route = key ?? RestaurantWorkspaceRoutes.DefaultRoute(_session.User.Role);
        if (!CanView(route))
            return; // Shell visibility is not authorization: reject direct route commands too.

        // A later refresh or navigation supersedes even a still-loading earlier route.
        var requestId = _pageRequests.Begin();

        var (pageTitle, pageSubtitle) = route switch
        {
            "dashboard" => ("Dashboard", "Restaurant overview and today's operations"),
            "pos" => ("POS & Orders", "Orders, menu and cashier workflow"),
            "tables" => ("Tables & Floor", "Live table occupancy and dining areas"),
            "kitchen" => ("Kitchen / KOT", "Kitchen tickets and preparation status"),
            "menu" => ("Menu & Products", "Menu catalog and availability"),
            "inventory" => ("Inventory", "Ingredients, stock and recipe consumption"),
            "purchases" => ("Purchases", "Suppliers, purchase orders and receiving"),
            "expenses" => ("Expenses", "Restaurant operating expenses"),
            "closing" => ("Daily Closing", "Cashier sessions and end-of-day controls"),
            "reports" => ("Reports & Accounting", "Sales, payments and performance"),
            "users" => ("Users & Roles", "Restaurant staff access"),
            "settings" => ("Settings", "LAN devices, local host and synchronization"),
            _ => ("Restaurant", "Operational workspace"),
        };

        // Commit the header, content and active route together only after a
        // successful load. If SQLite or a device is unavailable, the existing
        // page must not appear under a misleading new navigation title.
        try
        {
            var page = await RestaurantOperationalPages.CreateAsync(route, _diagnostics);
            if (!_pageRequests.IsCurrent(requestId))
                return;

            PageTitle = pageTitle;
            PageSubtitle = pageSubtitle;
            CurrentPage = page;
            CurrentRoute = route;
            ClearWorkspaceError();
        }
        catch (Exception exception)
        {
            HandleWorkspaceLoadFailure(route, pageTitle, requestId, exception);
        }
    }
}
