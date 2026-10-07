using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using BusinessOS.Restaurant.LocalServer;
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
            "expenses" => await ExpensesAsync(),
            "closing" => await OperationalActionViews.ClosingAsync(),
            "reports" => Reports(diagnostics),
            "settings" => await SettingsAsync(diagnostics),
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
        var sales = (await db.Bills.AsNoTracking()
                .Select(x => new { x.IssuedAt, x.Total })
                .ToListAsync())
            .Where(x => x.IssuedAt >= from && x.IssuedAt < to)
            .Sum(x => x.Total);

        var panel = Stack();
        panel.Children.Add(Hero(
            "Restaurant command center",
            "A polished local-first overview for cashier, floor and kitchen operations.",
            diagnostics.NetworkMode,
            diagnostics.LeaseStatus));
        panel.Children.Add(Cards(
            ("TODAY'S SALES", $"AFN {sales:N2}"),
            ("OPEN ORDERS", openOrders.ToString()),
            ("ACTIVE TABLES", activeTables.ToString()),
            ("KITCHEN TICKETS", activeKot.ToString())));
        panel.Children.Add(Cards(
            ("NETWORK MODE", diagnostics.NetworkMode),
            ("WAITER DEVICES", diagnostics.TerminalSummary),
            ("LICENSE / OFFLINE", diagnostics.LeaseStatus)));
        panel.Children.Add(Card(
            "Live operations",
            diagnostics.StatusMessage,
            double.NaN));
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
        var rows = (await (from ticket in db.KitchenTickets.AsNoTracking()
                           join station in db.KitchenStations.AsNoTracking() on ticket.KitchenStationId equals station.Id
                           where ticket.Status == "queued" || ticket.Status == "preparing" || ticket.Status == "ready"
                           select new KitchenRow(ticket.Id, ticket.TicketNumber, station.Name, ticket.Status, ticket.QueuedAt))
                          .ToListAsync())
            .OrderBy(x => x.QueuedAt)
            .ToList();

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
        var orders = (await db.Orders.AsNoTracking().Where(x => x.Status != "closed")
                .Select(x => new OrderRow(x.Id, x.ClientOrderId, x.WaiterName, x.Status, x.GuestCount, x.Total, x.UpdatedAtUtc))
                .ToListAsync())
            .OrderByDescending(x => x.UpdatedAt)
            .Take(100)
            .ToList();

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
        var rows = await (from item in db.InventoryItems.AsNoTracking()
                          join balance in db.InventoryBalances.AsNoTracking() on item.Id equals balance.InventoryItemId into balances
                          from balance in balances.DefaultIfEmpty()
                          join valuation in db.InventoryValuations.AsNoTracking() on item.Id equals valuation.InventoryItemId into valuations
                          from valuation in valuations.DefaultIfEmpty()
                          where item.IsActive
                          orderby item.Name
                          select new InventoryRow(item.Sku, item.Name, item.BaseUnit, item.PurchaseUnit ?? item.BaseUnit,
                              balance == null ? 0m : balance.Quantity, item.ReorderLevel,
                              valuation == null ? 0m : valuation.AverageUnitCost,
                              valuation == null ? 0m : valuation.Value)).ToListAsync();
        var grid = GridFor(rows);
        grid.Columns.Add(Column("SKU", nameof(InventoryRow.Sku), 120));
        grid.Columns.Add(Column("Ingredient", nameof(InventoryRow.Name), 220));
        grid.Columns.Add(Column("On hand", nameof(InventoryRow.Quantity), 110));
        grid.Columns.Add(Column("Base unit", nameof(InventoryRow.BaseUnit), 100));
        grid.Columns.Add(Column("Reorder", nameof(InventoryRow.ReorderLevel), 100));
        grid.Columns.Add(Column("Avg cost AFN", nameof(InventoryRow.AverageCost), 120));
        grid.Columns.Add(Column("Stock value AFN", nameof(InventoryRow.StockValue), 135));
        return Section("Inventory", "Restaurant ingredients, on-hand stock, valuation and recipe consumption remain available without internet.", grid);
    }

    private static async Task<FrameworkElement> PurchasesAsync()
    {
        var factory = new LocalDatabaseFactory();
        await factory.EnsureCreatedAsync();
        await using var db = factory.Create();
        var orders = (await (from po in db.PurchaseOrders.AsNoTracking()
                             join supplier in db.Suppliers.AsNoTracking() on po.SupplierId equals supplier.Id
                             select new PurchaseRow(po.PoNumber, supplier.Name, po.Status, po.EstimatedTotal, po.OrderedAt, po.CompletedAt))
                            .ToListAsync())
            .OrderByDescending(x => x.OrderedAt)
            .Take(100)
            .ToList();
        var suppliers = await db.Suppliers.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new SupplierRow(x.Code, x.Name, x.Phone, x.Email)).ToListAsync();
        var receipts = (await db.GoodsReceipts.AsNoTracking()
                .Select(x => new ReceiptRow(x.ReceiptNumber, x.Status, x.ReceivedAt, x.PurchaseOrderId))
                .ToListAsync())
            .OrderByDescending(x => x.ReceivedAt)
            .Take(100)
            .ToList();

        var panel = Stack();
        panel.Children.Add(Card("Restaurant procurement", "Supplier → purchase order → goods receipt → stock movement. Receiving stock updates the local restaurant inventory."));
        var supplierGrid = GridFor(suppliers); supplierGrid.MinHeight = 180;
        supplierGrid.Columns.Add(Column("Code", nameof(SupplierRow.Code), 110)); supplierGrid.Columns.Add(Column("Supplier", nameof(SupplierRow.Name), 220));
        supplierGrid.Columns.Add(Column("Phone", nameof(SupplierRow.Phone), 150)); supplierGrid.Columns.Add(Column("Email", nameof(SupplierRow.Email), 220));
        panel.Children.Add(supplierGrid);
        panel.Children.Add(new TextBlock { Text = "Purchase orders", FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0,20,0,10) });
        var grid = GridFor(orders); grid.MinHeight = 230;
        grid.Columns.Add(Column("PO", nameof(PurchaseRow.Number), 190)); grid.Columns.Add(Column("Supplier", nameof(PurchaseRow.Supplier), 220));
        grid.Columns.Add(Column("Status", nameof(PurchaseRow.Status), 120)); grid.Columns.Add(Column("Estimated AFN", nameof(PurchaseRow.Total), 140));
        grid.Columns.Add(Column("Ordered", nameof(PurchaseRow.OrderedAt), 190)); grid.Columns.Add(Column("Completed", nameof(PurchaseRow.CompletedAt), 190));
        panel.Children.Add(grid);
        panel.Children.Add(new TextBlock { Text = "Goods receipts", FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0,20,0,10) });
        var receiptGrid = GridFor(receipts); receiptGrid.MinHeight = 200;
        receiptGrid.Columns.Add(Column("GRN", nameof(ReceiptRow.Number), 210)); receiptGrid.Columns.Add(Column("Status", nameof(ReceiptRow.Status), 120));
        receiptGrid.Columns.Add(Column("Received", nameof(ReceiptRow.ReceivedAt), 200)); receiptGrid.Columns.Add(Column("Purchase order ID", nameof(ReceiptRow.PurchaseOrderId), 280));
        panel.Children.Add(receiptGrid);
        return Scroll(panel);
    }


    private static async Task<FrameworkElement> UsersAsync()
    {
        var factory = new LocalDatabaseFactory();
        await factory.EnsureCreatedAsync();
        await using var db = factory.Create();
        var staff = await db.StaffUsers.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new StaffRow(x.Name, x.Email, x.Role, x.IsActive)).ToListAsync();
        var shifts = (await db.WaiterShifts.AsNoTracking()
                .Select(x => new ShiftRow(x.UserName, x.Role, x.Status, x.StartedAt, x.EndedAt, x.BreakMinutes))
                .ToListAsync())
            .OrderByDescending(x => x.StartedAt)
            .Take(100)
            .ToList();

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

    private static async Task<FrameworkElement> ExpensesAsync()
    {
        var factory = new LocalDatabaseFactory();
        await factory.EnsureCreatedAsync();
        await using var db = factory.Create();
        var branches = await db.Branches.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new ExpenseBranchChoice(x.Id, x.Name)).ToListAsync();
        var rows = (await (from expense in db.Expenses.AsNoTracking()
                           join branch in db.Branches.AsNoTracking() on expense.BranchId equals branch.Id
                           select new
                           {
                               Row = new ExpenseRow(expense.ExpenseDate, branch.Name, expense.Category, expense.Description,
                                   expense.Amount, expense.Currency, expense.PaymentMethod, expense.Reference),
                               expense.ExpenseDate,
                               expense.RecordedAtUtc,
                           }).ToListAsync())
            .OrderByDescending(x => x.ExpenseDate)
            .ThenByDescending(x => x.RecordedAtUtc)
            .Take(500)
            .Select(x => x.Row)
            .ToList();

        var panel = Stack();
        panel.Children.Add(Card("Restaurant expenses", "Record local operating expenses in AFN. Every entry is audited and queued for cloud reconciliation without blocking offline restaurant operations."));

        var form = new WrapPanel { Margin = new Thickness(0, 4, 0, 14) };
        var branchBox = new ComboBox { ItemsSource = branches, DisplayMemberPath = nameof(ExpenseBranchChoice.Name), Width = 180, Height = 34, Margin = new Thickness(0,4,8,4) };
        var category = new TextBox { Text = "operations", Width = 140, Height = 34, Margin = new Thickness(0,4,8,4) };
        var description = new TextBox { Width = 240, Height = 34, Margin = new Thickness(0,4,8,4) };
        var amount = new TextBox { Text = "0", Width = 100, Height = 34, Margin = new Thickness(0,4,8,4) };
        var method = new ComboBox { ItemsSource = new[] { "cash", "card", "bank", "mobile_money", "other" }, SelectedIndex = 0, Width = 130, Height = 34, Margin = new Thickness(0,4,8,4) };
        var record = new Button { Content = "Record expense", MinWidth = 130, Height = 34, Margin = new Thickness(0,4,8,4) };
        var status = new TextBlock { Foreground = System.Windows.Media.Brushes.SlateGray, Margin = new Thickness(8,11,0,0), TextWrapping = TextWrapping.Wrap };
        form.Children.Add(branchBox); form.Children.Add(category); form.Children.Add(description); form.Children.Add(amount); form.Children.Add(method); form.Children.Add(record); form.Children.Add(status);
        panel.Children.Add(form);

        var grid = GridFor(rows);
        grid.Columns.Add(Column("Date", nameof(ExpenseRow.Date), 120));
        grid.Columns.Add(Column("Branch", nameof(ExpenseRow.Branch), 160));
        grid.Columns.Add(Column("Category", nameof(ExpenseRow.Category), 150));
        grid.Columns.Add(Column("Description", nameof(ExpenseRow.Description), 260));
        grid.Columns.Add(Column("Amount AFN", nameof(ExpenseRow.Amount), 120));
        grid.Columns.Add(Column("Method", nameof(ExpenseRow.Method), 130));
        grid.Columns.Add(Column("Reference", nameof(ExpenseRow.Reference), 180));
        panel.Children.Add(grid);

        var workflow = new DesktopRestaurantWorkflowService();
        record.Click += async (_, _) =>
        {
            try
            {
                if (branchBox.SelectedItem is not ExpenseBranchChoice branch) throw new InvalidOperationException("Select a branch.");
                if (string.IsNullOrWhiteSpace(description.Text)) throw new InvalidOperationException("Enter an expense description.");
                if (!decimal.TryParse(amount.Text, out var value) || value <= 0) throw new InvalidOperationException("Enter a positive expense amount.");
                await workflow.RecordExpenseAsync(branch.Id, category.Text, description.Text, value, method.SelectedItem?.ToString() ?? "cash", DateOnly.FromDateTime(DateTime.Today));
                status.Text = "Expense recorded locally. Refresh the page to update the ledger.";
                description.Clear(); amount.Text = "0";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };

        return Scroll(panel);
    }

    private static FrameworkElement Reports(LanDiagnosticsViewModel diagnostics)
    {
        var panel = Stack();
        panel.DataContext = diagnostics.Reports;
        panel.Children.Add(Card(
            "Reports & accounting",
            "Financial and kitchen performance data stay local-first. Kitchen metrics use KOT/item timestamps and the same warning/late thresholds configured in Restaurant Settings."));

        var filters = new WrapPanel { Margin = new Thickness(0, 12, 0, 12) };
        var branch = new TextBox { Width = 180, Height = 34, Margin = new Thickness(0, 4, 8, 6) };
        branch.SetBinding(TextBox.TextProperty, new Binding(nameof(ReportsViewModel.BranchId))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        var from = new DatePicker { Width = 150, Margin = new Thickness(0, 4, 8, 6) };
        from.SetBinding(DatePicker.SelectedDateProperty, new Binding(nameof(ReportsViewModel.From)) { Mode = BindingMode.TwoWay });
        var to = new DatePicker { Width = 150, Margin = new Thickness(0, 4, 8, 6) };
        to.SetBinding(DatePicker.SelectedDateProperty, new Binding(nameof(ReportsViewModel.To)) { Mode = BindingMode.TwoWay });
        var button = new Button { Content = "Run report", Width = 140, Height = 38, Margin = new Thickness(0, 2, 0, 0) };
        button.SetBinding(Button.CommandProperty, new Binding(nameof(ReportsViewModel.RefreshReportsCommand)));
        filters.Children.Add(branch);
        filters.Children.Add(from);
        filters.Children.Add(to);
        filters.Children.Add(button);
        panel.Children.Add(filters);

        var metrics = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 16) };
        foreach (var metric in new[]
        {
            ("KOT ROUNDS", nameof(ReportsViewModel.KitchenRounds)),
            ("AVG KITCHEN", nameof(ReportsViewModel.KitchenAverage)),
            ("LATE ITEMS", nameof(ReportsViewModel.KitchenLateItems)),
            ("RUSH ITEMS", nameof(ReportsViewModel.KitchenRushItems)),
        })
        {
            var stack = new StackPanel();
            var label = new TextBlock
            {
                Text = metric.Item1,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            var value = new TextBlock
            {
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 4, 0, 0),
            };
            value.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            value.SetBinding(TextBlock.TextProperty, new Binding(metric.Item2));
            stack.Children.Add(label);
            stack.Children.Add(value);

            var metricCard = new Border
            {
                Child = stack,
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 10, 0),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
            };
            metricCard.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
            metricCard.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            metrics.Children.Add(metricCard);
        }
        panel.Children.Add(metrics);

        var kitchenGrid = GridFor(diagnostics.Reports.KitchenStations);
        kitchenGrid.MinHeight = 240;
        kitchenGrid.Columns.Add(Column("Station", nameof(ReportKitchenStationRow.Station), 180));
        kitchenGrid.Columns.Add(Column("Items", nameof(ReportKitchenStationRow.Items), 80));
        kitchenGrid.Columns.Add(Column("Active", nameof(ReportKitchenStationRow.ActiveItems), 80));
        kitchenGrid.Columns.Add(Column("Rush", nameof(ReportKitchenStationRow.RushItems), 80));
        kitchenGrid.Columns.Add(Column("Late", nameof(ReportKitchenStationRow.LateItems), 80));
        kitchenGrid.Columns.Add(Column("Queue min", nameof(ReportKitchenStationRow.AverageQueueMinutes), 110));
        kitchenGrid.Columns.Add(Column("Prep min", nameof(ReportKitchenStationRow.AveragePreparationMinutes), 110));
        kitchenGrid.Columns.Add(Column("Total min", nameof(ReportKitchenStationRow.AverageTotalMinutes), 110));
        kitchenGrid.Columns.Add(Column("Utilization %", nameof(ReportKitchenStationRow.UtilizationPercent), 110));
        panel.Children.Add(Section(
            "Kitchen performance",
            "Stations are ordered by total preparation time so bottlenecks surface first.",
            kitchenGrid));

        return Scroll(panel);
    }

    private static async Task<FrameworkElement> SettingsAsync(LanDiagnosticsViewModel diagnostics)
    {
        var panel = Stack();
        panel.DataContext = diagnostics;

        var workflow = new DesktopRestaurantWorkflowService();
        var workflowSettings = await workflow.RestaurantSettingsAsync();

        var workflowPanel = new StackPanel();
        workflowPanel.Children.Add(new TextBlock
        {
            Text = "Kitchen workflow",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
        });

        var workflowHelp = new TextBlock
        {
            Text = "Queue and Preparing are independent. Turning one off never changes the other. New KOT rounds snapshot these values so historical tickets keep the workflow they were created with.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12),
        };
        workflowHelp.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        workflowPanel.Children.Add(workflowHelp);

        var queue = new CheckBox
        {
            Content = "Kitchen Queue",
            IsChecked = workflowSettings.KitchenQueueEnabled,
            Margin = new Thickness(0, 5, 0, 5),
        };
        var preparing = new CheckBox
        {
            Content = "Preparing stage",
            IsChecked = workflowSettings.PreparingStageEnabled,
            Margin = new Thickness(0, 5, 0, 5),
        };
        var expo = new CheckBox
        {
            Content = "Expo stage",
            IsChecked = workflowSettings.ExpoEnabled,
            Margin = new Thickness(0, 5, 0, 5),
        };
        var courses = new CheckBox
        {
            Content = "Course firing",
            IsChecked = workflowSettings.CoursesEnabled,
            Margin = new Thickness(0, 5, 0, 5),
        };
        var sound = new CheckBox
        {
            Content = "KOT notification sound",
            IsChecked = workflowSettings.KotSoundEnabled,
            Margin = new Thickness(0, 5, 0, 5),
        };
        var managerVoid = new CheckBox
        {
            Content = "Require manager approval for post-KOT void/cancel",
            IsChecked = workflowSettings.RequireManagerApprovalForPostKotVoid,
            Margin = new Thickness(0, 5, 0, 10),
        };

        workflowPanel.Children.Add(queue);
        workflowPanel.Children.Add(preparing);
        workflowPanel.Children.Add(expo);
        workflowPanel.Children.Add(courses);
        workflowPanel.Children.Add(sound);
        workflowPanel.Children.Add(managerVoid);

        var thresholds = new WrapPanel();
        var warning = new TextBox
        {
            Text = workflowSettings.KitchenWarningMinutes.ToString(),
            Width = 90,
            Height = 34,
            Margin = new Thickness(0, 4, 12, 6),
        };
        var late = new TextBox
        {
            Text = workflowSettings.KitchenLateMinutes.ToString(),
            Width = 90,
            Height = 34,
            Margin = new Thickness(0, 4, 12, 6),
        };
        thresholds.Children.Add(new TextBlock
        {
            Text = "Warning min",
            Margin = new Thickness(0, 12, 6, 0),
        });
        thresholds.Children.Add(warning);
        thresholds.Children.Add(new TextBlock
        {
            Text = "Late min",
            Margin = new Thickness(0, 12, 6, 0),
        });
        thresholds.Children.Add(late);
        workflowPanel.Children.Add(thresholds);

        var workflowActions = new WrapPanel();
        var saveWorkflow = new Button
        {
            Content = "Save restaurant workflow",
            MinWidth = 190,
            Height = 38,
            Margin = new Thickness(0, 4, 10, 0),
        };
        var workflowStatus = new TextBlock
        {
            Margin = new Thickness(4, 13, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        workflowStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        workflowActions.Children.Add(saveWorkflow);
        workflowActions.Children.Add(workflowStatus);
        workflowPanel.Children.Add(workflowActions);

        saveWorkflow.Click += async (_, _) =>
        {
            try
            {
                if (!int.TryParse(warning.Text, out var warningMinutes))
                    throw new InvalidOperationException("Enter a valid warning threshold.");
                if (!int.TryParse(late.Text, out var lateMinutes))
                    throw new InvalidOperationException("Enter a valid late threshold.");

                saveWorkflow.IsEnabled = false;
                var updated = await workflow.UpdateRestaurantSettingsAsync(
                    new RestaurantWorkflowSettingsUpdate(
                        queue.IsChecked == true,
                        preparing.IsChecked == true,
                        expo.IsChecked == true,
                        courses.IsChecked == true,
                        sound.IsChecked == true,
                        warningMinutes,
                        lateMinutes,
                        managerVoid.IsChecked == true));

                workflowStatus.Text =
                    $"Saved. Queue {(updated.KitchenQueueEnabled ? "ON" : "OFF")} · " +
                    $"Preparing {(updated.PreparingStageEnabled ? "ON" : "OFF")} · " +
                    $"Expo {(updated.ExpoEnabled ? "ON" : "OFF")}.";
            }
            catch (Exception ex)
            {
                workflowStatus.Text = ex.Message;
            }
            finally
            {
                saveWorkflow.IsEnabled = true;
            }
        };

        panel.Children.Add(Section(
            "Restaurant workflow settings",
            "These settings drive the Desktop KOT/KDS state machine and are included in LAN bootstrap/settings APIs for cross-client alignment.",
            workflowPanel));

        panel.Children.Add(Cards(
            ("LAN STATUS", diagnostics.NetworkMode),
            ("WAITER DEVICES", diagnostics.TerminalSummary),
            ("MOBILE ALLOWANCE", diagnostics.MobileAllowance),
            ("OFFLINE LEASE", diagnostics.LeaseStatus),
            ("CLOUD QUEUE", $"{diagnostics.PendingCloudMutations} pending · {diagnostics.OpenCloudConflicts} conflicts")));

        panel.Children.Add(Card(
            "Local restaurant network",
            $"{diagnostics.StatusMessage}\n\nCloud: {diagnostics.CloudStatus}\n\nWaiter phones and tablets connect directly to this Windows desktop over the restaurant LAN/Wi-Fi. Internet is not required for normal table ordering, KOT, kitchen or cashier operations while the signed offline lease is valid."));

        var pairing = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 84,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(12)
        };
        pairing.SetBinding(TextBox.TextProperty, new Binding(nameof(LanDiagnosticsViewModel.PairingDetails))
        {
            Mode = BindingMode.OneWay,
        });
        var pairingPanel = new StackPanel();
        var qr = new Image
        {
            Width = 220,
            Height = 220,
            Stretch = System.Windows.Media.Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12)
        };
        qr.SetBinding(Image.SourceProperty, new Binding(nameof(LanDiagnosticsViewModel.PairingQrImage)));
        pairingPanel.Children.Add(qr);
        pairingPanel.Children.Add(pairing);

        panel.Children.Add(Section(
            "Waiter app connection",
            "Scan this QR from the waiter app to configure both LAN and cloud routes. Automatic mode prefers LAN, falls back to cloud when LAN is unavailable, queues offline changes when neither route is reachable, and returns to LAN automatically.",
            pairingPanel));

        var actions = new WrapPanel { Margin = new Thickness(0, 4, 0, 14) };

        var refresh = new Button { Content = "Refresh status", MinWidth = 130, Height = 38, Margin = new Thickness(0, 0, 10, 0) };
        refresh.SetBinding(Button.CommandProperty, new Binding(nameof(LanDiagnosticsViewModel.RefreshCommand)));
        actions.Children.Add(refresh);

        var toggle = new Button { MinWidth = 140, Height = 38, Margin = new Thickness(0, 0, 10, 0) };
        toggle.SetBinding(Button.ContentProperty, new Binding(nameof(LanDiagnosticsViewModel.ToggleTerminalLabel)));
        toggle.SetBinding(Button.CommandProperty, new Binding(nameof(LanDiagnosticsViewModel.ToggleSelectedTerminalCommand)));
        actions.Children.Add(toggle);

        var unpair = new Button { Content = "Unpair device", MinWidth = 130, Height = 38 };
        unpair.SetBinding(Button.CommandProperty, new Binding(nameof(LanDiagnosticsViewModel.UnpairSelectedTerminalCommand)));
        actions.Children.Add(unpair);
        panel.Children.Add(actions);

        panel.Children.Add(new TextBlock
        {
            Text = "Paired waiter devices",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 8, 0, 10)
        });

        var grid = GridFor(diagnostics.Terminals);
        grid.MinHeight = 320;
        grid.SetBinding(DataGrid.SelectedItemProperty, new Binding(nameof(LanDiagnosticsViewModel.SelectedTerminal))
        {
            Mode = BindingMode.TwoWay
        });
        grid.Columns.Add(Column("Device", "DisplayName", 220));
        grid.Columns.Add(Column("Type", "ClientType", 120));
        grid.Columns.Add(Column("User", "UserName", 180));
        grid.Columns.Add(Column("Role", "UserRole", 120));
        grid.Columns.Add(Column("Status", "Status", 120));
        grid.Columns.Add(Column("Enabled", "IsEnabled", 90));
        grid.Columns.Add(Column("IP address", "LastIpAddress", 160));
        grid.Columns.Add(Column("Last seen", "LastSeenAtUtc", 210));
        panel.Children.Add(grid);

        panel.Children.Add(Card(
            "Device control",
            "Select a waiter device above to enable/disable or unpair it. Device-management actions require an Owner or Manager session. Disabling or unpairing a terminal does not disable the restaurant desktop or other LAN terminals."));

        return Scroll(panel);
    }

    private static FrameworkElement Placeholder(string route)
    {
        var note = new TextBlock
        {
            Text = "The local service layer already exists; the full editing surface is being connected.",
            TextWrapping = TextWrapping.Wrap,
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        return Section(route, "This management screen is part of the operational UI completion phase.", note);
    }

    private static StackPanel Stack() => new() { Margin = new Thickness(0) };

    private static ScrollViewer Scroll(UIElement child) => new()
    {
        Content = child,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };

    private static Border Hero(string title, string subtitle, string network, string license)
    {
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 25,
            FontWeight = FontWeights.Bold,
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var subtitleText = new TextBlock
        {
            Text = subtitle,
            FontSize = 13,
            Margin = new Thickness(0, 6, 0, 16),
            TextWrapping = TextWrapping.Wrap,
        };
        subtitleText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        var networkText = new TextBlock
        {
            Text = $"●  {network}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 18, 0),
        };
        networkText.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");

        var licenseText = new TextBlock
        {
            Text = license,
            FontWeight = FontWeights.SemiBold,
        };
        licenseText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(networkText);
        row.Children.Add(licenseText);

        var panel = new StackPanel();
        panel.Children.Add(titleText);
        panel.Children.Add(subtitleText);
        panel.Children.Add(row);

        var border = new Border
        {
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(22),
            Margin = new Thickness(0, 0, 12, 18),
            BorderThickness = new Thickness(1),
            Child = panel,
        };
        border.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        border.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 24,
            ShadowDepth = 5,
            Opacity = 0.12,
            Color = System.Windows.Media.Color.FromRgb(7, 24, 39),
        };
        return border;
    }

    private static Border Cards(params (string Label, string Value)[] values)
    {
        var wrap = new WrapPanel();
        foreach (var value in values)
            wrap.Children.Add(Card(value.Label, value.Value, 235));
        return new Border { Child = wrap, Margin = new Thickness(0, 0, 0, 6) };
    }

    private static Border Card(string title, string value, double width = double.NaN)
    {
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");

        var valueText = new TextBlock
        {
            Text = value,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        valueText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var panel = new StackPanel();
        panel.Children.Add(titleText);
        panel.Children.Add(valueText);

        var border = new Border
        {
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 12, 12),
            Width = width,
            BorderThickness = new Thickness(1),
            Child = panel,
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

    private static FrameworkElement Section(string title, string subtitle, UIElement content)
    {
        var panel = Stack();
        var titleText = new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var subtitleText = new TextBlock
        {
            Text = subtitle,
            Margin = new Thickness(0, 4, 0, 16),
            TextWrapping = TextWrapping.Wrap,
        };
        subtitleText.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        panel.Children.Add(titleText);
        panel.Children.Add(subtitleText);
        panel.Children.Add(content);
        return Scroll(panel);
    }

    private static DataGrid GridFor(object items)
    {
        var grid = new DataGrid
        {
            ItemsSource = (System.Collections.IEnumerable)items,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            MinHeight = 430,
            BorderThickness = new Thickness(1),
            AlternationCount = 2,
        };
        grid.SetResourceReference(DataGrid.BackgroundProperty, "SurfaceBrush");
        grid.SetResourceReference(DataGrid.BorderBrushProperty, "BorderBrush");
        return grid;
    }

    private static DataGridTextColumn Column(string header, string property, double width) => new() { Header = header, Binding = new Binding(property), Width = width };

    private sealed record TableRow(string Id, string Area, string Code, string Name, int Capacity, string Status);
    private sealed record KitchenRow(string Id, string TicketNumber, string Station, string Status, DateTimeOffset QueuedAt);
    private sealed record OrderRow(string Id, string ClientOrderId, string Waiter, string Status, int Guests, decimal Total, DateTimeOffset UpdatedAt);
    private sealed record MenuRow(string Sku, string Name, string Category, decimal Price, string Currency, bool Available);
    private sealed record InventoryRow(string Sku, string Name, string BaseUnit, string PurchaseUnit, decimal Quantity, decimal ReorderLevel, decimal AverageCost, decimal StockValue);
    private sealed record PurchaseRow(string Number, string Supplier, string Status, decimal Total, DateTimeOffset OrderedAt, DateTimeOffset? CompletedAt);
    private sealed record SupplierRow(string Code, string Name, string? Phone, string? Email);
    private sealed record ReceiptRow(string Number, string Status, DateTimeOffset ReceivedAt, string PurchaseOrderId);
    private sealed record StaffRow(string Name, string Email, string Role, bool Active);
    private sealed record ShiftRow(string Name, string Role, string Status, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, int BreakMinutes);
    private sealed record ExpenseBranchChoice(string Id, string Name);
    private sealed record ExpenseRow(DateOnly Date, string Branch, string Category, string Description, decimal Amount, string Currency, string Method, string? Reference);
}