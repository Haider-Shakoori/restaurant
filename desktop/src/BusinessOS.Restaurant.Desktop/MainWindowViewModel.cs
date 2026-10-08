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

    public string NetworkMode => _diagnostics.NetworkMode;

    public string ThemeButtonText =>
        ThemeManager.Current == AppearanceTheme.Glass ? "◐  Glass" : "◐  Classic";

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand<string> NavigateCommand { get; }

    public IRelayCommand ToggleThemeCommand { get; }

    public MainWindowViewModel(AuthSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        CurrentRoute = RestaurantWorkspaceRoutes.DefaultRoute(_session.User.Role);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        NavigateCommand = new AsyncRelayCommand<string>(NavigateAsync);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
    }

    public async Task InitializeAsync()
    {
        // Startup loads the dashboard once; the toolbar refresh additionally reloads the active page.
        await RefreshDiagnosticsAsync();
        await NavigateAsync(RestaurantWorkspaceRoutes.DefaultRoute(_session.User.Role));
    }

    private async Task RefreshAsync()
    {
        await RefreshDiagnosticsAsync();

        // Recreate the active operational page from the local store. Preserve the visible page
        // if loading fails, and never overwrite a newer navigation that completed meanwhile.
        var route = CurrentRoute;
        var refreshedPage = await RestaurantOperationalPages.CreateAsync(route, _diagnostics);
        if (CurrentRoute == route)
        {
            CurrentPage = refreshedPage;
        }
    }

    private async Task RefreshDiagnosticsAsync()
    {
        await _diagnostics.RefreshCommand.ExecuteAsync(null);
        OnPropertyChanged(nameof(NetworkMode));

        var license = await _licenses.GetStatusAsync();
        LicenseText = license.IsValid
            ? $"{license.PlanName} · {license.DaysRemaining} day(s)"
            : "License expired";
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

        (PageTitle, PageSubtitle) = route switch
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

        var page = await RestaurantOperationalPages.CreateAsync(route, _diagnostics);
        CurrentPage = page;
        CurrentRoute = route;
    }
}
