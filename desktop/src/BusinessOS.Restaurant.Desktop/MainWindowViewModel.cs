using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Restaurant.Desktop;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly LanDiagnosticsViewModel _diagnostics = new();

    [ObservableProperty] private string _pageTitle = "Dashboard";
    [ObservableProperty] private string _pageSubtitle = "Restaurant overview and today's operations";
    [ObservableProperty] private object? _currentPage;

    public string NetworkMode => _diagnostics.NetworkMode;
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand<string> NavigateCommand { get; }

    public MainWindowViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        NavigateCommand = new AsyncRelayCommand<string>(NavigateAsync);
    }

    public async Task InitializeAsync()
    {
        await RefreshAsync();
        await NavigateAsync("dashboard");
    }

    private async Task RefreshAsync()
    {
        await _diagnostics.RefreshCommand.ExecuteAsync(null);
        OnPropertyChanged(nameof(NetworkMode));
    }

    private async Task NavigateAsync(string? key)
    {
        var route = key ?? "dashboard";
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

        CurrentPage = await RestaurantOperationalPages.CreateAsync(route, _diagnostics);
    }
}