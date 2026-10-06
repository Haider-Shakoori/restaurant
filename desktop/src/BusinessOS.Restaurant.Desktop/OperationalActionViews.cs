using System.Windows;
using System.Windows.Controls;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.Desktop;

internal static class OperationalActionViews
{
    public static async Task<FrameworkElement> PosAsync()
    {
        var factory = new LocalDatabaseFactory();
        await factory.EnsureCreatedAsync();
        await using var db = factory.Create();

        var tables = await (from table in db.DiningTables.AsNoTracking()
                            where table.IsActive
                            orderby table.Name
                            select new Choice(table.Id, $"{table.Name} ({table.Code})")).ToListAsync();
        var menu = await db.MenuItems.AsNoTracking().Where(x => x.IsAvailable).OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new MenuChoice(x.Id, x.Name, x.Price)).ToListAsync();
        var orders = await db.Orders.AsNoTracking().Where(x => x.Status != "closed").OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new OrderChoice(x.Id, x.ClientOrderId, x.WaiterName, x.Status, x.GuestCount, x.Total)).Take(100).ToListAsync();

        var workflow = new DesktopRestaurantWorkflowService();
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var form = new StackPanel();
        form.Children.Add(Header("New local order", "Creates the same local order used by LAN-connected waiter devices."));
        var tableBox = Combo(tables, "Label");
        var guestBox = new TextBox { Text = "1", Margin = new Thickness(0, 4, 0, 10), Height = 34 };
        var menuBox = Combo(menu, "Display");
        var qtyBox = new TextBox { Text = "1", Margin = new Thickness(0, 4, 0, 10), Height = 34 };
        var orderIdBox = new TextBox { IsReadOnly = true, Margin = new Thickness(0, 4, 0, 10), Height = 34 };
        var status = new TextBlock { Foreground = System.Windows.Media.Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };

        form.Children.Add(Label("Table")); form.Children.Add(tableBox);
        form.Children.Add(Label("Guests")); form.Children.Add(guestBox);
        var open = Button("Open order");
        form.Children.Add(open);
        form.Children.Add(Label("Current order")); form.Children.Add(orderIdBox);
        form.Children.Add(Label("Menu item")); form.Children.Add(menuBox);
        form.Children.Add(Label("Quantity")); form.Children.Add(qtyBox);
        var add = Button("Add item"); var submit = Button("Send KOT");
        form.Children.Add(add); form.Children.Add(submit); form.Children.Add(status);

        open.Click += async (_, _) =>
        {
            try
            {
                if (tableBox.SelectedItem is not Choice table) throw new InvalidOperationException("Select a table.");
                if (!int.TryParse(guestBox.Text, out var guests)) guests = 1;
                orderIdBox.Text = await workflow.OpenOrderAsync(table.Id, guests);
                status.Text = "Order opened locally. Add items and send KOT.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        add.Click += async (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(orderIdBox.Text)) throw new InvalidOperationException("Open an order first.");
                if (menuBox.SelectedItem is not MenuChoice item) throw new InvalidOperationException("Select a menu item.");
                if (!int.TryParse(qtyBox.Text, out var qty)) qty = 1;
                await workflow.AddItemAsync(orderIdBox.Text, item.Id, qty);
                status.Text = $"{qty} × {item.Name} added.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        submit.Click += async (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(orderIdBox.Text)) throw new InvalidOperationException("Open an order first.");
                await workflow.SubmitOrderAsync(orderIdBox.Text);
                status.Text = "Order sent to kitchen/KOT.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };

        var grid = DataGrid(orders);
        grid.Columns.Add(Column("Order", nameof(OrderChoice.ClientOrderId), 220));
        grid.Columns.Add(Column("Waiter", nameof(OrderChoice.Waiter), 150));
        grid.Columns.Add(Column("Guests", nameof(OrderChoice.Guests), 80));
        grid.Columns.Add(Column("Status", nameof(OrderChoice.Status), 120));
        grid.Columns.Add(Column("Total AFN", nameof(OrderChoice.Total), 120));
        Grid.SetColumn(form, 0); Grid.SetColumn(grid, 2);
        root.Children.Add(Card(form)); root.Children.Add(grid);
        return root;
    }

    public static async Task<FrameworkElement> TablesAsync()
    {
        var factory = new LocalDatabaseFactory();
        await factory.EnsureCreatedAsync();
        await using var db = factory.Create();
        var rows = await (from table in db.DiningTables.AsNoTracking()
                          join area in db.DiningAreas.AsNoTracking() on table.DiningAreaId equals area.Id
                          where table.IsActive
                          orderby area.SortOrder, table.Name
                          select new TableChoice(table.Id, area.Name, table.Code, table.Name, table.Capacity, table.Status)).ToListAsync();

        var root = new StackPanel();
        root.Children.Add(Header("Dining floor", "Live table state shared with waiter phones/tablets over LAN."));
        var grid = DataGrid(rows);
        grid.Columns.Add(Column("Area", nameof(TableChoice.Area), 160));
        grid.Columns.Add(Column("Table", nameof(TableChoice.Name), 220));
        grid.Columns.Add(Column("Code", nameof(TableChoice.Code), 100));
        grid.Columns.Add(Column("Seats", nameof(TableChoice.Capacity), 80));
        grid.Columns.Add(Column("Status", nameof(TableChoice.Status), 130));
        root.Children.Add(grid);
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    public static async Task<FrameworkElement> KitchenAsync()
    {
        var factory = new LocalDatabaseFactory();
        await factory.EnsureCreatedAsync();
        await using var db = factory.Create();
        var rows = await (from ticket in db.KitchenTickets.AsNoTracking()
                          join station in db.KitchenStations.AsNoTracking() on ticket.KitchenStationId equals station.Id
                          where ticket.Status == "queued" || ticket.Status == "preparing" || ticket.Status == "ready"
                          orderby ticket.QueuedAt
                          select new KitchenChoice(ticket.Id, ticket.TicketNumber, station.Name, ticket.Status, ticket.QueuedAt)).ToListAsync();

        var workflow = new DesktopRestaurantWorkflowService();
        var root = new StackPanel();
        root.Children.Add(Header("Kitchen / KOT", "Tickets arrive from desktop POS and LAN-connected waiter devices."));
        var grid = DataGrid(rows);
        grid.SelectionMode = DataGridSelectionMode.Single;
        grid.Columns.Add(Column("KOT", nameof(KitchenChoice.TicketNumber), 220));
        grid.Columns.Add(Column("Station", nameof(KitchenChoice.Station), 180));
        grid.Columns.Add(Column("Status", nameof(KitchenChoice.Status), 120));
        grid.Columns.Add(Column("Queued", nameof(KitchenChoice.QueuedAt), 210));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var start = Button("Start preparing"); var ready = Button("Mark ready");
        actions.Children.Add(start); actions.Children.Add(ready);
        var status = new TextBlock { Margin = new Thickness(12, 9, 0, 0), Foreground = System.Windows.Media.Brushes.SlateGray };
        actions.Children.Add(status);
        start.Click += async (_, _) =>
        {
            try
            {
                if (grid.SelectedItem is not KitchenChoice row) throw new InvalidOperationException("Select a KOT.");
                await workflow.StartKitchenTicketAsync(row.Id);
                status.Text = $"{row.TicketNumber} started.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        ready.Click += async (_, _) =>
        {
            try
            {
                if (grid.SelectedItem is not KitchenChoice row) throw new InvalidOperationException("Select a KOT.");
                await workflow.MarkKitchenTicketReadyAsync(row.Id);
                status.Text = $"{row.TicketNumber} marked ready.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        root.Children.Add(grid); root.Children.Add(actions);
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static TextBlock HeaderText(string text, double size, bool bold = false) => new() { Text = text, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal };
    private static StackPanel Header(string title, string subtitle)
    {
        var p = new StackPanel { Margin = new Thickness(0,0,0,16) };
        p.Children.Add(HeaderText(title,20,true));
        p.Children.Add(new TextBlock { Text = subtitle, Foreground = System.Windows.Media.Brushes.SlateGray, Margin = new Thickness(0,4,0,0), TextWrapping = TextWrapping.Wrap });
        return p;
    }
    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,8,0,0) };
    private static ComboBox Combo(object items, string member) => new() { ItemsSource = (System.Collections.IEnumerable)items, DisplayMemberPath = member, Height = 34, Margin = new Thickness(0,4,0,6) };
    private static Button Button(string text) => new() { Content = text, Height = 36, MinWidth = 120, Margin = new Thickness(0,4,8,4), Padding = new Thickness(12,0,12,0), HorizontalAlignment = HorizontalAlignment.Left };
    private static Border Card(UIElement child) => new() { Background = System.Windows.Media.Brushes.White, CornerRadius = new CornerRadius(14), Padding = new Thickness(18), Child = child };
    private static DataGrid DataGrid(object items) => new() { ItemsSource = (System.Collections.IEnumerable)items, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, MinHeight = 420, Background = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0) };
    private static DataGridTextColumn Column(string header, string property, double width) => new() { Header = header, Binding = new System.Windows.Data.Binding(property), Width = width };

    private sealed record Choice(string Id, string Label);
    private sealed record MenuChoice(string Id, string Name, decimal Price) { public string Display => $"{Name} — AFN {Price:N2}"; }
    private sealed record OrderChoice(string Id, string ClientOrderId, string Waiter, string Status, int Guests, decimal Total);
    private sealed record TableChoice(string Id, string Area, string Code, string Name, int Capacity, string Status);
    private sealed record KitchenChoice(string Id, string TicketNumber, string Station, string Status, DateTimeOffset QueuedAt);
}
