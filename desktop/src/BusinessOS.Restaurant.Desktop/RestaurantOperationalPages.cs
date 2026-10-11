using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using BusinessOS.Restaurant.Application.OperationalData;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.Desktop;

internal static class RestaurantOperationalPages
{
    public static async Task<FrameworkElement> CreateAsync(string route, LanDiagnosticsViewModel diagnostics)
    {
        if (route is "inventory" or "purchases" or "recipes")
        {
            var modules = await new LocalRestaurantSettingsService(new LocalDatabaseFactory()).GetModulesAsync();
            if (route == "inventory" && !modules.InventoryEnabled ||
                route == "purchases" && (!modules.InventoryEnabled || !modules.PurchasingEnabled) ||
                route == "recipes" && !modules.RecipesEnabled)
            {
                return Card("Module disabled",
                    "This module is turned off for the restaurant. An Owner can re-enable it in Settings → Modules. Historical data is preserved.");
            }
        }

        return route switch
        {
            "dashboard" => await DashboardAsync(diagnostics),
            "tables" => await OperationalActionViews.TablesAsync(),
            "kitchen" => await OperationalActionViews.KitchenAsync(),
            "pos" => await OperationalActionViews.PosAsync(),
            "menu" => await MenuAsync(),
            "recipes" => await MenuAsync(recipesOnly: true),
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

    private static async Task OpenStandaloneCatalogAsync()
    {
        try
        {
            var signedIn = await new BusinessOS.Restaurant.Authentication.WindowsSessionStore().LoadAsync()
                ?? throw new InvalidOperationException("Sign in before managing the local catalog.");
            var activation = await new BusinessOS.Restaurant.Licensing.WindowsActivationStore().LoadAsync();
            if (!BusinessOS.Restaurant.Licensing.DesktopOperatingMode.IsStandalone(activation))
                throw new InvalidOperationException("Local catalog editing is only for signed standalone installations.");

            var dialog = new StandaloneCatalogWindow(signedIn.User.Role)
            {
                Owner = System.Windows.Application.Current?.MainWindow,
            };
            DesktopGlassDialog.ShowDialog(dialog);
            if (System.Windows.Application.Current?.MainWindow?.DataContext is MainWindowViewModel vm)
                await vm.RefreshCommand.ExecuteAsync(null);
        }
        catch (Exception e) { DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, e.Message); }
    }

    internal static async Task OpenCloudManagementAsync(string section)
    {
        try
        {
            var activation = await new BusinessOS.Restaurant.Licensing.WindowsActivationStore().LoadAsync();
            if (BusinessOS.Restaurant.Licensing.DesktopOperatingMode.IsStandalone(activation))
                throw new InvalidOperationException("Standalone Offline cannot open the web catalog editor. Existing menus remain available locally; use the standalone local catalog editor when provided.");

            var session = await new BusinessOS.Restaurant.Authentication.WindowsSessionStore().LoadAsync()
                ?? throw new InvalidOperationException("Sign in before editing the restaurant catalog.");
            var dialog = new DesktopCloudManagementWindow(session, section)
            {
                Owner = System.Windows.Application.Current?.MainWindow,
            };
            DesktopGlassDialog.ShowDialog(dialog);
            if (dialog.SavedChanges)
            {
                var refreshed = await RefreshOperationalSnapshotAsync();
                DesktopNoticeEvents.Publish(refreshed ? DesktopNoticeLevel.Success : DesktopNoticeLevel.Info,
                    refreshed ? "Restaurant catalog synchronized."
                        : "Saved online. Reconnect and sync to update local data.");
                if (refreshed && System.Windows.Application.Current?.MainWindow?.DataContext is MainWindowViewModel vm)
                    await vm.RefreshCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message);
        }
    }

    private static async Task<bool> RefreshOperationalSnapshotAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        return await new BusinessOS.Restaurant.Sync.OperationalDataRefreshService(
            new BusinessOS.Restaurant.Licensing.WindowsActivationStore(),
            new BusinessOS.Restaurant.Authentication.WindowsSessionStore(),
            new BusinessOS.Restaurant.Licensing.ConnectionSettingsStore(),
            new BusinessOS.Restaurant.Sync.CloudOperationalDataClient(http),
            new OperationalSnapshotStore(new LocalDatabaseFactory())).RefreshIfPossibleAsync();
    }

