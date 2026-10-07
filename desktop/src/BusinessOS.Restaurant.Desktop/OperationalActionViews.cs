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
        var orders = (await db.Orders.AsNoTracking().Where(x => x.Status != "closed")
                .Select(x => new { Row = new OrderChoice(x.Id, x.ClientOrderId, x.WaiterName, x.Status, x.GuestCount, x.Total), x.UpdatedAtUtc })
                .ToListAsync())
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(100)
            .Select(x => x.Row)
            .ToList();
        var branches = await db.Branches.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new Choice(x.Id, x.Name)).ToListAsync();
        var sessions = (await db.CashierSessions.AsNoTracking().Where(x => x.Status == "open")
                .Select(x => new CashierSessionChoice(x.Id, x.CashierName, x.Status, x.OpeningCash, x.ExpectedCash, x.DeclaredCash, x.CashVariance, x.OpenedAt))
                .ToListAsync())
            .OrderByDescending(x => x.OpenedAt)
            .ToList();
        var bills = (await db.Bills.AsNoTracking().Where(x => x.Status == "open")
                .Select(x => new { Row = new BillChoice(x.Id, x.OrderId, x.BillNumber, x.Total, x.PaidAmount, x.BalanceDue), x.IssuedAt })
                .ToListAsync())
            .OrderByDescending(x => x.IssuedAt)
            .Select(x => x.Row)
            .ToList();

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

        var cashier = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        cashier.Children.Add(Header("Cashier & billing", "Restaurant flow: serve ready order → issue bill → optional discount/split → payment → receipt."));
        var cashierStatus = new TextBlock { Foreground = System.Windows.Media.Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,0) };
        var branchBox = Combo(branches, "Label");
        var openingCash = new TextBox { Text = "0", Height = 34, Width = 130, Margin = new Thickness(0,4,8,6) };
        var sessionBox = Combo(sessions, "Cashier"); sessionBox.Width = 260;
        var orderBox = Combo(orders, "ClientOrderId"); orderBox.Width = 260;
        var billBox = Combo(bills, "Display"); billBox.Width = 360;
        var paymentMethod = new ComboBox { ItemsSource = new[] { "cash", "card", "bank", "mobile_money", "other" }, SelectedIndex = 0, Height = 34, Width = 160, Margin = new Thickness(0,4,8,6) };
        var amountBox = new TextBox { Text = "0", Height = 34, Width = 130, Margin = new Thickness(0,4,8,6) };
        var discountBox = new TextBox { Text = "0", Height = 34, Width = 110, Margin = new Thickness(0,4,8,6) };
        var splitCountBox = new TextBox { Text = "2", Height = 34, Width = 80, Margin = new Thickness(0,4,8,6) };

        cashier.Children.Add(Label("Branch / opening cash"));
        var sessionOpenRow = new WrapPanel(); sessionOpenRow.Children.Add(branchBox); sessionOpenRow.Children.Add(openingCash);
        var openSession = Button("Open cashier session"); sessionOpenRow.Children.Add(openSession); cashier.Children.Add(sessionOpenRow);
        cashier.Children.Add(Label("Open cashier session")); cashier.Children.Add(sessionBox);
        cashier.Children.Add(Label("Order")); cashier.Children.Add(orderBox);
        var orderActions = new WrapPanel(); var serve = Button("Mark served"); var issue = Button("Issue bill"); orderActions.Children.Add(serve); orderActions.Children.Add(issue); cashier.Children.Add(orderActions);
        cashier.Children.Add(Label("Open bill")); cashier.Children.Add(billBox);
        var discountRow = new WrapPanel(); discountRow.Children.Add(discountBox); var discount = Button("Apply % discount"); discountRow.Children.Add(discount); discountRow.Children.Add(splitCountBox); var split = Button("Split equally"); discountRow.Children.Add(split); cashier.Children.Add(discountRow);
        var payRow = new WrapPanel(); payRow.Children.Add(paymentMethod); payRow.Children.Add(amountBox); var pay = Button("Post payment"); var receipt = Button("Queue receipt"); payRow.Children.Add(pay); payRow.Children.Add(receipt); cashier.Children.Add(payRow);
        cashier.Children.Add(cashierStatus);

        openSession.Click += async (_, _) => { try { if (branchBox.SelectedItem is not Choice b) throw new InvalidOperationException("Select a branch."); if (!decimal.TryParse(openingCash.Text, out var cash)) throw new InvalidOperationException("Enter opening cash."); await workflow.OpenCashierSessionAsync(b.Id, cash); cashierStatus.Text = "Cashier session opened. Refresh to load it."; } catch (Exception ex) { cashierStatus.Text = ex.Message; } };
        serve.Click += async (_, _) => { try { if (orderBox.SelectedItem is not OrderChoice o) throw new InvalidOperationException("Select an order."); await workflow.ServeOrderAsync(o.Id); cashierStatus.Text = "Order served; recipe inventory consumption recorded."; } catch (Exception ex) { cashierStatus.Text = ex.Message; } };
        issue.Click += async (_, _) => { try { if (orderBox.SelectedItem is not OrderChoice o) throw new InvalidOperationException("Select an order."); await workflow.CreateBillAsync(o.Id); cashierStatus.Text = "Bill issued. Refresh to load it for payment."; } catch (Exception ex) { cashierStatus.Text = ex.Message; } };
        discount.Click += async (_, _) => { try { if (billBox.SelectedItem is not BillChoice b) throw new InvalidOperationException("Select a bill."); if (!decimal.TryParse(discountBox.Text, out var value)) throw new InvalidOperationException("Enter discount percent."); await workflow.ApplyDiscountAsync(b.Id, "percent", value, "Desktop cashier discount"); cashierStatus.Text = "Discount applied."; } catch (Exception ex) { cashierStatus.Text = ex.Message; } };
        split.Click += async (_, _) => { try { if (billBox.SelectedItem is not BillChoice b) throw new InvalidOperationException("Select a bill."); if (!int.TryParse(splitCountBox.Text, out var count)) throw new InvalidOperationException("Enter split count."); await workflow.CreateEqualSplitsAsync(b.Id, count); cashierStatus.Text = $"Bill split into {count} parts."; } catch (Exception ex) { cashierStatus.Text = ex.Message; } };
        pay.Click += async (_, _) => { try { if (billBox.SelectedItem is not BillChoice b) throw new InvalidOperationException("Select a bill."); if (sessionBox.SelectedItem is not CashierSessionChoice s) throw new InvalidOperationException("Select an open cashier session."); if (!decimal.TryParse(amountBox.Text, out var amount)) throw new InvalidOperationException("Enter payment amount."); await workflow.AddPaymentAsync(b.Id, s.Id, amount, paymentMethod.SelectedItem?.ToString() ?? "cash"); cashierStatus.Text = "Payment posted. A fully paid bill releases the table through the restaurant settlement workflow."; } catch (Exception ex) { cashierStatus.Text = ex.Message; } };
        receipt.Click += async (_, _) => { try { if (billBox.SelectedItem is not BillChoice b) throw new InvalidOperationException("Select a bill."); await workflow.QueueReceiptAsync(b.Id); cashierStatus.Text = "Receipt queued for the configured restaurant receipt printer."; } catch (Exception ex) { cashierStatus.Text = ex.Message; } };

        var page = new StackPanel();
        page.Children.Add(root);
        page.Children.Add(Card(cashier));
        return new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
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
        var rows = (await (from ticket in db.KitchenTickets.AsNoTracking()
                           join station in db.KitchenStations.AsNoTracking() on ticket.KitchenStationId equals station.Id
                           where ticket.Status == "queued" || ticket.Status == "preparing" || ticket.Status == "ready"
                           select new KitchenChoice(ticket.Id, ticket.TicketNumber, station.Name, ticket.Status, ticket.QueuedAt)).ToListAsync())
            .OrderBy(x => x.QueuedAt)
            .ToList();

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


    public static async Task<FrameworkElement> ClosingAsync()
    {
        var factory = new LocalDatabaseFactory();
        await factory.EnsureCreatedAsync();
        await using var db = factory.Create();

        var branches = await db.Branches.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new Choice(x.Id, x.Name)).ToListAsync();
        var sessions = (await db.CashierSessions.AsNoTracking()
                .Select(x => new CashierSessionChoice(x.Id, x.CashierName, x.Status, x.OpeningCash, x.ExpectedCash, x.DeclaredCash, x.CashVariance, x.OpenedAt))
                .ToListAsync())
            .OrderByDescending(x => x.OpenedAt)
            .Take(50)
            .ToList();
        var closings = await db.DailyClosings.AsNoTracking().OrderByDescending(x => x.BusinessDate).Take(30)
            .Select(x => new ClosingChoice(x.Id, x.BusinessDate, x.Status, x.FinalizedAt)).ToListAsync();

        var workflow = new DesktopRestaurantWorkflowService();
        var root = new StackPanel();
        root.Children.Add(Header("Daily closing", "Restaurant end-of-day control. Close cashier sessions and staff shifts before finalizing the business date."));

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,12) };
        var branchBox = Combo(branches, "Label"); branchBox.Width = 220;
        var dateBox = new TextBox { Text = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"), Width = 130, Height = 34, Margin = new Thickness(8,4,8,6) };
        var finalize = Button("Finalize day");
        var status = new TextBlock { Margin = new Thickness(12,10,0,0), Foreground = System.Windows.Media.Brushes.SlateGray, TextWrapping = TextWrapping.Wrap };
        controls.Children.Add(branchBox); controls.Children.Add(dateBox); controls.Children.Add(finalize); controls.Children.Add(status);
        finalize.Click += async (_, _) =>
        {
            try
            {
                if (branchBox.SelectedItem is not Choice branch) throw new InvalidOperationException("Select a branch.");
                if (!DateOnly.TryParse(dateBox.Text, out var date)) throw new InvalidOperationException("Enter a valid business date.");
                await workflow.FinalizeDailyClosingAsync(branch.Id, date);
                status.Text = $"Business date {date:yyyy-MM-dd} finalized.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        root.Children.Add(Card(controls));

        var sessionGrid = DataGrid(sessions);
        sessionGrid.Columns.Add(Column("Cashier", nameof(CashierSessionChoice.Cashier), 180));
        sessionGrid.Columns.Add(Column("Status", nameof(CashierSessionChoice.Status), 100));
        sessionGrid.Columns.Add(Column("Opening AFN", nameof(CashierSessionChoice.OpeningCash), 120));
        sessionGrid.Columns.Add(Column("Expected", nameof(CashierSessionChoice.ExpectedCash), 110));
        sessionGrid.Columns.Add(Column("Declared", nameof(CashierSessionChoice.DeclaredCash), 110));
        sessionGrid.Columns.Add(Column("Variance", nameof(CashierSessionChoice.Variance), 110));
        sessionGrid.Columns.Add(Column("Opened", nameof(CashierSessionChoice.OpenedAt), 190));
        root.Children.Add(Header("Cashier sessions", "Cash control is separated from waiter ordering and kitchen operations."));
        root.Children.Add(sessionGrid);

        var closingGrid = DataGrid(closings);
        closingGrid.MinHeight = 220;
        closingGrid.Columns.Add(Column("Business date", nameof(ClosingChoice.BusinessDate), 150));
        closingGrid.Columns.Add(Column("Status", nameof(ClosingChoice.Status), 120));
        closingGrid.Columns.Add(Column("Finalized", nameof(ClosingChoice.FinalizedAt), 210));
        root.Children.Add(Header("Closing history", "Finalized restaurant business dates and audit status."));
        root.Children.Add(closingGrid);
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static TextBlock HeaderText(string text, double size, bool bold = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        return block;
    }

    private static StackPanel Header(string title, string subtitle)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        panel.Children.Add(HeaderText(title, 20, true));

        var subtitleBlock = new TextBlock
        {
            Text = subtitle,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        subtitleBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        panel.Children.Add(subtitleBlock);
        return panel;
    }

    private static TextBlock Label(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 0),
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        return block;
    }

    private static ComboBox Combo(object items, string member) => new()
    {
        ItemsSource = (System.Collections.IEnumerable)items,
        DisplayMemberPath = member,
        Height = 34,
        Margin = new Thickness(0, 4, 0, 6),
    };

    private static Button Button(string text) => new()
    {
        Content = text,
        Height = 36,
        MinWidth = 120,
        Margin = new Thickness(0, 4, 8, 4),
        Padding = new Thickness(12, 0, 12, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private static Border Card(UIElement child)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18),
            BorderThickness = new Thickness(1),
            Child = child,
        };
        border.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        border.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 4,
            Opacity = 0.10,
            Color = System.Windows.Media.Color.FromRgb(7, 24, 39),
        };
        return border;
    }

    private static DataGrid DataGrid(object items)
    {
        var grid = new DataGrid
        {
            ItemsSource = (System.Collections.IEnumerable)items,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            MinHeight = 420,
            BorderThickness = new Thickness(1),
            AlternationCount = 2,
        };
        grid.SetResourceReference(DataGrid.BackgroundProperty, "SurfaceBrush");
        grid.SetResourceReference(DataGrid.BorderBrushProperty, "BorderBrush");
        return grid;
    }
    private static DataGridTextColumn Column(string header, string property, double width) => new() { Header = header, Binding = new System.Windows.Data.Binding(property), Width = width };

    private sealed record Choice(string Id, string Label);
    private sealed record MenuChoice(string Id, string Name, decimal Price) { public string Display => $"{Name} — AFN {Price:N2}"; }
    private sealed record OrderChoice(string Id, string ClientOrderId, string Waiter, string Status, int Guests, decimal Total);
    private sealed record BillChoice(string Id, string OrderId, string Number, decimal Total, decimal Paid, decimal Balance) { public string Display => $"{Number} — AFN {Balance:N2} due"; }
    private sealed record TableChoice(string Id, string Area, string Code, string Name, int Capacity, string Status);
    private sealed record KitchenChoice(string Id, string TicketNumber, string Station, string Status, DateTimeOffset QueuedAt);
    private sealed record CashierSessionChoice(string Id, string Cashier, string Status, decimal OpeningCash, decimal? ExpectedCash, decimal? DeclaredCash, decimal? Variance, DateTimeOffset OpenedAt);
    private sealed record ClosingChoice(string Id, DateOnly BusinessDate, string Status, DateTimeOffset? FinalizedAt);
}
