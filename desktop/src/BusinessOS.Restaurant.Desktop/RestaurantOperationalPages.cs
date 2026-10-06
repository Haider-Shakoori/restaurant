using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.Desktop;

internal static class RestaurantOperationalPages
{
    public static async Task<FrameworkElement> CreateAsync(string route, LanDiagnosticsViewModel diagnostics)
    {
        return route switch
        {
            "dashboard" => await DashboardAsync(diagnostics),
            "tables" => await OperationalActionViews.TablesAsync(),
            "kitchen" => await OperationalActionViews.KitchenAsync(),
            "pos" => await OperationalActionViews.PosAsync(),
            "menu" => await MenuAsync(),
            "inventory" => await InventoryAsync(),
            "purchases" => await PurchasesAsync(),
            "users" => await UsersAsync(),
            "expenses" => Expenses(),
            "closing" => await OperationalActionViews.ClosingAsync(),
            "reports" => Reports(diagnostics),
            "settings" => Settings(diagnostics),
            _ => Placeholder(route),
        };
    }

    private static async Task<FrameworkElement> DashboardAsync(LanDiagnosticsViewModel diagnostics)
    {
        var factory = new LocalDatabaseFactory();
        await factory.EnsureCreatedAsync();
        await using var db = factory.Create();
        var from = DateTimeOffset.UtcNow.Date;
        var to = from.AddDays(1);
        var openOrders = await db.Orders.CountAsync(x => x.Status != "closed");
        var activeTables = await db.DiningTables.CountAsync(x => x.IsActive && x.Status != "available");
        var activeKot = await db.KitchenTickets.CountAsync(x => x.Status == "queued" || x.Status == "preparing" || x.Status == "ready");
        var sales = await db.Bills.Where(x => x.IssuedAt >= from && x.IssuedAt < to).SumAsync(x => (decimal?)x.Total) ?? 0m;

        var panel = Stack();
        panel.Children.Add(Cards(
            ("TODAY'S SALES", $"AFN {sales:N2}"),
            ("OPEN ORDERS", openOrders.ToString()),
            ("ACTIVE TABLES", activeTables.ToString()),
            ("KITCHEN TICKETS", activeKot.ToString())));
        panel.Children.Add(Card("Local-first status", $"{diagnostics.NetworkMode}\n{diagnostics.TerminalSummary}\n{diagnostics.StatusMessage}"));
        return Scroll(panel);
    }

    private static async Task<FrameworkElement> TablesAsync()
    {
        var factory = new LocalDatabaseFactory();
        await factory.EnsureCreatedAsync();
        await using var db = factory.Create();
        var rows = await (from table in db.DiningTables.AsNoTracking()
                          join area in db.DiningAreas.AsNoTracking() on table.DiningAreaId equals area.Id
                          where table.IsActive
                          orderby area.SortOrder, table.Name
                          select new TableRow(table.Id, area.Name, table.Code, table.Name, table.Capacity, table.Status))
                         .ToListAsync();

        var grid = GridFor(rows);
        grid.Columns.Add(Column("Area", nameof(TableRow.Area), 160));
        grid.Columns.Add(Column("Table", nameof(TableRow.Name), 200));
        grid.Columns.Add(Column("Code", nameof(TableRow.Code), 110));
        grid.Columns.Add(Column("Seats", nameof(TableRow.Capacity), 90));
        grid.Columns.Add(Column("Status", nameof(TableRow.Status), 150));
        return Section("Dining floor", "Live local table state used by waiter devices and desktop orders.", grid);
    }