    private static async Task<FrameworkElement> DashboardAsync(LanDiagnosticsViewModel diagnostics)
    {
        var factory = new LocalDatabaseFactory();
        await factory.EnsureCreatedAsync();
        await using var db = factory.Create();

        var now = DateTimeOffset.Now;
        // Cancelled orders and unavailable/reserved tables are not active service.
        var openOrders = await db.Orders.CountAsync(x =>
            x.Status != "closed" && x.Status != "cancelled");
        var activeTables = await (from table in db.DiningTables.AsNoTracking()
            join area in db.DiningAreas.AsNoTracking() on table.DiningAreaId equals area.Id
            join branch in db.Branches.AsNoTracking() on area.BranchId equals branch.Id
            where table.IsActive && area.IsActive && branch.IsActive && table.Status == "occupied"
            select table.Id).CountAsync();
        var activeKot = await db.KitchenTickets.CountAsync(x =>
            x.Status == "queued" || x.Status == "active" || x.Status == "preparing" || x.Status == "ready");
        // Read only bill timestamps and totals; interpret the business day in local time.
        // The chart uses exactly the same billed totals as the headline (never sample points).
        var billRows = await db.Bills.AsNoTracking()
            .Select(x => new { x.IssuedAt, x.Total })
            .ToListAsync();
        var salesTrend = DashboardSalesTrend.Aggregate(
            now, billRows.Select(x => (x.IssuedAt, x.Total)));
        var sales = salesTrend.Total;

        // Cash received is not revenue; stock purchases are not the cost of
        // food until consumption is recorded. A missing historical recipe
        // unit cost must never be silently mistaken for zero.
        var dailyProfit = await DesktopUiWork.RunAsync(async () =>
        {
            await using var accounting = factory.Create();
            var since = salesTrend.BusinessDay;
            var expenses = await accounting.Expenses.AsNoTracking()
                .Where(x => x.ExpenseDate == since)
                .Select(x => x.Amount).ToListAsync();
            var cashRows = await accounting.Payments.AsNoTracking()
                .Where(x => x.Status == "posted")
                .Select(x => new { x.ReceivedAt, x.Amount })
                .ToListAsync();
            var usedRows = await accounting.StockMovements.AsNoTracking()
                .Where(x => x.MovementType == "consumption")
                .Select(x => new { x.SourceType, x.QuantityDelta, x.UnitCost, x.OccurredAt })
                .ToListAsync();
            var recipe = await accounting.RestaurantSettings.AsNoTracking()
                .Where(x => x.Key == "automatic_recipe_consumption_enabled")
                .Select(x => x.Value).FirstOrDefaultAsync();
            var mode = recipe is null || recipe.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                recipe == "1" ? "Recipe" : "Manual physical count";
            return DashboardProfitLoss.Compute(
                sales,
                usedRows.Where(x => DateOnly.FromDateTime(x.OccurredAt.LocalDateTime) == since)
                    .Select(x => (x.SourceType, x.QuantityDelta, x.UnitCost)),
                expenses.Sum(), cashRows
                    .Where(x => DateOnly.FromDateTime(x.ReceivedAt.LocalDateTime) == since)
                    .Sum(x => x.Amount), mode);
        });

        // Read-only, branch-aware inventory alerts from the same local database as
        // the Inventory screen. No synthetic zero balances for uninitialized branches.
        var stockBalances = await (
            from balance in db.InventoryBalances.AsNoTracking()
            join item in db.InventoryItems.AsNoTracking()
                on balance.InventoryItemId equals item.Id
            join branch in db.Branches.AsNoTracking()
                on balance.BranchId equals branch.Id
            where item.IsActive && branch.IsActive
            select new DashboardStockBalance(
                branch.Name, item.Name, item.BaseUnit, balance.Quantity, item.ReorderLevel))
            .ToListAsync();
        var stockAlerts = DashboardStockAlerts.Find(stockBalances);
        var trackedReorderBalances = stockBalances.Count(row => row.ReorderLevel > 0m);

        var root = new StackPanel();

        root.Children.Add(DashboardQuickActions());

        var netLabel = dailyProfit.IsComplete
            ? $"AFN {dailyProfit.NetOperatingProfit:N2}"
            : $"AFN {dailyProfit.NetOperatingProfit:N2}*";
        root.Children.Add(DashboardPanel(
            "Profit & Loss · Today",
            $"Billed revenue and actual ingredient use · {dailyProfit.ConsumptionMode} costing",
            BuildStatusRows(
                ("Billed revenue", $"AFN {dailyProfit.Revenue:N2}"),
                ("Ingredient consumption (valued)", $"AFN {dailyProfit.CostedFoodConsumption:N2}"),
                ("Gross profit (valued)", $"AFN {dailyProfit.GrossProfit:N2}"),
                ("Other expenses", $"AFN {dailyProfit.OperatingExpenses:N2}"),
                ("Operating P&L", netLabel),
                ("Cash collected (separate)", $"AFN {dailyProfit.CashCollected:N2}"),
                ("Accounting basis", dailyProfit.IsComplete
                    ? "Consumed stock, not purchased stock"
                    : dailyProfit.ConsumptionMode.StartsWith("Manual", StringComparison.OrdinalIgnoreCase)
                        ? $"PROVISIONAL · depends on completed physical stock counts; {dailyProfit.UnvaluedConsumptionMovements} unvalued entries"
                        : $"PROVISIONAL · {dailyProfit.UnvaluedConsumptionMovements} historical ingredient use entries lack cost"))));

        root.Children.Add(DashboardCards(
            ("▥", "TOTAL SALES TODAY", $"AFN {sales:N2}", "Restaurant sales today", Color.FromRgb(34, 197, 94)),
            ("▣", "OPEN ORDERS", openOrders.ToString(), "Orders currently in progress", Color.FromRgb(14, 165, 233)),
            ("▦", "ACTIVE TABLES", activeTables.ToString(), "Occupied dining tables", Color.FromRgb(245, 158, 11)),
            ("☷", "KITCHEN / KOT", activeKot.ToString(), "Active production tickets", Color.FromRgb(236, 72, 153)),
            ("◉", "NETWORK MODE", diagnostics.NetworkMode, "Current sync route", Color.FromRgb(124, 58, 237))));

        var overviewGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        overviewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        overviewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        overviewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var operations = DashboardPanel(
            "Restaurant Overview",
            $"Hourly billed sales · {salesTrend.BusinessDay:dd MMM yyyy} (local time)",
            BuildOperationsOverview(sales, openOrders, activeTables, activeKot, salesTrend, now.Hour));
        Grid.SetColumn(operations, 0);
        overviewGrid.Children.Add(operations);

        var right = new StackPanel();
        right.Children.Add(DashboardPanel(
            "License & Connectivity",
            diagnostics.LeaseStatus,
            BuildStatusRows(
                ("Network", diagnostics.NetworkMode),
                ("Waiter devices", diagnostics.TerminalSummary),
                ("Local-first", "Available"))));
        right.Children.Add(DashboardPanel(
            "Quick Status",
            "Today at a glance",
            BuildStatusRows(
                ("Orders", openOrders.ToString()),
                ("Tables", activeTables.ToString()),
                ("Kitchen", activeKot.ToString()))));
        Grid.SetColumn(right, 2);
        overviewGrid.Children.Add(right);

        // Keep the detail column useful even on 1024 px cashier displays.
        overviewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        overviewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.SizeChanged += (_, _) =>
        {
            var narrow = root.ActualWidth < 920;
            overviewGrid.ColumnDefinitions[0].Width = new GridLength(narrow ? 1 : 3, GridUnitType.Star);
            overviewGrid.ColumnDefinitions[1].Width = new GridLength(narrow ? 0 : 14);
            overviewGrid.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(right, narrow ? 0 : 2);
            Grid.SetRow(right, narrow ? 1 : 0);
        };
        root.Children.Add(overviewGrid);

        var lowerGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        lowerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lowerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        lowerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var floorPanel = DashboardPanel(
            "Floor & Orders",
            "Service activity",
            BuildFeatureSummary(
                "▦",
                activeTables == 0 ? "Dining floor is clear" : $"{activeTables} active table(s)",
                openOrders == 0 ? "No open orders right now." : $"{openOrders} order(s) are still open.",
                Color.FromRgb(14, 165, 233)));
        lowerGrid.Children.Add(floorPanel);

        var kitchenPanel = DashboardPanel(
            "Kitchen Production",
            "KOT execution",
            BuildFeatureSummary(
                "☷",
                activeKot == 0 ? "Kitchen queue is clear" : $"{activeKot} active KOT ticket(s)",
                activeKot == 0
                    ? "New KOT rounds will appear here as orders are sent."
                    : "Queue, preparing and ready tickets are being tracked locally.",
                Color.FromRgb(245, 158, 11)));
        Grid.SetColumn(kitchenPanel, 2);
        lowerGrid.Children.Add(kitchenPanel);

        lowerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        lowerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.SizeChanged += (_, _) =>
        {
            var narrow = root.ActualWidth < 770;
            lowerGrid.ColumnDefinitions[1].Width = new GridLength(narrow ? 0 : 14);
            lowerGrid.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(kitchenPanel, narrow ? 0 : 2);
            Grid.SetRow(kitchenPanel, narrow ? 1 : 0);
        };
        root.Children.Add(lowerGrid);
        root.Children.Add(DashboardPanel(
            "Stock Alerts",
            stockAlerts.Count > 0
                ? $"{stockAlerts.Count} branch ingredient balance(s) at or below reorder level"
                : "Active branch inventory thresholds",
            BuildStockAlertSummary(stockAlerts, stockBalances.Count, trackedReorderBalances)));
        root.Children.Add(DashboardPanel(
            "Live Operations",
            "Desktop, LAN and offline health",
            new TextBlock
            {
                Text = diagnostics.StatusMessage,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5,
                Margin = new Thickness(2, 2, 2, 2),
            }));

        return Scroll(root);
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

    private static async Task<FrameworkElement> MenuAsync(bool recipesOnly = false)
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
        var panel = Stack();
        panel.Margin = new Thickness(4);
        if (!recipesOnly)
        {
            var overview = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            overview.Children.Add(Card("MENU ITEMS", rows.Count.ToString()));
            overview.Children.Add(Card("AVAILABLE", rows.Count(item => item.Available).ToString()));
            overview.Children.Add(Card("CATEGORIES", rows.Select(item => item.Category).Distinct().Count().ToString()));
            panel.Children.Add(overview);
        }
        panel.Children.Add(recipesOnly
            ? Card("Recipes & Food Cost", "Review ingredient quantities, create recipe versions and preview food cost in AFN. Recipes are stored per restaurant branch.")
            : Card("Menu catalog", "Manage restaurant menu items and their tablet-visible images."));
        // Menus are cloud-owned reference records. Editing only the SQLite
        // snapshot would silently disappear after the next cloud refresh.
        var activation = await new BusinessOS.Restaurant.Licensing.WindowsActivationStore().LoadAsync();
        var standalone = BusinessOS.Restaurant.Licensing.DesktopOperatingMode.IsStandalone(activation);
        var create = new Button { Content = standalone ? "+ Add Local Menu Item" : "+ Add Menu Item (Tenant)",
            MinWidth = 185, Height = 38, Margin = new Thickness(0, 8, 0, 12) };
        create.Click += async (_, _) =>
        {
            if (standalone) await OpenStandaloneCatalogAsync();
            else await OpenCloudManagementAsync("menu_items");
        };
        var moduleFlags = await new LocalRestaurantSettingsService(factory).GetModulesAsync();
        var recipes = new Button { Content = "View Recipes", Height = 38, Margin = new Thickness(0, 4, 0, 8) };
        recipes.Visibility = moduleFlags.RecipesEnabled ? Visibility.Visible : Visibility.Collapsed;
        recipes.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        recipes.Click += async (_, _) =>
        {
            await using var recipeDb = factory.Create();
            // Display every stored component in its actual ingredient stock
            // unit; the authoritative costing/consumption quantity stays in
            // QuantityBase, including older recipes without a selected UOM.
            var versions = await (from version in recipeDb.Recipes.AsNoTracking()
                                  join menuItem in recipeDb.MenuItems.AsNoTracking()
                                      on version.MenuItemId equals menuItem.Id
                                  join branch in recipeDb.Branches.AsNoTracking()
                                      on version.BranchId equals branch.Id
                                  join recipeItem in recipeDb.RecipeItems.AsNoTracking()
                                      on version.Id equals recipeItem.RecipeId
                                  join ingredient in recipeDb.InventoryItems.AsNoTracking()
                                      on recipeItem.InventoryItemId equals ingredient.Id
                                  orderby menuItem.Name, version.Version descending, ingredient.Name
                                  select new
                                  {
                                      Branch = branch.Name,
                                      Menu = menuItem.Name,
                                      Recipe = version.Name,
                                      version.Version,
                                      Active = version.IsActive,
                                      Ingredient = ingredient.Name,
                                      Quantity = recipeItem.QuantityBase,
                                      Unit = ingredient.BaseUnit,
                                  }).ToListAsync();
            var dialog = new Window
            {
                Title = "Recipe Ingredients & Units",
                Width = 1020, Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var owner = System.Windows.Application.Current?.MainWindow;
            if (owner is not null) dialog.Owner = owner;
            var recipeGrid = GridFor(versions);
            recipeGrid.AutoGenerateColumns = true;
            dialog.Content = recipeGrid;
            DesktopGlassDialog.ShowDialog(dialog);
        };
        // Cloud-owned catalog edits never disappear on the next snapshot refresh.
        var editCatalog = new Button { Content = standalone
            ? "+ Manage Local Branches, Tables, Menu & Images"
            : "+ Create / Edit Menu, Categories & Images",
            Height = 38, Margin = new Thickness(0, 6, 0, 8) };
        editCatalog.Click += async (_, _) =>
        {
            if (standalone) await OpenStandaloneCatalogAsync();
            else await OpenCloudManagementAsync("menu_items");
        };
        // Group menu actions in a responsive toolbar, not full-width bars.
        var menuActions = new WrapPanel
        {
            Margin = new Thickness(0, 6, 0, 12),
            Orientation = Orientation.Horizontal,
        };
        foreach (var action in recipesOnly ? new[] { recipes } : new[] { editCatalog, create, recipes })
        {
            action.HorizontalAlignment = HorizontalAlignment.Left;
            action.MinWidth = 0;
            action.Margin = new Thickness(0, 0, 10, 10);
            menuActions.Children.Add(action);
        }
        panel.Children.Add(menuActions);
        var recipeCreate = new Button { Content = "+ Create Recipe Version", Height = 38, Margin = new Thickness(0, 4, 0, 8) };
        recipeCreate.Click += async (_, _) =>
        {
            await using var lookup = factory.Create();
            var branches = await lookup.Branches.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
            var menuItems = await lookup.MenuItems.AsNoTracking().Where(x => x.IsAvailable).OrderBy(x => x.Name).ToListAsync();
            var ingredients = await lookup.InventoryItems.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
            var dialog = new Window
            {
                Title = "New Recipe Version", Width = 900, Height = 730,
                MinWidth = 760, MinHeight = 660,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            dialog.SetResourceReference(Window.BackgroundProperty, "SurfaceBrush");
            var owner = System.Windows.Application.Current?.MainWindow;
            if (owner is not null) dialog.Owner = owner;
            var form = Stack();
            form.Margin = new Thickness(18);
            var branch = new ComboBox { Name = "RecipeBranchInput", ItemsSource = branches, DisplayMemberPath = "Name", Height = 34 };
            var menu = new ComboBox { Name = "RecipeMenuItemInput", ItemsSource = menuItems, DisplayMemberPath = "Name", Height = 34 };
            var ingredient = new ComboBox { Name = "RecipeIngredientInput", ItemsSource = ingredients, DisplayMemberPath = "Name", Height = 34 };
            var quantity = new TextBox { Name = "RecipeQuantityInput", Text = "1", Height = 34 };
            var recipeUnit = new ComboBox { Name = "RecipeUnitInput", Height = 34, MinWidth = 110 };
            var unitHelp = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 5) };
            unitHelp.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            ingredient.SelectionChanged += (_, _) =>
            {
                if (ingredient.SelectedItem is LocalInventoryItem selected)
                {
                    recipeUnit.ItemsSource = RecipeUnitConversion.AllowedUnits(selected.BaseUnit);
                    recipeUnit.SelectedItem = selected.BaseUnit.ToLowerInvariant();
                    unitHelp.Text = $"Stock is tracked in {selected.BaseUnit}. " +
                                    "Quantity and unit will be converted to that stock unit for inventory and food cost.";
                }
            };
            ingredient.SelectedIndex = ingredients.Count > 0 ? 0 : -1;
            foreach (var entry in new (string Label, FrameworkElement Input)[]
            {
                ("Branch", branch), ("Menu item", menu), ("Ingredient", ingredient),
                ("Recipe quantity", quantity), ("Recipe unit (kg, g, l, ml, pcs as applicable)", recipeUnit)
            })
            {
                form.Children.Add(new TextBlock { Text = entry.Label, Margin = new Thickness(0, 8, 0, 3) });
                form.Children.Add(entry.Input);
            }
            form.Children.Add(unitHelp);
            var components = new List<LocalRecipeComponentRequest>();
            var summary = new TextBlock { Text = "Add at least one ingredient.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
            var add = new Button { Content = "+ Add Recipe Ingredient", Height = 36 };
            add.Click += (_, _) =>
            {
                if (ingredient.SelectedItem is not LocalInventoryItem selected ||
                    !decimal.TryParse(quantity.Text, out var amount) || amount <= 0 ||
                    recipeUnit.SelectedItem is not string unit)
                {
                    summary.Text = "Choose an ingredient, positive recipe quantity and a compatible unit.";
                    return;
                }
                decimal quantityBase;
                try { quantityBase = RecipeUnitConversion.ToBase(amount, unit, selected.BaseUnit); }
                catch (ArgumentException ex) { summary.Text = ex.Message; return; }
                if (components.Any(x => x.InventoryItemId == selected.Id))
                {
                    summary.Text = "This ingredient has already been added.";
                    return;
                }
                components.Add(new LocalRecipeComponentRequest(selected.Id, quantityBase));
                summary.Text = string.Join("\n", components.Select(x =>
                    ingredients.First(y => y.Id == x.InventoryItemId).Name + ": " +
                    x.QuantityBase.ToString("0.####") + " " +
                    ingredients.First(y => y.Id == x.InventoryItemId).BaseUnit));
            };
            form.Children.Add(add);
            // The selected ingredients have their own scrollable summary column.
            var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
            var cancel = new Button { Content = "Cancel", Height = 40, MinWidth = 120 };
            cancel.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
            var remove = new Button { Content = "Remove selected ingredient", Height = 36 };
            remove.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
            var save = new Button { Content = "Save Recipe", Height = 38 };
            save.Click += async (_, _) =>
            {
                if (branch.SelectedItem is not LocalBranch selectedBranch ||
                    menu.SelectedItem is not LocalMenuItem selectedMenu || components.Count == 0)
                {
                    feedback.Text = "Select branch, menu item and at least one ingredient.";
                    return;
                }

                // Microsoft.Data.Sqlite runs most async commands synchronously.
                // Never execute its lock waits or transaction commit on the WPF
                // dispatcher, particularly while ShowDialog runs a modal loop.
                // Snapshot inputs and guard against repeated clicks/versions.
                save.IsEnabled = false;
                save.Content = "Saving recipe…";
                cancel.IsEnabled = false;
                add.IsEnabled = false;
                remove.IsEnabled = false;
                feedback.Text = "Saving recipe to local inventory. Please wait…";
                var snapshot = components.ToArray();
                var branchId = selectedBranch.Id;
                var menuId = selectedMenu.Id;
                var recipeName = selectedMenu.Name;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try
                {
                    await Task.Run(async () =>
                    {
                        var actor = await new DesktopRestaurantWorkflowService()
                            .CurrentPrincipalAsync(timeout.Token).ConfigureAwait(false);
                        await new LocalInventoryService(factory).CreateRecipeVersionAsync(
                            branchId, menuId, recipeName, snapshot, actor, timeout.Token)
                            .ConfigureAwait(false);
                    }, timeout.Token).WaitAsync(timeout.Token);
                    dialog.DialogResult = true;
                }
                catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
                {
                    cancel.IsEnabled = true;
                    App.LogRecoverableException("recipe-create-timeout", ex);
                    feedback.Text = "Recipe save took too long. Close this window and refresh Recipes " +
                        "to check if it was saved before trying again.";
                    // A cancelled request may already have committed; do not let a
                    // second click silently create an additional recipe version.
                    save.Content = "Check saved recipes before retrying";
                }
                catch (Exception ex)
                {
                    cancel.IsEnabled = true;
                    App.LogRecoverableException("recipe-create-save", ex);
                    feedback.Text = "Recipe could not be confirmed: " + ex.Message +
                        " Check existing versions before retrying.";
                    save.Content = "Close and verify recipe versions";
                }
            };
            var ingredientRows = new ListBox
            {
                Name = "RecipeSelectedIngredients",
                DisplayMemberPath = "Value",
                MinHeight = 100,
                BorderThickness = new Thickness(0),
            };
            ingredientRows.SetResourceReference(Control.BackgroundProperty, "InputBackgroundBrush");
            remove.Click += (_, _) =>
            {
                if (ingredientRows.SelectedItem is KeyValuePair<string, string> selected)
                {
                    components.RemoveAll(component => component.InventoryItemId == selected.Key);
                    RefreshIngredients();
                }
            };
            void RefreshIngredients()
            {
                ingredientRows.ItemsSource = components.Select(component =>
                {
                    var item = ingredients.First(x => x.Id == component.InventoryItemId);
                    return new KeyValuePair<string, string>(item.Id,
                        item.Name + "   ·   " + component.QuantityBase.ToString("0.####") + " " + item.BaseUnit);
                }).ToList();
                summary.Text = components.Count == 0
                    ? "No ingredients selected yet."
                    : components.Count + " ingredient(s) selected";
            }
            add.Click += (_, _) => RefreshIngredients();
            RefreshIngredients();

            cancel.Click += (_, _) => dialog.Close();

            dialog.Content = BuildRecipeVersionEditor(
                form, ingredientRows, remove, summary, feedback, save, cancel);
            if (DesktopGlassDialog.ShowDialog(dialog) == true)
            {
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success,
                    "Recipe version saved. Refresh Recipes to view it.");
                if (System.Windows.Application.Current?.MainWindow?.DataContext is MainWindowViewModel vm)
                    await vm.RefreshCommand.ExecuteAsync(null);
            }
        };
        recipeCreate.HorizontalAlignment = HorizontalAlignment.Left;
        recipeCreate.Margin = new Thickness(0, 0, 10, 10);
        recipeCreate.Visibility = moduleFlags.RecipesEnabled ? Visibility.Visible : Visibility.Collapsed;
        menuActions.Children.Add(recipeCreate);
        var costPreview = new Button { Content = "Recipe Food-Cost Preview", Height = 38, Margin = new Thickness(0, 4, 0, 8) };
        costPreview.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        costPreview.Click += async (_, _) =>
        {
            await using var costDb = factory.Create();
            var valuations = await costDb.InventoryValuations.AsNoTracking().ToListAsync();
            var components = await costDb.RecipeItems.AsNoTracking().ToListAsync();
            var ingredients = await costDb.InventoryItems.AsNoTracking().ToDictionaryAsync(x => x.Id);
            var menuItems = await costDb.MenuItems.AsNoTracking().ToDictionaryAsync(x => x.Id);
            var branches = await costDb.Branches.AsNoTracking().ToDictionaryAsync(x => x.Id);
            var versions = await costDb.Recipes.AsNoTracking().Where(x => x.IsActive).ToListAsync();
            var unitCosts = valuations.ToDictionary(x => (x.BranchId, x.InventoryItemId), x => x.AverageUnitCost);
            var componentGroups = components.ToLookup(x => x.RecipeId);

            var preview = versions.Select(version =>
            {
                var recipeItems = componentGroups[version.Id].ToList();
                decimal total = 0m;
                var missing = recipeItems.Count == 0 ? 1 : 0;
                foreach (var component in recipeItems)
                {
                    if (component.QuantityBase <= 0 ||
                        !ingredients.TryGetValue(component.InventoryItemId, out var ingredient) ||
                        !ingredient.IsActive ||
                        !unitCosts.TryGetValue((version.BranchId, component.InventoryItemId), out var unitCost) ||
                        unitCost <= 0)
                    {
                        missing++;
                        continue;
                    }
                    total += component.QuantityBase * unitCost;
                }

                menuItems.TryGetValue(version.MenuItemId, out var menuItem);
                branches.TryGetValue(version.BranchId, out var selectedBranch);
                var salePrice = menuItem?.Price ?? 0m;
                var reliable = missing == 0 && salePrice > 0;
                return new
                {
                    Branch = selectedBranch?.Name ?? version.BranchId,
                    Menu = menuItem?.Name ?? version.MenuItemId,
                    version.Version,
                    IngredientCostAfn = missing == 0 ? Math.Round(total, 2).ToString("0.00") : "N/A",
                    SalePriceAfn = salePrice,
                    FoodCostPercent = reliable ? Math.Round(total / salePrice * 100m, 2).ToString("0.00") + "%" : "N/A",
                    GrossMarginAfn = reliable ? Math.Round(salePrice - total, 2).ToString("0.00") : "N/A",
                    GrossMarginPercent = reliable ? Math.Round((salePrice - total) / salePrice * 100m, 2).ToString("0.00") + "%" : "N/A",
                    MissingCosts = missing,
                };
            }).ToList();

            var dialog = new Window { Title = "Estimated Recipe Food Cost (AFN)", Width = 1040, Height = 470, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var owner = System.Windows.Application.Current?.MainWindow;
            if (owner is not null) dialog.Owner = owner;
            var costGrid = GridFor(preview);
            costGrid.AutoGenerateColumns = true;
            dialog.Content = costGrid;
            DesktopGlassDialog.ShowDialog(dialog);
        };
        costPreview.HorizontalAlignment = HorizontalAlignment.Left;
        costPreview.Margin = new Thickness(0, 0, 10, 10);
        costPreview.Visibility = moduleFlags.RecipesEnabled ? Visibility.Visible : Visibility.Collapsed;
        menuActions.Children.Add(costPreview);
        if (recipesOnly)
        {
            // A first-class recipe workspace must list saved versions on the
            // page itself; the View Recipes dialog drills into ingredients.
            var recipeRows = await (from version in db.Recipes.AsNoTracking()
                join menuItem in db.MenuItems.AsNoTracking() on version.MenuItemId equals menuItem.Id
                join branch in db.Branches.AsNoTracking() on version.BranchId equals branch.Id
                orderby menuItem.Name, version.Version descending
                select new
                {
                    Branch = branch.Name,
                    MenuItem = menuItem.Name,
                    Recipe = version.Name,
                    version.Version,
                    Active = version.IsActive,
                }).ToListAsync();

            panel.Children.Add(Card("Recipe versions",
                recipeRows.Count == 0
                    ? "No recipes saved yet. Use Create Recipe Version to begin."
                    : $"{recipeRows.Count} saved version(s). Use View Recipes for ingredient quantities and units."));
            var recipeGrid = GridFor(recipeRows);
            recipeGrid.AutoGenerateColumns = true;
            var recipeSearch = new TextBox
            {
                Name = "RecipesSearchBox",
                Height = 40,
                Margin = new Thickness(0, 6, 0, 14),
                ToolTip = "Search by menu item, recipe or branch",
            };
            recipeSearch.SetResourceReference(Control.BackgroundProperty, "InputBackgroundBrush");
            recipeSearch.TextChanged += (_, _) =>
            {
                var term = recipeSearch.Text.Trim();
                recipeGrid.ItemsSource = recipeRows.Where(row =>
                    row.MenuItem.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    row.Recipe.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    row.Branch.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
            };
            panel.Children.Add(new TextBlock { Text = "Search recipe versions", FontWeight = FontWeights.SemiBold });
            panel.Children.Add(recipeSearch);
            panel.Children.Add(recipeGrid);
        }
        else
        {
            var menuSearch = new TextBox
            {
                Name = "RestaurantMenuSearchBox",
                Height = 40,
                Margin = new Thickness(0, 6, 0, 14),
                ToolTip = "Search menu by item name, SKU or category",
            };
            menuSearch.SetResourceReference(Control.BackgroundProperty, "InputBackgroundBrush");
            menuSearch.TextChanged += (_, _) =>
            {
                var term = menuSearch.Text.Trim();
                grid.ItemsSource = rows.Where(row =>
                    row.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    row.Sku.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    row.Category.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
            };
            panel.Children.Add(new TextBlock { Text = "Search items, SKUs and categories", FontWeight = FontWeights.SemiBold });
            panel.Children.Add(menuSearch);
            panel.Children.Add(grid);
        }
        return Scroll(WorkspaceFrostedSurface.Wrap(panel));
    }

    /// <summary>
    /// Two-pane recipe editor with a pinned save footer and a visible ingredient
    /// summary. Only the editor controls scroll; saving never blocks the WPF UI.
    /// </summary>
    private static FrameworkElement BuildRecipeVersionEditor(
        StackPanel form, ListBox ingredientRows, Button remove,
        TextBlock summary, TextBlock feedback, Button save, Button cancel)
    {
        var layout = new Grid { Margin = new Thickness(0) };
        layout.SetResourceReference(Panel.BackgroundProperty, "SurfaceBrush");
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(22, 18, 22, 16) };
        header.Children.Add(new TextBlock
        {
            Text = "Build a recipe", FontSize = 24,
            FontWeight = FontWeights.Bold,
        });
        var subtitle = new TextBlock
        {
            Text = "Choose the branch and dish, then add measured ingredients. All quantities use stock base units.",
            Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap,
        };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        header.Children.Add(subtitle);
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        var columns = new Grid { Margin = new Thickness(22, 0, 22, 0) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(11, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10, GridUnitType.Star) });

        var editor = new Border
        {
            CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
            Padding = new Thickness(14), Margin = new Thickness(0, 0, 8, 0),
            Child = new ScrollViewer
            {
                Content = form,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };
        editor.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
        editor.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        columns.Children.Add(editor);

        var selected = new StackPanel { Margin = new Thickness(10) };
        selected.Children.Add(new TextBlock
        {
            Text = "Selected ingredients", FontSize = 17,
            FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8),
        });
        selected.Children.Add(summary);
        selected.Children.Add(new TextBlock
        {
            Text = "Select a row and remove it if the amount or ingredient is incorrect.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 8),
        });
        var itemsPanel = new DockPanel();
        DockPanel.SetDock(remove, Dock.Bottom);
        remove.Margin = new Thickness(0, 10, 0, 0);
        itemsPanel.Children.Add(remove);
        itemsPanel.Children.Add(ingredientRows);

        var selectedGrid = new Grid();
        selectedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        selectedGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        // Use a fixed header and a separately scrollable ingredient list.
        Grid.SetRow(selected, 0);
        selectedGrid.Children.Add(selected);
        Grid.SetRow(itemsPanel, 1);
        selectedGrid.Children.Add(itemsPanel);
        var summaryPanel = new Border
        {
            CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
            Padding = new Thickness(16), Margin = new Thickness(8, 0, 0, 0),
            Child = selectedGrid,
        };
        summaryPanel.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
        summaryPanel.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        Grid.SetColumn(summaryPanel, 1);
        columns.Children.Add(summaryPanel);
        Grid.SetRow(columns, 1);
        layout.Children.Add(columns);

        var footer = new Grid { Margin = new Thickness(22, 14, 22, 18) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        feedback.TextWrapping = TextWrapping.Wrap;
        feedback.VerticalAlignment = VerticalAlignment.Center;
        feedback.Margin = new Thickness(0, 0, 16, 0);
        Grid.SetColumn(feedback, 0);
        footer.Children.Add(feedback);
        cancel.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetColumn(cancel, 1);
        footer.Children.Add(cancel);
        save.MinWidth = 180;
        save.Height = 40;
        Grid.SetColumn(save, 2);
        footer.Children.Add(save);
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        return layout;
    }

    private static async Task<FrameworkElement> InventoryAsync()
    {
        // Do not execute SQLite queries on the WPF dispatcher during page navigation.
        var workspaceData = await DesktopUiWork.RunAsync(async () =>
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
            return (factory, rows);
        });
        var (factory, rows) = workspaceData;
        var modules = await DesktopUiWork.RunAsync(() =>
            new LocalRestaurantSettingsService(factory).GetModulesAsync());

        var grid = GridFor(rows);
        grid.Columns.Add(Column("SKU", nameof(InventoryRow.Sku), 120));
        grid.Columns.Add(Column("Ingredient", nameof(InventoryRow.Name), 220));
        grid.Columns.Add(Column("On hand", nameof(InventoryRow.Quantity), 110));
        grid.Columns.Add(Column("Base unit", nameof(InventoryRow.BaseUnit), 100));
        grid.Columns.Add(Column("Reorder", nameof(InventoryRow.ReorderLevel), 100));
        grid.Columns.Add(Column("Avg cost AFN", nameof(InventoryRow.AverageCost), 120));
        grid.Columns.Add(Column("Stock value AFN", nameof(InventoryRow.StockValue), 135));
        var panel = Stack();
        panel.Margin = new Thickness(4);
        panel.Children.Add(Card("Inventory overview",
            "Search ingredients, review local quantities and costs, and record audited changes. All quantities are stored in each ingredient's base unit."));
        panel.Children.Add(Card("Ingredient costing mode",
            modules.AutomaticRecipeConsumptionEnabled
                ? "Recipe mode: kitchen production automatically consumes recipe ingredients. Physical counts cannot be booked as additional food cost."
                : "Manual mode: KOT does not deduct ingredients. Count physical stock after service to record actual usage and food cost. Purchases remain stock assets until consumed."));

        var lowStockCount = rows.Count(x => x.ReorderLevel > 0 && x.Quantity <= x.ReorderLevel);
        var summaryCards = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        summaryCards.Children.Add(Card("ACTIVE INGREDIENTS", rows.Count.ToString(), 205));
        summaryCards.Children.Add(Card("LOW STOCK ALERTS", lowStockCount.ToString(), 205));
        summaryCards.Children.Add(Card("OUT OF STOCK", rows.Count(x => x.Quantity <= 0).ToString(), 205));
        summaryCards.Children.Add(Card("STOCK UNITS", rows.Select(x => x.BaseUnit).Distinct().Count().ToString(), 205));
        panel.Children.Add(summaryCards);

        if (lowStockCount > 0)
        {
            var warning = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 10),
                Child = new TextBlock
                {
                    Text = $"● {lowStockCount} ingredient(s) need replenishment. Use the Low stock filter to review them.",
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
            };
            warning.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
            warning.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            panel.Children.Add(warning);
        }

        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var create = new Button { Content = "+ Add Ingredient", MinWidth = 160, Height = 38, Margin = new Thickness(0, 8, 0, 12) };
        create.Click += async (_, _) =>
        {
            var dialog = new Window
            {
                Title = "Add Ingredient",
                Width = 550,
                Height = 610,
                MinWidth = 460,
                MinHeight = 510,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
            };
            var owner = System.Windows.Application.Current?.MainWindow;
            if (owner is not null) dialog.Owner = owner;
            var content = Stack();
            content.Margin = new Thickness(20);
            var name = new TextBox { Name = "IngredientNameInput", Height = 36 };
            var sku = new TextBox { Name = "IngredientSkuInput", Height = 36 };
            var units = new[] { "kg", "g", "l", "ml", "pcs" };
            var unit = new ComboBox { ItemsSource = units, SelectedIndex = 0, Height = 36 };
            var purchaseUnit = new ComboBox { ItemsSource = units, SelectedIndex = 0, Height = 36 };
            var factor = new TextBox { Text = "1", Height = 36 };
            unit.SelectionChanged += (_, _) =>
            {
                if (purchaseUnit.SelectedItem?.ToString() == unit.SelectedItem?.ToString())
                    factor.Text = "1";
            };
            var reorder = new TextBox { Text = "0", Height = 36 };
            foreach (var entry in new (string Label, FrameworkElement Input)[]
            {
                ("Ingredient name", name), ("SKU", sku), ("Base unit (stock)", unit),
                ("Purchase unit", purchaseUnit),
                ("Base units per one purchase unit", factor), ("Reorder level (base unit)", reorder),
            })
            {
                content.Children.Add(new TextBlock { Text = entry.Label, Margin = new Thickness(0, 8, 0, 3) });
                content.Children.Add(entry.Input);
            }
            var feedback = new TextBlock
            {
                Name = "IngredientSaveFeedback",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 10),
            };
            content.Children.Add(feedback);
            var save = new Button { Content = "Save Ingredient", Height = 42 };
            save.Click += async (_, _) =>
            {
                // Capture WPF control values on the UI thread. SQLite's
                // "async" operations can still block on file locks; don't
                // run initialization or SQL on the WPF dispatcher.
                if (!save.IsEnabled) return;
                if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(sku.Text))
                {
                    feedback.Text = "Enter ingredient name and SKU.";
                    return;
                }
                if (!decimal.TryParse(reorder.Text, out var level) || level < 0)
                {
                    feedback.Text = "Enter a non-negative reorder level.";
                    return;
                }
                if (!decimal.TryParse(factor.Text, out var conversion) || conversion <= 0)
                {
                    feedback.Text = "Enter a positive purchase-to-base unit conversion factor.";
                    return;
                }
                var baseUnit = unit.SelectedItem?.ToString() ?? "kg";
                var purchase = purchaseUnit.SelectedItem?.ToString() ?? "kg";
                if (baseUnit == purchase && conversion != 1m)
                {
                    feedback.Text = "The conversion factor must be 1 when purchase and stock units match.";
                    return;
                }

                var ingredientSku = sku.Text.Trim();
                var ingredientName = name.Text.Trim();
                save.IsEnabled = false;
                save.Content = "Saving ingredient…";
                feedback.Text = "Saving ingredient locally. Please wait…";
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try
                {
                    await Task.Run(async () =>
                    {
                        var actor = await new DesktopRestaurantWorkflowService()
                            .CurrentPrincipalAsync(timeout.Token).ConfigureAwait(false);
                        await new LocalInventoryService(factory).CreateItemAsync(
                            ingredientSku, ingredientName, baseUnit, purchase,
                            conversion, level, actor, timeout.Token)
                            .ConfigureAwait(false);
                    }, timeout.Token).WaitAsync(timeout.Token);
                    dialog.DialogResult = true;
                }
                catch (LocalSyncConflictException ex) when (ex.Code == "inventory_item_conflict")
                {
                    // Duplicates are recoverable validation errors, not a
                    // hung save. Let the operator correct the SKU in-place.
                    feedback.Text = "SKU '" + ingredientSku.ToUpperInvariant() +
                        "' already exists. Enter a different SKU and try again.";
                    save.Content = "Save Ingredient";
                    save.IsEnabled = true;
                    sku.Focus();
                    sku.SelectAll();
                }
                catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
                {
                    App.LogRecoverableException("ingredient-create-timeout", ex);
                    feedback.Text = "The save exceeded 20 seconds. Close this form and refresh " +
                        "Inventory to check whether the item was created before trying again.";
                    // SQLite may have committed before the timeout signal.
                    // Never let a blind retry create a second item/outbox row.
                    save.Content = "Check inventory before retrying";
                }
                catch (Exception ex)
                {
                    App.LogRecoverableException("ingredient-create-save", ex);
                    feedback.Text = "Ingredient could not be saved: " + ex.Message;
                    save.Content = "Save Ingredient";
                    save.IsEnabled = true;
                }
            };
            content.Children.Add(save);
            dialog.Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            if (DesktopGlassDialog.ShowDialog(dialog) == true)
            {
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, "Ingredient created successfully.");
                if (System.Windows.Application.Current?.MainWindow?.DataContext is MainWindowViewModel vm)
                    await vm.RefreshCommand.ExecuteAsync(null);
            }
        };
        var editInventory = new Button { Content = "Edit Ingredients & Units (Online)", Height = 38, Margin = new Thickness(0, 6, 0, 8) };
        editInventory.Click += async (_, _) => await OpenCloudManagementAsync("inventory_items");
        create.HorizontalAlignment = HorizontalAlignment.Left;
        editInventory.HorizontalAlignment = HorizontalAlignment.Left;
        create.MinWidth = 0;
        editInventory.MinWidth = 0;
        create.Margin = new Thickness(0, 0, 10, 8);
        editInventory.Margin = new Thickness(0, 0, 10, 8);
        actions.Children.Add(create);
        actions.Children.Add(editInventory);

        var adjustStock = new Button { Content = "+ Adjust Stock Quantity", Height = 38, MinWidth = 190, Margin = new Thickness(0, 6, 0, 12) };
        adjustStock.Click += async (_, _) =>
        {
            try
            {
                await using var lookup = factory.Create();
                var branchRows = await lookup.Branches.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
                var ingredientRows = await lookup.InventoryItems.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
                var dialog = new Window { Title = "Audited stock adjustment", Width = 480, Height = 400,
                    MinHeight = 355, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = System.Windows.Application.Current?.MainWindow };
                var form = Stack();
                form.Margin = new Thickness(18);
                var branch = new ComboBox { ItemsSource = branchRows, DisplayMemberPath = "Name", SelectedIndex = branchRows.Count > 0 ? 0 : -1, Height = 34 };
                var item = new ComboBox { ItemsSource = ingredientRows, DisplayMemberPath = "Name", SelectedIndex = ingredientRows.Count > 0 ? 0 : -1, Height = 34 };
                var delta = new TextBox { Text = "0", Height = 34 };
                var reason = new TextBox { Height = 34 };
                foreach (var field in new (string Label, FrameworkElement Input)[]
                {
                    ("Branch", branch), ("Ingredient", item),
                    ("Quantity difference in stock units (+ received / - lost)", delta),
                    ("Reason (required for audit)", reason),
                })
                {
                    form.Children.Add(new TextBlock { Text = field.Label, Margin = new Thickness(0, 8, 0, 4), TextWrapping = TextWrapping.Wrap });
                    form.Children.Add(field.Input);
                }
                form.Children.Add(new TextBlock
                {
                    Text = "Quantity corrections retain existing average valuation. Use Purchase & Receive to establish the original unit cost.",
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10),
                });
                var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
                var save = new Button { Content = "Post audited adjustment", Height = 38 };
                var adjustmentId = "DESKTOP-STOCK-" + Guid.CreateVersion7().ToString("N");
                save.Click += async (_, _) =>
                {
                    try
                    {
                        if (branch.SelectedItem is not LocalBranch selectedBranch ||
                            item.SelectedItem is not LocalInventoryItem selectedItem)
                            throw new InvalidOperationException("Select a branch and ingredient.");
                        if (!decimal.TryParse(delta.Text, out var amount) || amount == 0)
                            throw new InvalidOperationException("Enter a nonzero stock change.");
                        if (string.IsNullOrWhiteSpace(reason.Text))
                            throw new InvalidOperationException("Enter the reason for this correction.");
                        if (amount < 0 && !DesktopGlassDialog.Confirm(
                            $"Remove {Math.Abs(amount)} {selectedItem.BaseUnit} of {selectedItem.Name} from stock?",
                            "Confirm negative stock adjustment", dialog))
                            return;
                        if (!save.IsEnabled) return;
                        var branchId = selectedBranch.Id;
                        var ingredientId = selectedItem.Id;
                        var explanation = reason.Text.Trim();
                        save.IsEnabled = false;
                        save.Content = "Posting adjustment…";
                        error.Text = "Updating the local stock ledger. Please wait…";
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                        try
                        {
                            await Task.Run(async () =>
                            {
                                var actor = await new DesktopRestaurantWorkflowService()
                                    .CurrentPrincipalAsync(timeout.Token).ConfigureAwait(false);
                                await new LocalInventoryService(factory).AdjustAsync(
                                    branchId, ingredientId, amount, adjustmentId,
                                    explanation, actor, timeout.Token).ConfigureAwait(false);
                            }, timeout.Token).WaitAsync(timeout.Token);
                            dialog.DialogResult = true;
                        }
                        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
                        {
                            App.LogRecoverableException("stock-adjust-timeout", ex);
                            error.Text = "Stock update timed out. Close this form and check the audit/stock ledger " +
                                "before another adjustment. Do not post twice.";
                            save.Content = "Verify stock before retrying";
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LogRecoverableException("stock-adjust-save", ex);
                        error.Text = ex.Message;
                        save.Content = "Post audited adjustment";
                        save.IsEnabled = true;
                    }
                };
                form.Children.Add(save);
                form.Children.Add(error);
                dialog.Content = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                if (DesktopGlassDialog.ShowDialog(dialog) == true)
                    DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, "Audited stock adjustment posted. Refresh Inventory.");
            }
            catch (Exception ex) { DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message); }
        };
        adjustStock.HorizontalAlignment = HorizontalAlignment.Left;
        adjustStock.MinWidth = 0;
        adjustStock.Margin = new Thickness(0, 0, 10, 8);
        actions.Children.Add(adjustStock);
        var countRemaining = new Button
        {
            Name = "InventoryCountRemainingStock",
            Content = "Count Remaining Stock · Record Consumption",
            Height = 38, MinWidth = 270,
            Margin = new Thickness(0, 0, 10, 8),
            Visibility = modules.AutomaticRecipeConsumptionEnabled
                ? Visibility.Collapsed : Visibility.Visible,
            ToolTip = "Manual mode: enter the actual remaining quantity. The difference becomes audited food cost.",
        };
        countRemaining.Click += async (_, _) =>
        {
            countRemaining.IsEnabled = false;
            try
            {
                var info = await DesktopUiWork.RunAsync(async () =>
                {
                    await using var db = factory.Create();
                    var branches = await db.Branches.AsNoTracking()
                        .Where(x => x.IsActive).OrderBy(x => x.Name).ToArrayAsync();
                    var ingredients = await db.InventoryItems.AsNoTracking()
                        .Where(x => x.IsActive).OrderBy(x => x.Name).ToArrayAsync();
                    var balances = await db.InventoryBalances.AsNoTracking()
                        .Select(x => new { x.BranchId, x.InventoryItemId, x.Quantity }).ToArrayAsync();
                    var valuations = await db.InventoryValuations.AsNoTracking()
                        .Select(x => new { x.BranchId, x.InventoryItemId, x.AverageUnitCost }).ToArrayAsync();
                    return (Branches: branches, Ingredients: ingredients,
                        Balances: balances, Valuations: valuations);
                });
                var dialog = new Window
                {
                    Title = "Count Remaining Stock · Manual Consumption",
                    Name = "ManualConsumptionDialog",
                    Width = 580, Height = 610, MinWidth = 450, MinHeight = 480,
                    Owner = System.Windows.Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.CanResize,
                };
                var content = Stack();
                content.Margin = new Thickness(22);
                content.Children.Add(Card("Physical ingredient count",
                    "Enter what remains in storage. Only the difference from current book stock is recognized as ingredient consumption, at its existing average cost."));
                var branch = new ComboBox
                {
                    Name = "ManualCountBranch", ItemsSource = info.Branches,
                    DisplayMemberPath = "Name", SelectedIndex = info.Branches.Length > 0 ? 0 : -1,
                    Height = 38,
                };
                var ingredient = new ComboBox
                {
                    Name = "ManualCountIngredient", ItemsSource = info.Ingredients,
                    DisplayMemberPath = "Name", SelectedIndex = info.Ingredients.Length > 0 ? 0 : -1,
                    Height = 38,
                };
                var count = new TextBox { Name = "ManualCountRemaining", Text = "0", Height = 38 };
                var reason = new TextBox
                {
                    Text = "End-of-service physical ingredient count", Height = 38,
                };
                var preview = new TextBlock
                {
                    Name = "ManualCountPreview", TextWrapping = TextWrapping.Wrap,
                    FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 12),
                };
                void RefreshCountPreview()
                {
                    if (branch.SelectedItem is not LocalBranch selectedBranch ||
                        ingredient.SelectedItem is not LocalInventoryItem selectedItem) return;
                    var onHand = info.Balances.FirstOrDefault(x => x.BranchId == selectedBranch.Id &&
                        x.InventoryItemId == selectedItem.Id)?.Quantity ?? 0m;
                    var unitCost = info.Valuations.FirstOrDefault(x => x.BranchId == selectedBranch.Id &&
                        x.InventoryItemId == selectedItem.Id)?.AverageUnitCost ?? 0m;
                    if (!decimal.TryParse(count.Text, out var remaining) || remaining < 0 || remaining > onHand)
                    {
                        preview.Text = $"Book stock: {onHand:N3} {selectedItem.BaseUnit}. Enter a physical balance from 0 through {onHand:N3}.";
                        return;
                    }
                    var consumed = onHand - remaining;
                    preview.Text = $"Book stock: {onHand:N3} {selectedItem.BaseUnit}  →  " +
                        $"Physical: {remaining:N3} {selectedItem.BaseUnit}  →  " +
                        $"Consumed: {consumed:N3} {selectedItem.BaseUnit}  ·  " +
                        $"Estimated food cost: AFN {consumed * unitCost:N2}";
                }
                foreach (var entry in new (string Text, FrameworkElement Field)[]
                {
                    ("Branch", branch), ("Ingredient", ingredient),
                    ("Physical quantity remaining (in the ingredient base unit)", count),
                    ("Reason / count period", reason),
                })
                {
                    content.Children.Add(Label(entry.Text));
                    content.Children.Add(entry.Field);
                }
                content.Children.Add(preview);
                branch.SelectionChanged += (_, _) => RefreshCountPreview();
                ingredient.SelectionChanged += (_, _) => RefreshCountPreview();
                count.TextChanged += (_, _) => RefreshCountPreview();
                RefreshCountPreview();
                var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
                var save = new Button { Name = "ManualCountSave", Content = "Confirm consumption", Height = 44 };
                var countId = Guid.CreateVersion7().ToString("N");
                save.Click += async (_, _) =>
                {
                    if (!save.IsEnabled) return;
                    if (branch.SelectedItem is not LocalBranch selectedBranch ||
                        ingredient.SelectedItem is not LocalInventoryItem selectedItem ||
                        !decimal.TryParse(count.Text, out var remaining) ||
                        remaining < 0 || string.IsNullOrWhiteSpace(reason.Text))
                    {
                        error.Text = "Select branch/ingredient, a non-negative quantity and a reason.";
                        return;
                    }
                    var book = info.Balances.FirstOrDefault(x => x.BranchId == selectedBranch.Id &&
                        x.InventoryItemId == selectedItem.Id)?.Quantity ?? 0m;
                    if (remaining >= book)
                    {
                        error.Text = "Enter a quantity below the book balance. To increase stock, use Adjust Stock.";
                        return;
                    }
                    if (!DesktopGlassDialog.Confirm(
                        $"Record {book - remaining:N3} {selectedItem.BaseUnit} of {selectedItem.Name} as consumed? This posts food cost and cannot be undone by editing this count.",
                        "Confirm audited physical count", dialog)) return;
                    var branchId = selectedBranch.Id;
                    var itemId = selectedItem.Id;
                    var explanation = reason.Text.Trim();
                    save.IsEnabled = false;
                    save.Content = "Recording consumption…";
                    error.Text = "Updating local inventory and food-cost ledger…";
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                    try
                    {
                        await DesktopUiWork.RunBoundedAsync(async token =>
                        {
                            var actor = await new DesktopRestaurantWorkflowService()
                                .CurrentPrincipalAsync(token).ConfigureAwait(false);
                            await new LocalInventoryService(factory).RecordManualConsumptionAsync(
                                branchId, itemId, remaining, countId, explanation, actor, token)
                                .ConfigureAwait(false);
                        }, TimeSpan.FromSeconds(25));
                        dialog.DialogResult = true;
                    }
                    catch (OperationCanceledException ex)
                    {
                        App.LogRecoverableException("manual-inventory-count-timeout", ex);
                        error.Text = "Result uncertain: close this form and review stock movements before attempting another count. Do not double-record consumption.";
                        save.Content = "Verify stock before retry";
                    }
                    catch (Exception ex)
                    {
                        App.LogRecoverableException("manual-inventory-count", ex);
                        error.Text = ex.Message;
                        save.Content = "Confirm consumption";
                        save.IsEnabled = true;
                    }
                };
                content.Children.Add(save);
                content.Children.Add(error);
                dialog.Content = new ScrollViewer
                {
                    Content = content,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                };
                if (DesktopGlassDialog.ShowDialog(dialog) == true)
                {
                    DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success,
                        "Actual ingredient consumption recorded. Food cost and stock ledger updated.");
                    if (System.Windows.Application.Current?.MainWindow?.DataContext is MainWindowViewModel vm)
                        await vm.RefreshCommand.ExecuteAsync(null);
                }
            }
            catch (Exception ex)
            {
                App.LogRecoverableException("manual-inventory-dialog", ex);
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message);
            }
            finally { countRemaining.IsEnabled = true; }
        };
        actions.Children.Add(countRemaining);
        panel.Children.Add(actions);

        var filters = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var searchBox = new TextBox
        {
            Name = "InventorySearchBox",
            Width = 360,
            Height = 38,
            Margin = new Thickness(0, 0, 12, 8),
            ToolTip = "Filter by ingredient name, SKU, stock unit or purchase unit",
        };
        searchBox.SetResourceReference(Control.BackgroundProperty, "InputBackgroundBrush");
        var lowStockOnly = new CheckBox
        {
            Name = "InventoryLowStockOnlyToggle",
            Content = "Low stock only",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 8),
            FontWeight = FontWeights.SemiBold,
        };
        void ApplyInventoryFilter()
        {
            var search = searchBox.Text.Trim();
            grid.ItemsSource = rows.Where(row =>
                (!lowStockOnly.IsChecked.GetValueOrDefault() ||
                 row.ReorderLevel > 0 && row.Quantity <= row.ReorderLevel) &&
                (row.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                 row.Sku.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                 row.BaseUnit.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                 row.PurchaseUnit.Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        searchBox.TextChanged += (_, _) => ApplyInventoryFilter();
        lowStockOnly.Checked += (_, _) => ApplyInventoryFilter();
        lowStockOnly.Unchecked += (_, _) => ApplyInventoryFilter();
        filters.Children.Add(searchBox);
        filters.Children.Add(lowStockOnly);
        panel.Children.Add(new TextBlock
        {
            Text = "Search inventory", FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 7),
        });
        panel.Children.Add(filters);

        grid.Name = "InventoryStockGrid";
        grid.MinHeight = 240;
        grid.MaxHeight = 520;
        var table = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Child = grid,
        };
        table.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
        table.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        panel.Children.Add(table);
        return Scroll(WorkspaceFrostedSurface.Wrap(panel));
    }

    private static async Task<FrameworkElement> PurchasesAsync()
    {
        // Do not execute SQLite queries on the WPF dispatcher during page navigation.
        var workspaceData = await DesktopUiWork.RunAsync(async () =>
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

            return (factory, orders, suppliers, receipts);
        });
        var (factory, orders, suppliers, receipts) = workspaceData;

        var panel = Stack();
        panel.Children.Add(Card("Restaurant procurement", "Supplier → purchase order → goods receipt → stock movement. Receiving stock updates the local restaurant inventory."));
        var supplierGrid = GridFor(suppliers); supplierGrid.MinHeight = 180;
        supplierGrid.Columns.Add(Column("Code", nameof(SupplierRow.Code), 110)); supplierGrid.Columns.Add(Column("Supplier", nameof(SupplierRow.Name), 220));
        supplierGrid.Columns.Add(Column("Phone", nameof(SupplierRow.Phone), 150)); supplierGrid.Columns.Add(Column("Email", nameof(SupplierRow.Email), 220));
        panel.Children.Add(supplierGrid);
        var addSupplier = new Button { Content = "+ Add Supplier", Height = 38, MinWidth = 150, Margin = new Thickness(0, 10, 0, 6) };
        addSupplier.Click += (_, _) =>
        {
            var dialog = new Window { Title = "Add Supplier", Width = 445, Height = 410, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var owner = System.Windows.Application.Current?.MainWindow;
            if (owner is not null) dialog.Owner = owner;
            var form = Stack();
            form.Margin = new Thickness(18);
            var code = new TextBox { Height = 34 };
            var name = new TextBox { Height = 34 };
            var phone = new TextBox { Height = 34 };
            var email = new TextBox { Height = 34 };
            foreach (var field in new (string Label, FrameworkElement Input)[]
            {
                ("Supplier code", code), ("Supplier name", name), ("Phone (optional)", phone), ("Email (optional)", email)
            })
            {
                form.Children.Add(new TextBlock { Text = field.Label, Margin = new Thickness(0, 8, 0, 3) });
                form.Children.Add(field.Input);
            }
            var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 6) };
            form.Children.Add(feedback);
            var save = new Button { Content = "Save Supplier", Height = 38 };
            save.Click += async (_, _) =>
            {
                if (!save.IsEnabled) return;
                if (string.IsNullOrWhiteSpace(code.Text) || string.IsNullOrWhiteSpace(name.Text))
                {
                    feedback.Text = "Supplier code and name are required.";
                    return;
                }
                var supplierCode = code.Text.Trim();
                var supplierName = name.Text.Trim();
                var supplierPhone = phone.Text.Trim();
                var supplierEmail = email.Text.Trim();
                save.IsEnabled = false;
                save.Content = "Saving supplier…";
                feedback.Text = "Saving supplier locally. Please wait…";
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try
                {
                    await Task.Run(async () =>
                    {
                        var actor = await new DesktopRestaurantWorkflowService()
                            .CurrentPrincipalAsync(timeout.Token).ConfigureAwait(false);
                        await new LocalInventoryService(factory).CreateSupplierAsync(
                            supplierCode, supplierName, supplierPhone, supplierEmail,
                            null, actor, timeout.Token).ConfigureAwait(false);
                    }, timeout.Token).WaitAsync(timeout.Token);
                    dialog.DialogResult = true;
                }
                catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
                {
                    App.LogRecoverableException("supplier-save-timeout", ex);
                    feedback.Text = "Save timed out. Close and refresh suppliers to check if it succeeded " +
                        "before submitting the same supplier again.";
                    save.Content = "Verify supplier before retrying";
                }
                catch (Exception ex)
                {
                    App.LogRecoverableException("supplier-save", ex);
                    feedback.Text = ex.Message;
                    save.Content = "Save Supplier";
                    save.IsEnabled = true;
                }
            };
            form.Children.Add(save);
            dialog.Content = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            if (DesktopGlassDialog.ShowDialog(dialog) == true)
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, "Supplier saved. Refresh Purchases to see it.");
        };
        panel.Children.Add(addSupplier);

        var purchaseButton = new Button { Content = "+ New Purchase & Receive", Height = 38, MinWidth = 200, Margin = new Thickness(0, 6, 0, 8) };
        purchaseButton.Click += async (_, _) =>
        {
            var dialog = new Window { Title = "Purchase & Receive Inventory", Width = 500, Height = 550, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var owner = System.Windows.Application.Current?.MainWindow;
            if (owner is not null) dialog.Owner = owner;
            await using var lookupDb = factory.Create();
            var availableSuppliers = await lookupDb.Suppliers.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
            var availableItems = await lookupDb.InventoryItems.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
            var availableBranches = await lookupDb.Branches.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
            var form = Stack();
            form.Margin = new Thickness(18);
            var supplier = new ComboBox { ItemsSource = availableSuppliers, DisplayMemberPath = "Name", Height = 34 };
            var branch = new ComboBox { ItemsSource = availableBranches, DisplayMemberPath = "Name", Height = 34 };
            var item = new ComboBox { ItemsSource = availableItems, DisplayMemberPath = "Name", Height = 34 };
            var quantity = new TextBox { Text = "1", Height = 34 };
            var cost = new TextBox { Text = "0", Height = 34 };
            foreach (var entry in new (string Label, FrameworkElement Input)[]
            {
                ("Branch", branch), ("Supplier", supplier), ("Ingredient", item),
                ("Purchase quantity", quantity), ("Unit cost AFN", cost)
            })
            {
                form.Children.Add(new TextBlock { Text = entry.Label, Margin = new Thickness(0, 8, 0, 3) });
                form.Children.Add(entry.Input);
            }

            var purchaseLines = new List<(LocalInventoryItem Item, decimal Quantity, decimal UnitCost)>();
            var linesSummary = new TextBlock { Text = "Add at least one ingredient.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
            var addLine = new Button { Content = "+ Add Purchase Line", Height = 36 };
            addLine.Click += (_, _) =>
            {
                if (item.SelectedItem is not LocalInventoryItem selectedItem ||
                    !decimal.TryParse(quantity.Text, out var qty) || qty <= 0 ||
                    !decimal.TryParse(cost.Text, out var unitCost) || unitCost < 0)
                {
                    linesSummary.Text = "Select an ingredient and enter a positive quantity and valid unit cost.";
                    return;
                }
                if (purchaseLines.Any(x => x.Item.Id == selectedItem.Id))
                {
                    linesSummary.Text = "This ingredient is already in the purchase. Add each ingredient once.";
                    return;
                }
                purchaseLines.Add((selectedItem, qty, unitCost));
                var total = purchaseLines.Sum(x => x.Quantity * x.UnitCost);
                linesSummary.Text = string.Join("\n", purchaseLines.Select(x =>
                    x.Item.Name + ": " + x.Quantity + " " + (x.Item.PurchaseUnit ?? x.Item.BaseUnit) +
                    " × " + x.UnitCost.ToString("0.00") + " AFN")) +
                    "\nTotal: " + total.ToString("0.00") + " AFN";
            };
            form.Children.Add(addLine);
            form.Children.Add(linesSummary);
            var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 8) };
            form.Children.Add(feedback);
            var save = new Button { Content = "Create PO and Receive Stock", Height = 40 };
            string? pendingPurchaseId = null;
            LocalReceivePurchaseOrderLineRequest[]? pendingReceiptLines = null;
            save.Click += async (_, _) =>
            {
                if (!save.IsEnabled) return;
                try
                {
                    if (branch.SelectedItem is not LocalBranch selectedBranch ||
                        supplier.SelectedItem is not LocalSupplier selectedSupplier || purchaseLines.Count == 0)
                        throw new InvalidOperationException("Select branch, supplier and at least one purchase line.");

                    var branchId = selectedBranch.Id;
                    var supplierId = selectedSupplier.Id;
                    var frozenLines = purchaseLines
                        .Select(x => new LocalPurchaseOrderLineRequest(x.Item.Id, x.Quantity, x.UnitCost))
                        .ToArray();
                    var requested = purchaseLines.Select(x => (x.Item.Id, x.Quantity)).ToArray();
                    save.IsEnabled = false;
                    save.Content = pendingPurchaseId is null ? "Creating purchase…" : "Receiving stock…";
                    feedback.Text = "Recording purchase and receipt in the local ledger…";
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));

                    if (pendingPurchaseId is null)
                    {
                        // Two-step transaction: never generate another PO when
                        // receiving an already-created purchase needs a retry.
                        var result = await Task.Run(async () =>
                        {
                            var actor = await new DesktopRestaurantWorkflowService()
                                .CurrentPrincipalAsync(timeout.Token).ConfigureAwait(false);
                            var created = await new LocalInventoryService(factory).CreatePurchaseOrderAsync(
                                branchId, supplierId, frozenLines,
                                "Desktop one-step purchase", actor, timeout.Token).ConfigureAwait(false);
                            var snapshot = System.Text.Json.JsonSerializer.SerializeToElement(created);
                            var id = snapshot.GetProperty("id").GetString()
                                ?? throw new InvalidOperationException("Purchase order ID was not returned.");
                            var returned = snapshot.GetProperty("lines").EnumerateArray().ToArray();
                            var receipts = requested.Select(line =>
                            {
                                var savedLine = returned.Single(x =>
                                    x.GetProperty("inventory_item_id").GetString() == line.Id);
                                var savedId = savedLine.GetProperty("id").GetString()
                                    ?? throw new InvalidOperationException("Purchase order line ID was not returned.");
                                return new LocalReceivePurchaseOrderLineRequest(savedId, line.Quantity);
                            }).ToArray();
                            return (Id: id, Lines: receipts);
                        }, timeout.Token).WaitAsync(timeout.Token);
                        pendingPurchaseId = result.Id;
                        pendingReceiptLines = result.Lines;
                        branch.IsEnabled = false;
                        supplier.IsEnabled = false;
                        item.IsEnabled = false;
                        quantity.IsEnabled = false;
                        cost.IsEnabled = false;
                        addLine.IsEnabled = false;
                    }

                    var idToReceive = pendingPurchaseId!;
                    var linesToReceive = pendingReceiptLines!;
                    save.Content = "Receiving stock…";
                    await Task.Run(async () =>
                    {
                        var actor = await new DesktopRestaurantWorkflowService()
                            .CurrentPrincipalAsync(timeout.Token).ConfigureAwait(false);
                        await new LocalInventoryService(factory).ReceivePurchaseOrderAsync(
                            idToReceive, linesToReceive, "desktop-receipt-" + idToReceive,
                            "Received through desktop purchase form", actor, timeout.Token)
                            .ConfigureAwait(false);
                    }, timeout.Token).WaitAsync(timeout.Token);
                    dialog.DialogResult = true;
                }
                catch (OperationCanceledException ex)
                {
                    App.LogRecoverableException("purchase-receive-timeout", ex);
                    feedback.Text = "Purchase/receipt took too long. Close this form and inspect the purchase " +
                        "and stock ledgers before retrying. An operation may already have committed.";
                    save.Content = "Verify purchase and receipt before retrying";
                    // Do not enable an ambiguous financial retry.
                }
                catch (Exception ex)
                {
                    App.LogRecoverableException("purchase-receive-save", ex);
                    feedback.Text = (pendingPurchaseId is null ? "" :
                        "Purchase order already saved. Retry receiving; do not create a duplicate. ") + ex.Message;
                    save.Content = pendingPurchaseId is null ? "Create PO and Receive Stock" : "Retry receiving stock";
                    save.IsEnabled = true;
                }
            };
            form.Children.Add(save);
            dialog.Content = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            if (DesktopGlassDialog.ShowDialog(dialog) == true)
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, "Purchase received and inventory updated. Refresh Purchases to see it.");
        };
        panel.Children.Add(purchaseButton);
        var receivePending = new Button { Content = "Receive Outstanding PO", Height = 36, MinWidth = 205, Margin = new Thickness(0, 2, 0, 8) };
        receivePending.Click += async (_, _) =>
        {
            await using var lookup = factory.Create();
            var openOrders = await lookup.PurchaseOrders.AsNoTracking()
                .Where(x => x.Status == "ordered" || x.Status == "partially_received")
                .OrderByDescending(x => x.OrderedAt).ToListAsync();
            if (openOrders.Count == 0)
            {
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Info, "No outstanding purchase orders.");
                return;
            }

            var dialog = new Window { Title = "Receive Outstanding Purchase", Width = 480, Height = 275, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var owner = System.Windows.Application.Current?.MainWindow;
            if (owner is not null) dialog.Owner = owner;
            var form = Stack();
            form.Margin = new Thickness(18);
            form.Children.Add(new TextBlock { Text = "Purchase order", Margin = new Thickness(0, 5, 0, 5) });
            var selected = new ComboBox { ItemsSource = openOrders, DisplayMemberPath = "PoNumber", SelectedIndex = 0, Height = 36 };
            form.Children.Add(selected);
            form.Children.Add(new TextBlock { Text = "Receive all outstanding quantities into inventory.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8) });
            var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
            form.Children.Add(feedback);
            var save = new Button { Content = "Receive Remaining Stock", Height = 38 };
            string? pendingId = null;
            LocalReceivePurchaseOrderLineRequest[]? receiptLines = null;
            var receiptKey = "desktop-pending-" + Guid.NewGuid().ToString("N");
            save.Click += async (_, _) =>
            {
                if (!save.IsEnabled) return;
                try
                {
                    if (selected.SelectedItem is not LocalPurchaseOrder po)
                        throw new InvalidOperationException("Select a purchase order.");

                    var orderId = po.Id;
                    save.IsEnabled = false;
                    save.Content = "Receiving outstanding stock…";
                    feedback.Text = "Updating stock balances locally…";
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                    if (pendingId is null)
                    {
                        var calculated = await Task.Run(async () =>
                        {
                            await using var lineDb = factory.Create();
                            var lines = await lineDb.PurchaseOrderLines.AsNoTracking()
                                .Where(x => x.PurchaseOrderId == orderId)
                                .ToListAsync(timeout.Token).ConfigureAwait(false);
                            if (lines.Any(x => x.ConversionFactor <= 0))
                                throw new InvalidOperationException("Invalid purchase unit conversion.");
                            var outstanding = lines.Where(x => x.OrderedBaseQuantity > x.ReceivedBaseQuantity).ToList();
                            if (outstanding.Count == 0)
                                throw new InvalidOperationException("No outstanding quantities on this purchase.");
                            return outstanding.Select(x => new LocalReceivePurchaseOrderLineRequest(
                                x.Id, (x.OrderedBaseQuantity - x.ReceivedBaseQuantity) / x.ConversionFactor)).ToArray();
                        }, timeout.Token).WaitAsync(timeout.Token);
                        receiptLines = calculated;
                        pendingId = orderId;
                        selected.IsEnabled = false;
                    }

                    var idToReceive = pendingId!;
                    var linesToReceive = receiptLines!;
                    await Task.Run(async () =>
                    {
                        var actor = await new DesktopRestaurantWorkflowService()
                            .CurrentPrincipalAsync(timeout.Token).ConfigureAwait(false);
                        await new LocalInventoryService(factory).ReceivePurchaseOrderAsync(
                            idToReceive, linesToReceive, receiptKey,
                            "Outstanding purchase received through desktop", actor, timeout.Token)
                            .ConfigureAwait(false);
                    }, timeout.Token).WaitAsync(timeout.Token);
                    dialog.DialogResult = true;
                }
                catch (OperationCanceledException ex)
                {
                    App.LogRecoverableException("pending-receipt-timeout", ex);
                    feedback.Text = "Receipt timed out. Close and inspect goods receipts and stock before " +
                        "trying again. Do not post a duplicate receipt.";
                    save.Content = "Verify receipt before retrying";
                }
                catch (Exception ex)
                {
                    App.LogRecoverableException("pending-receipt-save", ex);
                    feedback.Text = ex.Message;
                    save.Content = "Receive Remaining Stock";
                    save.IsEnabled = true;
                }
            };
            form.Children.Add(save);
            dialog.Content = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            if (DesktopGlassDialog.ShowDialog(dialog) == true)
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, "Outstanding purchase received. Refresh Purchases and Inventory.");
        };
        panel.Children.Add(receivePending);
        panel.Children.Add(new TextBlock { Text = "Purchase orders", FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0,20,0,10) });
        var grid = GridFor(orders); grid.MinHeight = 230;
        grid.Columns.Add(Column("PO", nameof(PurchaseRow.Number), 190)); grid.Columns.Add(Column("Supplier", nameof(PurchaseRow.Supplier), 220));
        grid.Columns.Add(Column("Status", nameof(PurchaseRow.Status), 120)); grid.Columns.Add(Column("Estimated AFN", nameof(PurchaseRow.Total), 140));
        grid.Columns.Add(Column("Ordered", nameof(PurchaseRow.OrderedAt), 190)); grid.Columns.Add(Column("Completed", nameof(PurchaseRow.CompletedAt), 190));
        panel.Children.Add(grid);
        // Receipt records remain in the database for auditing and stock
        // reconciliation, but are not a second mandatory operator workflow.
        var historyToggle = new Button
        {
            Content = "Show receipt audit history",
            MinWidth = 220,
            Height = 36,
            Margin = new Thickness(0, 12, 0, 8),
        };
        var receiptHistory = new StackPanel { Visibility = Visibility.Collapsed };
        receiptHistory.Children.Add(new TextBlock { Text = "Goods receipts", FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0,20,0,10) });
        var receiptGrid = GridFor(receipts); receiptGrid.MinHeight = 200;
        receiptGrid.Columns.Add(Column("GRN", nameof(ReceiptRow.Number), 210)); receiptGrid.Columns.Add(Column("Status", nameof(ReceiptRow.Status), 120));
        receiptGrid.Columns.Add(Column("Received", nameof(ReceiptRow.ReceivedAt), 200)); receiptGrid.Columns.Add(Column("Purchase order ID", nameof(ReceiptRow.PurchaseOrderId), 280));
        receiptHistory.Children.Add(receiptGrid);
        historyToggle.Click += (_, _) =>
        {
            receiptHistory.Visibility = receiptHistory.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
            historyToggle.Content = receiptHistory.Visibility == Visibility.Visible
                ? "Hide receipt audit history" : "Show receipt audit history";
        };
        panel.Children.Add(historyToggle);
        panel.Children.Add(receiptHistory);
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
        // The local staff grid is a read-only projection; edits go through cloud
        // owner/admin authorization and immediately create genuine login identities.
        var manageUsers = new Button
        {
            Content = "Add Users / Manage Roles",
            MinWidth = 190,
            Height = 38,
            Margin = new Thickness(0, 10, 0, 8),
        };
        manageUsers.Click += async (_, _) =>
        {
            try
            {
                var session = await new BusinessOS.Restaurant.Authentication.WindowsSessionStore().LoadAsync()
                    ?? throw new InvalidOperationException("Sign in before managing restaurant users.");
                var dialog = new DesktopUserManagementWindow(session)
                {
                    Owner = System.Windows.Application.Current?.MainWindow,
                };
                DesktopGlassDialog.ShowDialog(dialog);
                if (dialog.SavedChanges)
                {
                    var refreshed = await RefreshOperationalSnapshotAsync();
                    DesktopNoticeEvents.Publish(refreshed ? DesktopNoticeLevel.Success : DesktopNoticeLevel.Info,
                        refreshed ? "Tenant staff synchronized. Refresh Users to see the updated roster."
                            : "Accounts saved online; local staff refresh will occur when cloud sync is available.");
                }
            }
            catch (Exception ex)
            {
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message);
            }
        };
        panel.Children.Insert(1, manageUsers);

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
        // Do not execute SQLite queries on the WPF dispatcher during page navigation.
        var workspaceData = await DesktopUiWork.RunAsync(async () =>
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

            return (factory, branches, rows);
        });
        var (factory, branches, rows) = workspaceData;

        var panel = Stack();
        panel.Children.Add(Card("Restaurant expenses", "Record local operating expenses in AFN. Every entry is audited and queued for cloud reconciliation without blocking offline restaurant operations."));

        var createExpense = new Button
        {
            Content = "+ Create Expense",
            MinWidth = 170,
            Height = 42,
            Margin = new Thickness(0, 8, 0, 14),
            FontWeight = FontWeights.SemiBold,
        };
        panel.Children.Add(createExpense);

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
        createExpense.Click += async (_, _) =>
        {
            var owner = System.Windows.Application.Current?.MainWindow;
            var dialog = new Window
            {
                Title = "Create Expense",
                Width = 520,
                Height = 490,
                MinWidth = 400,
                WindowStartupLocation = owner is null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromArgb(246, 248, 250, 255)),
                WindowStyle = WindowStyle.ToolWindow,
            };
            if (owner is not null) dialog.Owner = owner;

            var content = new StackPanel { Margin = new Thickness(24) };
            content.Children.Add(new TextBlock
            {
                Text = "New restaurant expense",
                FontSize = 23,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 14),
            });
            var branchBox = new ComboBox
            {
                ItemsSource = branches,
                DisplayMemberPath = nameof(ExpenseBranchChoice.Name),
                SelectedIndex = branches.Count > 0 ? 0 : -1,
                Height = 36,
            };
            var category = new TextBox { Text = "operations", Height = 36 };
            var description = new TextBox { Height = 36 };
            var amount = new TextBox { Height = 36 };
            var method = new ComboBox
            {
                ItemsSource = new[] { "cash", "card", "bank", "mobile_money", "other" },
                SelectedIndex = 0,
                Height = 36,
            };
            foreach (var field in new (string Label, FrameworkElement Input)[]
            {
                ("Branch", branchBox), ("Category", category), ("Description", description),
                ("Amount (AFN)", amount), ("Payment method", method),
            })
            {
                content.Children.Add(new TextBlock
                {
                    Text = field.Label,
                    Margin = new Thickness(0, 7, 0, 3),
                    FontWeight = FontWeights.Medium,
                });
                content.Children.Add(field.Input);
            }
            var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 6) };
            feedback.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
            content.Children.Add(feedback);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "Cancel", MinWidth = 100, Height = 36, Margin = new Thickness(0, 0, 8, 0) };
            var save = new Button { Content = "Save Expense", MinWidth = 135, Height = 36 };
            cancel.Click += (_, _) => dialog.Close();
            save.Click += async (_, _) =>
            {
                try
                {
                    if (branchBox.SelectedItem is not ExpenseBranchChoice branch)
                        throw new InvalidOperationException("Select a branch.");
                    if (string.IsNullOrWhiteSpace(description.Text))
                        throw new InvalidOperationException("Enter an expense description.");
                    if (!decimal.TryParse(amount.Text, out var value) || value <= 0)
                        throw new InvalidOperationException("Enter a positive expense amount.");
                    if (!save.IsEnabled) return;
                    var branchId = branch.Id;
                    var expenseCategory = category.Text;
                    var expenseDescription = description.Text;
                    var paymentMethod = method.SelectedItem?.ToString() ?? "cash";
                    var businessDate = DateOnly.FromDateTime(DateTime.Today);
                    save.IsEnabled = false;
                    save.Content = "Saving expense…";
                    feedback.Text = "Recording the expense locally. Please wait…";
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    try
                    {
                        await Task.Run(async () =>
                            await workflow.RecordExpenseAsync(branchId, expenseCategory, expenseDescription,
                                value, paymentMethod, businessDate, token: timeout.Token)
                                .ConfigureAwait(false), timeout.Token).WaitAsync(timeout.Token);
                        dialog.DialogResult = true;
                    }
                    catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
                    {
                        App.LogRecoverableException("expense-save-timeout", ex);
                        feedback.Text = "Expense save timed out. Close and inspect the expense ledger before " +
                            "trying again, to avoid a duplicate financial entry.";
                        save.Content = "Verify expense before retrying";
                    }
                }
                catch (Exception ex)
                {
                    App.LogRecoverableException("expense-save", ex);
                    feedback.Text = ex.Message;
                    save.Content = "Save Expense";
                    save.IsEnabled = true;
                }
            };
            actions.Children.Add(cancel);
            actions.Children.Add(save);
            content.Children.Add(actions);
            dialog.Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            if (DesktopGlassDialog.ShowDialog(dialog) == true)
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success,
                    "Expense saved. Refresh Expenses to see the latest ledger entry.");
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
        // Build each group exactly once so the original action handlers and
        // device bindings stay intact as operators move between settings tabs.
        var workflowPage = Stack();
        var modulesPage = await RestaurantModulesPanel.CreateAsync();
        var backupPage = Stack();
        var printingPage = Stack();
        var networkPage = Stack();
        var devicesPage = Stack();

        var workflow = new DesktopRestaurantWorkflowService();
        var workflowSettings = await DesktopUiWork.RunAsync(() => workflow.RestaurantSettingsAsync());

        var workflowPanel = new StackPanel();
        workflowPanel.Children.Add(new TextBlock
        {
            Text = "Kitchen workflow",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
        });

        var workflowHelp = new TextBlock
        {
            Text = "Choose the kitchen flow for new KOT rounds. Existing tickets keep their original stages and production history.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12),
        };
        workflowHelp.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        workflowPanel.Children.Add(workflowHelp);

        var readyOnly = new CheckBox
        {
            Name = "SettingsReadyOnlyKot",
            Content = "Ready-only KOT (recommended: tap READY and notify waiter)",
            IsChecked = !workflowSettings.PreparingStageEnabled && !workflowSettings.ExpoEnabled,
            Margin = new Thickness(0, 10, 0, 6),
            FontWeight = FontWeights.SemiBold,
        };
        var workflowModeHint = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(18, 0, 0, 12),
        };
        workflowModeHint.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
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
        var negativeStockPolicy = new ComboBox
        {
            ItemsSource = new[] { "block", "warn", "allow" },
            SelectedItem = workflowSettings.NegativeStockPolicy,
            Width = 160,
            Height = 34,
            Margin = new Thickness(0, 4, 0, 10),
        };

        void UpdateKitchenWorkflowMode()
        {
            var instant = readyOnly.IsChecked == true;
            preparing.Visibility = instant ? Visibility.Collapsed : Visibility.Visible;
            expo.Visibility = instant ? Visibility.Collapsed : Visibility.Visible;
            if (!instant && preparing.IsChecked != true)
                preparing.IsChecked = true;
            expo.IsEnabled = preparing.IsChecked == true;
            if (!expo.IsEnabled) expo.IsChecked = false;
            workflowModeHint.Text = instant
                ? "New tickets: QUEUED → READY → waiter pickup. No Start or Expo steps."
                : "New tickets use their selected stages. Start preparation first, then mark READY " +
                  "(or send to Expo when enabled). Earlier tickets keep their original workflow.";
        }
        readyOnly.Checked += (_, _) => UpdateKitchenWorkflowMode();
        readyOnly.Unchecked += (_, _) => UpdateKitchenWorkflowMode();
        preparing.Checked += (_, _) => UpdateKitchenWorkflowMode();
        preparing.Unchecked += (_, _) => UpdateKitchenWorkflowMode();
        workflowPanel.Children.Add(readyOnly);
        workflowPanel.Children.Add(workflowModeHint);
        workflowPanel.Children.Add(queue);
        workflowPanel.Children.Add(preparing);
        workflowPanel.Children.Add(expo);
        UpdateKitchenWorkflowMode();

        workflowPanel.Children.Add(courses);
        workflowPanel.Children.Add(sound);
        workflowPanel.Children.Add(managerVoid);
        workflowPanel.Children.Add(new TextBlock
        {
            Text = "Negative stock policy",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 0),
        });
        workflowPanel.Children.Add(negativeStockPolicy);

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
                var update = new RestaurantWorkflowSettingsUpdate(
                    queue.IsChecked == true,
                    readyOnly.IsChecked != true && preparing.IsChecked == true,
                    readyOnly.IsChecked != true && expo.IsChecked == true,
                    courses.IsChecked == true,
                    sound.IsChecked == true,
                    warningMinutes,
                    lateMinutes,
                    managerVoid.IsChecked == true,
                    negativeStockPolicy.SelectedItem?.ToString() ?? "block");
                workflowStatus.Text = "Saving restaurant workflow settings…";
                var updated = await DesktopUiWork.RunAsync(() =>
                    workflow.UpdateRestaurantSettingsAsync(update));

                workflowStatus.Text =
                    $"Saved. Queue {(updated.KitchenQueueEnabled ? "ON" : "OFF")} · " +
                    $"KOT {(updated.PreparingStageEnabled ? (updated.ExpoEnabled ? "START → EXPO → READY" : "START → READY") : "READY-only")} · " +
                    $"Negative stock {updated.NegativeStockPolicy.ToUpperInvariant()}.";
            }
            catch (Exception ex)
            {
                App.LogRecoverableException("restaurant-workflow-settings-save", ex);
                // EF Core's outer message conceals the actionable SQLite
                // problem (locked database, missing column, constraint).
                workflowStatus.Text = "Not saved: " + ex.GetBaseException().Message +
                    " · Your previous settings are unchanged. See local diagnostic log.";
            }
            finally
            {
                saveWorkflow.IsEnabled = true;
            }
        };

        workflowPage.Children.Add(SettingsSection(
            "Restaurant workflow settings",
            "These settings drive the Desktop KOT/KDS state machine and are included in LAN bootstrap/settings APIs for cross-client alignment.",
            workflowPanel));

        backupPage.Children.Add(SettingsSection(
            "Backup, restore & local database health",
            "Back up a consistent SQLite snapshot; staged restores apply only on the next cold launch before local services start.",
            await BackupRestorePanelAsync()));

        printingPage.Children.Add(SettingsSection(
            "Printing & Recovery",
            "KOT and receipt queues are independent. Interrupted spool submissions are not replayed automatically because an unconfirmed replay can cause duplicate food production.",
            await PrinterQueueRecoveryPanelAsync(workflow)));

        var activeLease = await new BusinessOS.Restaurant.Licensing.WindowsActivationStore().LoadAsync();
        var standalone = BusinessOS.Restaurant.Licensing.DesktopOperatingMode.IsStandalone(activeLease);
        var connection = await new BusinessOS.Restaurant.Licensing.ConnectionSettingsStore().LoadAsync();
        var modePanel = new StackPanel();
        modePanel.Children.Add(new TextBlock
        {
            Text = standalone ? "Standalone Offline — no web sync" :
                connection?.SyncEnabled == true ? "Cloud Sync — enabled" : "Cloud Sync — locally paused",
            FontSize = 18, FontWeight = FontWeights.Bold,
        });
        modePanel.Children.Add(new TextBlock
        {
            Text = standalone
                ? "Your signed license disables all automatic cloud communication. Orders, KOT, payments and LAN operate in SQLite. Changes stay here and are NOT queued for later cloud upload. A current signed lease is required; refresh it before the paid term expires."
                : "This desktop uses local SQLite and may exchange restaurant records with the authorized tenant. The platform administrator controls Standalone vs Cloud in License & Devices.",
            Margin = new Thickness(0, 8, 0, 12), TextWrapping = TextWrapping.Wrap,
        });
        var refreshMode = new Button { Content = "Refresh signed desktop mode (requires Internet)",
            MinHeight = 38, Margin = new Thickness(0, 0, 0, 8) };
        var modeStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        modePanel.Children.Add(refreshMode);
        if (standalone)
        {
            var importRenewal = new Button
            {
                Content = "Import signed offline renewal (.json)",
                MinHeight = 38, Margin = new Thickness(0, 0, 0, 8),
            };
            importRenewal.Click += async (_, _) =>
            {
                importRenewal.IsEnabled = false;
                try
                {
                    var session = await new BusinessOS.Restaurant.Authentication.WindowsSessionStore().LoadAsync();
                    if (session?.User.Role.Trim().ToLowerInvariant() is not ("owner" or "admin"))
                        throw new UnauthorizedAccessException("Only Owner/Admin may import a license renewal.");
                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Import restaurant offline license renewal",
                        Filter = "Signed Restaurant renewal|*.json",
                        CheckFileExists = true,
                        Multiselect = false,
                    };
                    if (dialog.ShowDialog() == true)
                    {
                        var updated = await new BusinessOS.Restaurant.Licensing.RestaurantLicenseCoordinator()
                            .ImportOfflineRenewalAsync(dialog.FileName);
                        modeStatus.Text = "Signed renewal verified locally; licensed through " +
                            updated.Snapshot.OfflineValidUntil.ToLocalTime().ToString("yyyy-MM-dd") +
                            ". Restart the application to update its status. No web request was made.";
                    }
                }
                catch (Exception error) { modeStatus.Text = error.Message; }
                finally { importRenewal.IsEnabled = true; }
            };
            modePanel.Children.Add(importRenewal);
        }
        modePanel.Children.Add(modeStatus);
        refreshMode.Click += async (_, _) =>
        {
            refreshMode.IsEnabled = false;
            try
            {
                var session = await new BusinessOS.Restaurant.Authentication.WindowsSessionStore().LoadAsync();
                if (session?.User.Role.Trim().ToLowerInvariant() is not ("owner" or "admin"))
                    throw new UnauthorizedAccessException("Only the owner/admin can refresh the desktop mode.");
                var updated = await new BusinessOS.Restaurant.Licensing.RestaurantLicenseCoordinator().RefreshModeAsync();
                modeStatus.Text = "Signed mode: " + updated.Snapshot.DesktopMode +
                    ". Restart Windows Restaurant to apply. If leaving standalone, back up local data and reconcile before enabling cloud sync.";
            }
            catch (Exception error) { modeStatus.Text = error.Message; }
            finally { refreshMode.IsEnabled = true; }
        };
        networkPage.Children.Add(SettingsSection("Licensed desktop operating mode",
            "Configured centrally under Platform → Restaurant → License & Devices. A signed offline renewal file can be imported on an already activated standalone terminal. Changing operating modes still requires a deliberate online refresh.", modePanel));

        networkPage.Children.Add(Cards(
            ("LAN STATUS", diagnostics.NetworkMode),
            ("WAITER DEVICES", diagnostics.TerminalSummary),
            ("MOBILE ALLOWANCE", diagnostics.MobileAllowance),
            ("OFFLINE LEASE", diagnostics.LeaseStatus),
            ("CLOUD QUEUE", $"{diagnostics.PendingCloudMutations} pending · {diagnostics.OpenCloudConflicts} conflicts")));

        networkPage.Children.Add(Card(
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

        devicesPage.Children.Add(SettingsSection(
            "Waiter app connection",
            "Scan this QR from the waiter app to configure both LAN and cloud routes. Automatic mode prefers LAN, falls back to cloud when LAN is unavailable, queues offline changes when neither route is reachable, and returns to LAN automatically.",
            pairingPanel));

        var refresh = new Button { Content = "Refresh status", MinWidth = 130, Height = 38, Margin = new Thickness(0, 0, 10, 10) };
        refresh.SetBinding(Button.CommandProperty, new Binding(nameof(LanDiagnosticsViewModel.RefreshCommand)));
        networkPage.Children.Add(refresh);

        var deviceActions = new WrapPanel { Margin = new Thickness(0, 4, 0, 14) };
        var toggle = new Button { MinWidth = 140, Height = 38, Margin = new Thickness(0, 0, 10, 0) };
        toggle.SetBinding(Button.ContentProperty, new Binding(nameof(LanDiagnosticsViewModel.ToggleTerminalLabel)));
        toggle.SetBinding(Button.CommandProperty, new Binding(nameof(LanDiagnosticsViewModel.ToggleSelectedTerminalCommand)));
        deviceActions.Children.Add(toggle);

        var unpair = new Button { Content = "Unpair device", MinWidth = 130, Height = 38 };
        unpair.SetBinding(Button.CommandProperty, new Binding(nameof(LanDiagnosticsViewModel.UnpairSelectedTerminalCommand)));
        deviceActions.Children.Add(unpair);
        devicesPage.Children.Add(deviceActions);

        devicesPage.Children.Add(new TextBlock
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
        devicesPage.Children.Add(grid);

        devicesPage.Children.Add(Card(
            "Device control",
            "Select a waiter device above to enable/disable or unpair it. Device-management actions require an Owner or Manager session. Disabling or unpairing a terminal does not disable the restaurant desktop or other LAN terminals."));

        var tabs = new TabControl
        {
            Name = "RestaurantSettingsTabs",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            DataContext = diagnostics,
        };
        tabs.Items.Add(SettingsTab("kitchen", "Kitchen & KOT", workflowPage));
        tabs.Items.Add(SettingsTab("modules", "Modules", modulesPage));
        tabs.Items.Add(SettingsTab("backup", "Backup & Restore", backupPage));
        tabs.Items.Add(SettingsTab("printing", "Printing", printingPage));
        tabs.Items.Add(SettingsTab("network", "Network & Sync", networkPage));
        tabs.Items.Add(SettingsTab("devices", "Waiter Devices", devicesPage, horizontalScroll: true));
        tabs.SelectedIndex = 0;

        // Keep tab headers in view while the selected tab's contents scroll.
        // The translucent white surface is shared across the entire tab strip.
        return WorkspaceFrostedSurface.Wrap(tabs);
    }

    private static async Task<FrameworkElement> BackupRestorePanelAsync()
    {
        var factory = new LocalDatabaseFactory();
        var maintenance = new LocalMaintenanceService(factory);
        var diagnostics = await maintenance.GetDiagnosticsAsync();

        var panel = new StackPanel();
        var summary = new TextBlock
        {
            Text = $"Integrity: {diagnostics.Integrity} · " +
                   $"Database: {diagnostics.DatabaseBytes / 1024.0 / 1024.0:N1} MB · " +
                   $"{diagnostics.BackupCount} backups · " +
                   $"Restore staged: {(diagnostics.PendingRestore ? "YES" : "NO")}",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        summary.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        panel.Children.Add(summary);

        var warning = new TextBlock
        {
            Text = "Before restoring, end the restaurant shift, stop mobile orders and " +
                   "close all connected terminals. A restore replaces the active local " +
                   "database at next launch; a pre-restore recovery copy is retained.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        warning.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        panel.Children.Add(warning);

        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var backupButton = new Button
        {
            Content = "Create verified backup",
            MinWidth = 180, Height = 40, Margin = new Thickness(0, 0, 12, 0),
        };
        var restoreButton = new Button
        {
            Content = "Stage database restore",
            MinWidth = 180, Height = 40,
        };
        buttons.Children.Add(backupButton);
        buttons.Children.Add(restoreButton);
        panel.Children.Add(buttons);

        var result = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        result.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        panel.Children.Add(result);

        backupButton.Click += async (_, _) =>
        {
            backupButton.IsEnabled = false;
            try
            {
                var backup = await maintenance.CreateBackupAsync();
                result.Text = $"Backup saved: {backup.Path} · SHA-256: {backup.Sha256}. " +
                              "Keep a safe copy outside this computer.";
            }
            catch (Exception exception)
            {
                result.Text = "Backup failed: " + exception.Message;
            }
            finally
            {
                backupButton.IsEnabled = true;
            }
        };

        restoreButton.Click += async (_, _) =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select a verified BusinessOS Restaurant SQLite backup",
                Filter = "SQLite backups (*.db)|*.db|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            if (picker.ShowDialog() != true)
                return;

            var confirmation = DesktopGlassDialog.Confirm(
                "Restore this backup only after the restaurant has stopped taking orders. " +
                "It will replace the local database on the next launch. " +
                "The app will first retain a safety copy of the current database. Continue?",
                "Stage a restaurant restore");
            if (!confirmation)
                return;

            restoreButton.IsEnabled = false;
            try
            {
                await maintenance.StageRestoreAsync(picker.FileName);
                result.Text = "Backup validated and staged. Restart Restaurant Desktop " +
                              "after the shift is fully stopped to apply the restore.";
            }
            catch (Exception exception)
            {
                result.Text = "Restore not staged: " + exception.Message;
            }
            finally
            {
                restoreButton.IsEnabled = true;
            }
        };

        return panel;
    }

    private static async Task<FrameworkElement> PrinterQueueRecoveryPanelAsync(
        DesktopRestaurantWorkflowService workflow)
    {
        var factory = new LocalDatabaseFactory();
        await factory.EnsureCreatedAsync();
        await using var db = factory.Create();

        // EF Core SQLite cannot translate DateTimeOffset ordering. Read the
        // already status-filtered queue and sort timestamps in managed code;
        // otherwise Settings fails even on a brand-new empty restaurant.
        var kitchen = (await db.PrintJobs.AsNoTracking()
            .Where(job => job.Status == "failed" || job.Status == "printing" || job.Status == "pending")
            .ToArrayAsync())
            .OrderBy(job => job.CreatedAtUtc)
            .ToArray();
        var receipts = (await db.ReceiptPrintJobs.AsNoTracking()
            .Where(job => job.Status == "failed" || job.Status == "printing" || job.Status == "pending")
            .ToArrayAsync())
            .OrderBy(job => job.CreatedAtUtc)
            .ToArray();

        var interrupted = kitchen.Count(job => job.Status == "printing") +
                          receipts.Count(job => job.Status == "printing");
        var failed = kitchen.Count(job => job.Status == "failed") +
                     receipts.Count(job => job.Status == "failed");
        var pending = kitchen.Count(job => job.Status == "pending") +
                      receipts.Count(job => job.Status == "pending");

        var panel = new StackPanel();
        var header = new TextBlock
        {
            Text = $"{pending} pending · {failed} failed · {interrupted} require physical printer review",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 10),
        };
        header.SetResourceReference(TextBlock.ForegroundProperty,
            failed + interrupted > 0 ? "BrandPrimaryBrush" : "TextSecondaryBrush");
        panel.Children.Add(header);

        var instructions = new TextBlock
        {
            Text = "Before retrying, check whether the kitchen ticket or receipt already printed. " +
                   "A manager-confirmed retry may print the same document again; it never creates another KOT round or bill.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        instructions.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        panel.Children.Add(instructions);

        var retryItems = new System.Collections.ObjectModel.ObservableCollection<PrinterIssueChoice>(
            kitchen.Where(job => job.Status != "pending")
                .Select(job => new PrinterIssueChoice(job.Id, false,
                    $"KOT · {job.Status.ToUpperInvariant()} · {job.DocumentName} · {job.PrinterName}"))
                .Concat(receipts.Where(job => job.Status != "pending")
                    .Select(job => new PrinterIssueChoice(job.Id, true,
                        $"RECEIPT · {job.Status.ToUpperInvariant()} · {job.DocumentName} · {job.PrinterName}"))));

        var choice = new ComboBox
        {
            ItemsSource = retryItems,
            DisplayMemberPath = nameof(PrinterIssueChoice.Display),
            Width = 540,
            MaxWidth = 540,
            Height = 38,
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        panel.Children.Add(choice);

        var retry = new Button
        {
            Content = "Review & retry selected print",
            Height = 38,
            MinWidth = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = retryItems.Count > 0,
        };
        var message = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        message.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        retry.Click += async (_, _) =>
        {
            if (choice.SelectedItem is not PrinterIssueChoice selected)
            {
                message.Text = "Select a failed or interrupted printer job.";
                return;
            }

            var answer = DesktopGlassDialog.Confirm(
                "Check the physical printer output first. A retry could print a duplicate " +
                "ticket or receipt. Confirm that you have checked and want to requeue this exact job.",
                "Confirm printer recovery");
            if (!answer)
                return;

            retry.IsEnabled = false;
            try
            {
                message.Text = "Requeueing the existing print document…";
                await DesktopUiWork.RunAsync(() =>
                    workflow.RequeuePrintJobAsync(selected.Id, selected.Receipt));
                retryItems.Remove(selected);
                message.Text = "The existing print document was requeued and audited. " +
                               "No new order, kitchen production or payment record was created.";
            }
            catch (Exception exception)
            {
                message.Text = exception.Message;
            }
            finally
            {
                retry.IsEnabled = retryItems.Count > 0;
            }
        };

        panel.Children.Add(retry);
        panel.Children.Add(message);
        return panel;
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

    private static FrameworkElement SettingsSection(string title, string subtitle, UIElement content)
    {
        // Tab pages have one outer ScrollViewer. Do not embed the older
        // Section() ScrollViewer: it traps the mouse wheel and hides controls.
        var panel = Stack();
        panel.Margin = new Thickness(0, 0, 0, 24);
        var heading = new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var hint = new TextBlock
        {
            Text = subtitle,
            Margin = new Thickness(0, 6, 0, 16),
            TextWrapping = TextWrapping.Wrap,
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        panel.Children.Add(heading);
        panel.Children.Add(hint);
        panel.Children.Add(content);
        return panel;
    }

    private static TabItem SettingsTab(string id, string label, UIElement content, bool horizontalScroll = false)
    {
        var tab = new TabItem
        {
            Header = label,
            Tag = id,
            Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = horizontalScroll
                    ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 18, 0, 0),
            },
        };
        tab.SetResourceReference(FrameworkElement.StyleProperty, "RestaurantSettingsTabItemStyle");
        return tab;
    }

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

    private static FrameworkElement DashboardQuickActions()
    {
        // Navigate through MainWindowViewModel rather than bypassing existing
        // page loading or permissions. Commands inherit the shell DataContext.
        var links = new (string Label, string Route, string Hint)[]
        {
            ("▣  New order / POS", "pos", "Open orders and cashier"),
            ("▦  Tables & floor", "tables", "Manage dining tables"),
            ("☷  Kitchen / KOT", "kitchen", "Review production tickets"),
            ("▤  Inventory", "inventory", "Review ingredient alerts"),
        };

        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        foreach (var (label, route, hint) in links)
        {
            var button = new Button
            {
                Content = label,
                CommandParameter = route,
                ToolTip = hint,
                Height = 42,
                MinWidth = 190,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 10, 8),
                Padding = new Thickness(14, 0, 14, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
            };
            button.SetResourceReference(Button.BackgroundProperty, "TopBarActionBrush");
            button.SetResourceReference(Button.ForegroundProperty, "TextPrimaryBrush");
            button.SetResourceReference(Button.BorderBrushProperty, "CardBorderBrush");
            button.BorderThickness = new Thickness(1);
            button.SetBinding(Button.CommandProperty,
                new Binding(nameof(MainWindowViewModel.NavigateCommand)));
            var visibilityProperty = route switch
            {
                "pos" => nameof(MainWindowViewModel.CanViewPos),
                "tables" => nameof(MainWindowViewModel.CanViewTables),
                "kitchen" => nameof(MainWindowViewModel.CanViewKitchen),
                "inventory" => nameof(MainWindowViewModel.CanViewInventory),
                _ => nameof(MainWindowViewModel.CanViewDashboard),
            };
            button.SetBinding(UIElement.VisibilityProperty, new Binding(visibilityProperty)
            {
                Converter = new BooleanToVisibilityConverter(),
            });
            wrap.Children.Add(button);
        }

        return wrap;
    }

    private static Border DashboardCards(params (string Icon, string Label, string Value, string Detail, Color Accent)[] values)
    {
        var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var value in values)
            wrap.Children.Add(DashboardCard(value.Icon, value.Label, value.Value, value.Detail, value.Accent));

        var container = new Border
        {
            Child = wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        container.SizeChanged += (_, _) =>
        {
            var available = container.ActualWidth;
            if (available <= 0 || !double.IsFinite(available)) return;

            // KPI cards fill each row rather than clipping at lower resolutions.
            var columns = Math.Clamp((int)((available + 10) / 245), 1, values.Length);
            var cardWidth = Math.Max(164, Math.Floor(available / columns) - 10);
            foreach (var card in wrap.Children.OfType<Border>())
                card.Width = cardWidth;
        };
        return container;
    }

    private static Border DashboardCard(string icon, string title, string value, string detail, Color accent)
    {
        var accentBrush = new SolidColorBrush(accent);
        var soft = Color.FromArgb(52, accent.R, accent.G, accent.B);
        var softBrush = new SolidColorBrush(soft);

        var iconText = new TextBlock
        {
            Text = icon,
            Foreground = Brushes.White,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var iconSurface = new Border
        {
            Width = 58,
            Height = 58,
            CornerRadius = new CornerRadius(15),
            Background = accentBrush,
            Child = iconText,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        var valueText = new TextBlock
        {
            Text = value,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        valueText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var detailText = new TextBlock
        {
            Text = detail,
            FontSize = 10.5,
            Margin = new Thickness(0, 4, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        detailText.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");

        var copy = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        copy.Children.Add(titleText);
        copy.Children.Add(valueText);
        copy.Children.Add(detailText);

        var content = new Grid { Margin = new Thickness(14, 13, 14, 13) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(iconSurface);
        Grid.SetColumn(copy, 1);
        content.Children.Add(copy);

        var decoration = new Canvas
        {
            IsHitTestVisible = false,
            Opacity = 0.72,
            ClipToBounds = true,
        };
        var waveOne = new Ellipse
        {
            Width = 165,
            Height = 72,
            Fill = softBrush,
            Stroke = new SolidColorBrush(Color.FromArgb(70, accent.R, accent.G, accent.B)),
            StrokeThickness = 1.2,
        };
        Canvas.SetRight(waveOne, -34);
        Canvas.SetBottom(waveOne, -29);
        decoration.Children.Add(waveOne);

        var waveTwo = new Ellipse
        {
            Width = 132,
            Height = 56,
            Fill = Brushes.Transparent,
            Stroke = new SolidColorBrush(Color.FromArgb(90, accent.R, accent.G, accent.B)),
            StrokeThickness = 1.1,
        };
        Canvas.SetRight(waveTwo, -10);
        Canvas.SetBottom(waveTwo, -30);
        decoration.Children.Add(waveTwo);

        var layer = new Grid();
        layer.Children.Add(decoration);
        layer.Children.Add(content);

        var border = new Border
        {
            CornerRadius = new CornerRadius(18),
            Margin = new Thickness(0, 0, 10, 10),
            Width = 238,
            Height = 112,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = layer,
        };
        border.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        border.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 3,
            Opacity = 0.12,
            Color = Color.FromRgb(20, 38, 61),
        };
        return border;
    }

    private static UIElement BuildStockAlertSummary(
        IReadOnlyList<DashboardStockAlert> alerts, int balanceCount, int configuredCount)
    {
        var stack = new StackPanel();
        if (balanceCount == 0 || configuredCount == 0 || alerts.Count == 0)
        {
            var description = balanceCount == 0
                ? "No active branch stock balances have been loaded."
                : configuredCount == 0
                    ? "Set reorder levels for ingredients to enable stock alerts."
                    : "No ingredients are currently at or below their reorder levels.";
            var empty = new TextBlock
            {
                Text = description,
                FontSize = 12,
                Margin = new Thickness(2, 3, 2, 6),
                TextWrapping = TextWrapping.Wrap,
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            stack.Children.Add(empty);
            return stack;
        }

        foreach (var alert in alerts.Take(5))
        {
            var row = new Grid { Margin = new Thickness(2, 3, 2, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var item = new TextBlock
            {
                Text = $"{alert.ItemName}  ·  {alert.BranchName}",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 14, 0),
            };
            item.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

            var shortage = new TextBlock
            {
                Text = $"{alert.Quantity:N2} / {alert.ReorderLevel:N2} {alert.BaseUnit}",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
            };
            shortage.SetResourceReference(TextBlock.ForegroundProperty,
                alert.Quantity <= 0m ? "BrandPrimaryBrush" : "TextSecondaryBrush");

            row.Children.Add(item);
            Grid.SetColumn(shortage, 1);
            row.Children.Add(shortage);
            stack.Children.Add(row);
        }

        if (alerts.Count > 5)
        {
            var remaining = new TextBlock
            {
                Text = $"+ {alerts.Count - 5} more branch ingredient alert(s) · open Inventory to review all balances.",
                FontSize = 11,
                Margin = new Thickness(2, 2, 2, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            remaining.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            stack.Children.Add(remaining);
        }
        return stack;
    }

    private static Border DashboardPanel(string title, string subtitle, UIElement content)
    {
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var subtitleText = new TextBlock
        {
            Text = subtitle,
            FontSize = 10.5,
            Margin = new Thickness(0, 2, 0, 12),
        };
        subtitleText.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");

        var stack = new StackPanel();
        stack.Children.Add(titleText);
        stack.Children.Add(subtitleText);
        stack.Children.Add(content);

        var border = new Border
        {
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 10),
            BorderThickness = new Thickness(1),
            Child = stack,
        };
        border.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        border.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 3,
            Opacity = 0.10,
            Color = Color.FromRgb(20, 38, 61),
        };
        return border;
    }

    private static UIElement BuildOperationsOverview(
        decimal sales, int orders, int tables, int kitchen,
        DashboardDailySales salesTrend, int currentHour)
    {
        var grid = new Grid { MinHeight = 178 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var metrics = new UniformGrid
        {
            Columns = 4,
            Margin = new Thickness(0, 0, 0, 12),
        };
        metrics.Children.Add(MiniMetric("Sales", $"AFN {sales:N0}", Color.FromRgb(34, 197, 94)));
        metrics.Children.Add(MiniMetric("Orders", orders.ToString(), Color.FromRgb(14, 165, 233)));
        metrics.Children.Add(MiniMetric("Tables", tables.ToString(), Color.FromRgb(245, 158, 11)));
        metrics.Children.Add(MiniMetric("Kitchen", kitchen.ToString(), Color.FromRgb(236, 72, 153)));
        grid.Children.Add(metrics);

        var trend = salesTrend.HourlyTotals.Take(currentHour + 1).ToArray();
        var chartArea = new Grid { Height = 138, Margin = new Thickness(4, 6, 4, 0) };

        if (trend.All(amount => amount <= 0))
        {
            var empty = new TextBlock
            {
                Text = "No billed sales yet today — the trend will appear after the first bill.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            chartArea.Children.Add(empty);
        }
        else
        {
            // Draw vectors in logical WPF units instead of scaling a fixed-width
            // Viewbox. On 4K, Stretch.Fill distorted strokes and circular markers.
            var plot = new Canvas
            {
                Height = 118,
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var guides = new List<Line>();
            for (var i = 0; i < 4; i++)
            {
                var guide = new Line
                {
                    X1 = 14,
                    Y1 = 20 + i * 29,
                    Y2 = 20 + i * 29,
                    StrokeThickness = 1,
                    Opacity = 0.26,
                };
                guide.SetResourceReference(Shape.StrokeProperty, "BorderBrush");
                guides.Add(guide);
                plot.Children.Add(guide);
            }

            var peak = trend.Max();
            Polyline? trendLine = null;
            if (trend.Length > 1)
            {
                trendLine = new Polyline
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(47, 107, 255)),
                    StrokeThickness = 3,
                    StrokeLineJoin = PenLineJoin.Round,
                };
                plot.Children.Add(trendLine);
            }

            var marker = new Ellipse
            {
                Width = 9, Height = 9,
                Fill = new SolidColorBrush(Color.FromRgb(47, 107, 255)),
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
            };
            plot.Children.Add(marker);

            void UpdateChartGeometry()
            {
                if (plot.ActualWidth <= 0) return;
                var width = plot.ActualWidth;
                foreach (var guide in guides)
                    guide.X2 = DashboardChartLayout.GuideEnd(width);

                var points = new PointCollection();
                for (var hour = 0; hour < trend.Length; hour++)
                {
                    var x = DashboardChartLayout.HourX(hour, trend.Length, width);
                    var y = 108 - 86 * (double)(Math.Max(0m, trend[hour]) / peak);
                    points.Add(new Point(x, y));
                }

                if (trendLine is not null)
                    trendLine.Points = points;
                var lastPoint = points[^1];
                Canvas.SetLeft(marker, lastPoint.X - marker.Width / 2);
                Canvas.SetTop(marker, lastPoint.Y - marker.Height / 2);
            }

            plot.SizeChanged += (_, _) => UpdateChartGeometry();
            chartArea.Children.Add(plot);
        }

        var chartStack = new StackPanel();
        chartStack.Children.Add(chartArea);
        var timeLabels = new Grid { Margin = new Thickness(5, 0, 5, 0) };
        timeLabels.Children.Add(new TextBlock
        {
            Text = "00:00",
            FontSize = 10,
            Foreground = Brushes.SlateGray,
        });
        timeLabels.Children.Add(new TextBlock
        {
            Text = $"{currentHour:00}:00 · local time",
            FontSize = 10,
            Foreground = Brushes.SlateGray,
            HorizontalAlignment = HorizontalAlignment.Right,
        });
        chartStack.Children.Add(timeLabels);
        Grid.SetRow(chartStack, 1);
        grid.Children.Add(chartStack);

        return grid;
    }

    private static Border MiniMetric(string label, string value, Color accent)
    {
        var labelText = new TextBlock { Text = label, FontSize = 10.5 };
        labelText.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        var valueText = new TextBlock
        {
            Text = value,
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 3, 0, 0),
        };
        valueText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var stack = new StackPanel();
        stack.Children.Add(labelText);
        stack.Children.Add(valueText);

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(0, 0, 0, 3),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 10, 0),
            Child = stack,
        };
    }

    private static UIElement BuildStatusRows(params (string Label, string Value)[] rows)
    {
        var stack = new StackPanel();
        foreach (var row in rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock { Text = row.Label, FontSize = 11 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            var value = new TextBlock
            {
                Text = row.Value,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(12, 0, 0, 0),
            };
            value.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

            grid.Children.Add(label);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
            stack.Children.Add(grid);
        }

        return stack;
    }

    private static UIElement BuildFeatureSummary(string icon, string headline, string detail, Color accent)
    {
        var iconText = new TextBlock
        {
            Text = icon,
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(accent),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var headlineText = new TextBlock
        {
            Text = headline,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        headlineText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var detailText = new TextBlock
        {
            Text = detail,
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        detailText.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");

        var copy = new StackPanel { Margin = new Thickness(13, 0, 0, 0) };
        copy.Children.Add(headlineText);
        copy.Children.Add(detailText);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(iconText);
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);
        return grid;
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
        // Avoid repeating informational prose beneath every section header.
        // Operational warnings and validation messages remain in their panels.
        panel.Children.Add(titleText);
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
            MinHeight = 220,
            MaxHeight = 440,
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
    private sealed record PrinterIssueChoice(string Id, bool Receipt, string Display);

    private sealed record InventoryRow(string Sku, string Name, string BaseUnit, string PurchaseUnit, decimal Quantity, decimal ReorderLevel, decimal AverageCost, decimal StockValue);
    private sealed record PurchaseRow(string Number, string Supplier, string Status, decimal Total, DateTimeOffset OrderedAt, DateTimeOffset? CompletedAt);
    private sealed record SupplierRow(string Code, string Name, string? Phone, string? Email);
    private sealed record ReceiptRow(string Number, string Status, DateTimeOffset ReceivedAt, string PurchaseOrderId);
    private sealed record StaffRow(string Name, string Email, string Role, bool Active);
    private sealed record ShiftRow(string Name, string Role, string Status, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, int BreakMinutes);
    private sealed record ExpenseBranchChoice(string Id, string Name);
    private sealed record ExpenseRow(DateOnly Date, string Branch, string Category, string Description, decimal Amount, string Currency, string Method, string? Reference);
}