    private static async Task<FrameworkElement> KitchenAsync()
    {
        var factory = new LocalDatabaseFactory();
        await factory.EnsureCreatedAsync();
        await using var db = factory.Create();
        var rows = await (from ticket in db.KitchenTickets.AsNoTracking()
                          join station in db.KitchenStations.AsNoTracking() on ticket.KitchenStationId equals station.Id
                          where ticket.Status == "queued" || ticket.Status == "preparing" || ticket.Status == "ready"
                          orderby ticket.QueuedAt
                          select new KitchenRow(ticket.Id, ticket.TicketNumber, station.Name, ticket.Status, ticket.QueuedAt))
                         .ToListAsync();

        var grid = GridFor(rows);
        grid.Columns.Add(Column("KOT", nameof(KitchenRow.TicketNumber), 220));
        grid.Columns.Add(Column("Station", nameof(KitchenRow.Station), 180));
        grid.Columns.Add(Column("Status", nameof(KitchenRow.Status), 130));
        grid.Columns.Add(Column("Queued", nameof(KitchenRow.QueuedAt), 220));
        return Section("Kitchen display", "Tickets created locally by desktop or LAN-connected waiter devices.", grid);
    }

    private static async Task<FrameworkElement> PosAsync()
    {
        var factory = new LocalDatabaseFactory();
        await factory.EnsureCreatedAsync();
        await using var db = factory.Create();
        var orders = await db.Orders.AsNoTracking().Where(x => x.Status != "closed").OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new OrderRow(x.Id, x.ClientOrderId, x.WaiterName, x.Status, x.GuestCount, x.Total, x.UpdatedAtUtc)).Take(100).ToListAsync();

        var grid = GridFor(orders);
        grid.Columns.Add(Column("Order", nameof(OrderRow.ClientOrderId), 220));
        grid.Columns.Add(Column("Waiter", nameof(OrderRow.Waiter), 160));
        grid.Columns.Add(Column("Guests", nameof(OrderRow.Guests), 90));
        grid.Columns.Add(Column("Status", nameof(OrderRow.Status), 120));
        grid.Columns.Add(Column("Total AFN", nameof(OrderRow.Total), 130));
        grid.Columns.Add(Column("Updated", nameof(OrderRow.UpdatedAt), 210));
        return Section("POS & active orders", "Desktop and mobile orders share the same local SQLite order store.", grid);
    }

    private static async Task<FrameworkElement> MenuAsync()
    {
        var factory = new LocalDatabaseFactory();
        await factory.EnsureCreatedAsync();
        await using var db = factory.Create();
        var rows = await (from item in db.MenuItems.AsNoTracking()
                          join category in db.MenuCategories.AsNoTracking() on item.MenuCategoryId equals category.Id into categories
                          from category in categories.DefaultIfEmpty()
                          orderby item.SortOrder, item.Name
                          select new MenuRow(item.Sku ?? "", item.Name, category == null ? "Uncategorized" : category.Name, item.Price, item.Currency, item.IsAvailable)).ToListAsync();
        var grid = GridFor(rows);
        grid.Columns.Add(Column("SKU", nameof(MenuRow.Sku), 130));
        grid.Columns.Add(Column("Item", nameof(MenuRow.Name), 260));
        grid.Columns.Add(Column("Category", nameof(MenuRow.Category), 180));
        grid.Columns.Add(Column("Price", nameof(MenuRow.Price), 120));
        grid.Columns.Add(Column("Currency", nameof(MenuRow.Currency), 90));
        grid.Columns.Add(Column("Available", nameof(MenuRow.Available), 100));
        return Section("Menu catalog", "The same local menu is served to LAN-connected waiter devices.", grid);
    }

    private static async Task<FrameworkElement> InventoryAsync()
    {
        var factory = new LocalDatabaseFactory();
        await factory.EnsureCreatedAsync();
        await using var db = factory.Create();
        var rows = await db.InventoryItems.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new InventoryRow(x.Sku, x.Name, x.BaseUnit, x.PurchaseUnit ?? x.BaseUnit, x.ReorderLevel)).ToListAsync();
        var grid = GridFor(rows);
        grid.Columns.Add(Column("SKU", nameof(InventoryRow.Sku), 140));
        grid.Columns.Add(Column("Ingredient", nameof(InventoryRow.Name), 260));
        grid.Columns.Add(Column("Base unit", nameof(InventoryRow.BaseUnit), 120));
        grid.Columns.Add(Column("Purchase unit", nameof(InventoryRow.PurchaseUnit), 140));
        grid.Columns.Add(Column("Reorder level", nameof(InventoryRow.ReorderLevel), 130));
        return Section("Inventory", "Local stock and recipe consumption remain available without internet.", grid);
    }

    private static async Task<FrameworkElement> PurchasesAsync()
    {
        var factory = new LocalDatabaseFactory();
        await factory.EnsureCreatedAsync();
        await using var db = factory.Create();
        var rows = await (from po in db.PurchaseOrders.AsNoTracking()
                          join supplier in db.Suppliers.AsNoTracking() on po.SupplierId equals supplier.Id
                          orderby po.OrderedAt descending
                          select new PurchaseRow(po.PoNumber, supplier.Name, po.Status, po.EstimatedTotal, po.OrderedAt)).Take(100).ToListAsync();
        var grid = GridFor(rows);
        grid.Columns.Add(Column("PO", nameof(PurchaseRow.Number), 190));
        grid.Columns.Add(Column("Supplier", nameof(PurchaseRow.Supplier), 240));
        grid.Columns.Add(Column("Status", nameof(PurchaseRow.Status), 120));
        grid.Columns.Add(Column("Estimated AFN", nameof(PurchaseRow.Total), 150));
        grid.Columns.Add(Column("Ordered", nameof(PurchaseRow.OrderedAt), 210));
        return Section("Purchases", "Purchase orders and receiving use the desktop local database.", grid);
    }


    private static async Task<FrameworkElement> UsersAsync()
    {
        var factory = new LocalDatabaseFactory();
        await factory.EnsureCreatedAsync();
        await using var db = factory.Create();
        var staff = await db.StaffUsers.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new StaffRow(x.Name, x.Email, x.Role, x.IsActive)).ToListAsync();
        var shifts = await db.WaiterShifts.AsNoTracking().OrderByDescending(x => x.StartedAt).Take(100)
            .Select(x => new ShiftRow(x.UserName, x.Role, x.Status, x.StartedAt, x.EndedAt, x.BreakMinutes)).ToListAsync();

        var panel = Stack();
        panel.Children.Add(Card("Restaurant staff", "Restaurant roles and access are shown from the local operational store. Pharmacy roles are not reused."));
        var staffGrid = GridFor(staff);
        staffGrid.MinHeight = 260;
        staffGrid.Columns.Add(Column("Name", nameof(StaffRow.Name), 220));
        staffGrid.Columns.Add(Column("Email", nameof(StaffRow.Email), 260));
        staffGrid.Columns.Add(Column("Restaurant role", nameof(StaffRow.Role), 160));
        staffGrid.Columns.Add(Column("Active", nameof(StaffRow.Active), 100));
        panel.Children.Add(staffGrid);

        panel.Children.Add(new TextBlock { Text = "Staff shifts", FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0,20,0,10) });
        var shiftGrid = GridFor(shifts);
        shiftGrid.MinHeight = 260;
        shiftGrid.Columns.Add(Column("Staff", nameof(ShiftRow.Name), 200));
        shiftGrid.Columns.Add(Column("Role", nameof(ShiftRow.Role), 140));
        shiftGrid.Columns.Add(Column("Status", nameof(ShiftRow.Status), 110));
        shiftGrid.Columns.Add(Column("Started", nameof(ShiftRow.StartedAt), 190));
        shiftGrid.Columns.Add(Column("Ended", nameof(ShiftRow.EndedAt), 190));
        shiftGrid.Columns.Add(Column("Break min", nameof(ShiftRow.BreakMinutes), 100));
        panel.Children.Add(shiftGrid);
        return Scroll(panel);
    }

    private static FrameworkElement Expenses()
        => Section("Expenses", "Restaurant operating expenses", Card("Backend boundary", "The desktop local schema does not currently contain an expense entity/service. Phase 4 will not invent pharmacy-style expense logic; this screen remains explicitly unavailable until the restaurant expense contract exists."));

    private static FrameworkElement Reports(LanDiagnosticsViewModel diagnostics)
    {
        var panel = Stack();
        panel.DataContext = diagnostics.Reports;
        panel.Children.Add(Card("Reports & accounting", "Use the existing local reporting service. Financial data stays available on the desktop during internet outages."));
        var button = new Button { Content = "Run report", Width = 140, Height = 38, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
        button.SetBinding(Button.CommandProperty, new Binding("RefreshReportsCommand"));
        panel.Children.Add(button);
        return Scroll(panel);
    }

    private static FrameworkElement Settings(LanDiagnosticsViewModel diagnostics)
    {
        var grid = GridFor(diagnostics.Terminals);
        grid.Columns.Add(Column("Device", "DisplayName", 220));
        grid.Columns.Add(Column("Type", "ClientType", 120));
        grid.Columns.Add(Column("User", "UserName", 180));
        grid.Columns.Add(Column("Role", "UserRole", 120));
        grid.Columns.Add(Column("Status", "Status", 120));
        grid.Columns.Add(Column("IP address", "LastIpAddress", 160));
        grid.Columns.Add(Column("Last seen", "LastSeenAtUtc", 210));
        return Section("LAN & mobile devices", $"{diagnostics.NetworkMode} · {diagnostics.TerminalSummary}\nMobile devices connect to the desktop host over the restaurant LAN/Wi-Fi.", grid);
    }

    private static FrameworkElement Placeholder(string route) => Section(route, "This management screen is part of the operational UI completion phase.", new TextBlock { Text = "The local service layer already exists; the full editing surface is being connected.", Foreground = System.Windows.Media.Brushes.SlateGray });

    private static StackPanel Stack() => new() { Margin = new Thickness(0) };
    private static ScrollViewer Scroll(UIElement child) => new() { Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    private static Border Cards(params (string Label, string Value)[] values)
    {
        var wrap = new WrapPanel();
        foreach (var value in values)
            wrap.Children.Add(Card(value.Label, value.Value, 235));
        return new Border { Child = wrap, Margin = new Thickness(0, 0, 0, 16) };
    }

    private static Border Card(string title, string value, double width = double.NaN) => new()
    {
        Background = System.Windows.Media.Brushes.White,
        CornerRadius = new CornerRadius(16),
        Padding = new Thickness(18),
        Margin = new Thickness(0, 0, 12, 12),
        Width = width,
        Child = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.SlateGray },
                new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0,8,0,0), TextWrapping = TextWrapping.Wrap }
            }
        }
    };

    private static FrameworkElement Section(string title, string subtitle, UIElement content)
    {
        var panel = Stack();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = subtitle, Foreground = System.Windows.Media.Brushes.SlateGray, Margin = new Thickness(0,4,0,16), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(content);
        return Scroll(panel);
    }

    private static DataGrid GridFor(object items) => new()
    {
        ItemsSource = (System.Collections.IEnumerable)items,
        AutoGenerateColumns = false,
        IsReadOnly = true,
        CanUserAddRows = false,
        MinHeight = 430,
        Background = System.Windows.Media.Brushes.White,
        BorderThickness = new Thickness(0)
    };

    private static DataGridTextColumn Column(string header, string property, double width) => new() { Header = header, Binding = new Binding(property), Width = width };

    private sealed record TableRow(string Id, string Area, string Code, string Name, int Capacity, string Status);
    private sealed record KitchenRow(string Id, string TicketNumber, string Station, string Status, DateTimeOffset QueuedAt);
    private sealed record OrderRow(string Id, string ClientOrderId, string Waiter, string Status, int Guests, decimal Total, DateTimeOffset UpdatedAt);
    private sealed record MenuRow(string Sku, string Name, string Category, decimal Price, string Currency, bool Available);
    private sealed record InventoryRow(string Sku, string Name, string BaseUnit, string PurchaseUnit, decimal ReorderLevel);
    private sealed record PurchaseRow(string Number, string Supplier, string Status, decimal Total, DateTimeOffset OrderedAt);
    private sealed record StaffRow(string Name, string Email, string Role, bool Active);
    private sealed record ShiftRow(string Name, string Role, string Status, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, int BreakMinutes);
}