using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.Desktop;

internal static class OperationalActionViews
{
    // Only irreversible changes ask for confirmation. Normal order entry and
    // KOT production remain quick single-click workflows.
    private static bool ConfirmOperationalChange(string message, string title) =>
        DesktopGlassDialog.Confirm(message, title);

    public static async Task<FrameworkElement> PosAsync()
    {
        var navigationClock = Stopwatch.StartNew();
        // Materialize all SQLite query results on a background thread before
        // constructing any WPF controls. SQLite async providers may block
        // synchronously while waiting for locks.
        var pageData = await DesktopUiWork.RunAsync(async () =>
        {
            var factory = new LocalDatabaseFactory();
            await factory.EnsureCreatedAsync();
            await using var db = factory.Create();

            var tables = await (from table in db.DiningTables.AsNoTracking()
                                join area in db.DiningAreas.AsNoTracking() on table.DiningAreaId equals area.Id
                                join branch in db.Branches.AsNoTracking() on area.BranchId equals branch.Id
                                where table.IsActive && area.IsActive && branch.IsActive &&
                                      table.Status == "available"
                                orderby area.SortOrder, table.Name
                                select new Choice(table.Id, $"{branch.Name} · {area.Name} · {table.Name} ({table.Code})")).ToListAsync();
            var menu = await (
                from item in db.MenuItems.AsNoTracking()
                join category in db.MenuCategories.AsNoTracking() on item.MenuCategoryId equals category.Id into groups
                from category in groups.DefaultIfEmpty()
                where item.IsAvailable
                orderby item.SortOrder, item.Name
                select new MenuChoice(item.Id, item.Name, item.Price,
                    category == null ? "Uncategorized" : category.Name, item.ImageUrl)).ToListAsync();
            var modifierChoices = await (
                from link in db.MenuItemModifierGroups.AsNoTracking()
                join modifierGroup in db.ModifierGroups.AsNoTracking() on link.ModifierGroupId equals modifierGroup.Id
                join option in db.ModifierOptions.AsNoTracking() on modifierGroup.Id equals option.ModifierGroupId
                where modifierGroup.IsActive && option.IsActive
                orderby link.SortOrder, modifierGroup.SortOrder, option.SortOrder, option.Name
                select new ModifierChoice(
                    link.MenuItemId,
                    option.Id,
                    $"{modifierGroup.Name}: {option.Name}",
                    option.PriceDelta))
                .ToListAsync();
            var orders = (await db.Orders.AsNoTracking().Where(x => x.Status != "closed")
                    .Select(x => new { Row = new OrderChoice(x.Id, x.ClientOrderId, x.ServiceType, x.WaiterName, x.Status, x.GuestCount, x.Total, x.DisplayNumber), x.UpdatedAtUtc })
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
            // The visible Orders list is capped at 100. Do not load the item
            // history for every old unclosed order when POS first opens.
            var visibleOrderIds = orders.Select(x => x.Id).ToArray();
            // SQLite cannot translate ORDER BY on DateTimeOffset; sort the projected rows in memory.
            var orderLines = (await (
                from line in db.OrderItems.AsNoTracking()
                join order in db.Orders.AsNoTracking() on line.OrderId equals order.Id
                where visibleOrderIds.Contains(order.Id) &&
                      order.Status != "closed" && order.Status != "cancelled" &&
                      line.Status != "voided" && line.Status != "cancelled"
                select new
                {
                    Row = new OrderLineChoice(
                        order.ClientOrderId, line.ClientLineId, line.ItemName,
                        line.Quantity, line.Status, line.RoundNumber),
                    line.CreatedAtUtc,
                })
                .ToListAsync())
                .OrderBy(x => x.CreatedAtUtc)
                .Select(x => x.Row)
                .ToList();

            // Floor tiles drive the POS: available and occupied tables with
            // their live guests/total for one-tap table ordering.
            var floorTables = await (from table in db.DiningTables.AsNoTracking()
                                     join area in db.DiningAreas.AsNoTracking() on table.DiningAreaId equals area.Id
                                     join branch in db.Branches.AsNoTracking() on area.BranchId equals branch.Id
                                     where table.IsActive && area.IsActive && branch.IsActive
                                     orderby area.SortOrder, table.Name
                                     select new TableChoice(table.Id, branch.Name + " · " + area.Name,
                                         table.Code, table.Name, table.Capacity, table.Status)).ToListAsync();
            var floorOrders = (await db.Orders.AsNoTracking()
                    .Where(x => x.ServiceType == "dine_in" && x.Status != "closed" && x.Status != "cancelled")
                    .Select(x => new
                    {
                        Row = new TableOrderChoice(x.Id, x.ClientOrderId, x.DiningTableId, "", x.Status, x.Total, x.GuestCount, x.DisplayNumber),
                        x.UpdatedAtUtc,
                    })
                    .ToListAsync())
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Select(x => x.Row).ToArray();

            return (factory, tables, menu, modifierChoices, orders, branches, sessions, bills, orderLines, floorTables, floorOrders);
        });
        var (factory, tables, menu, modifierChoices, orders, branches, sessions, bills, orderLines, floorTables, floorOrders) = pageData;
        // Mutable floor state so tiles refresh in place without rebuilding the page.
        var tablesNow = floorTables;
        var ordersNow = floorOrders;
        TableChoice? selectedTable = null;
        Action<string>? openDishForSelectedTable = null;
        var posTableStrip = new WrapPanel { Name = "PosTableStrip", Margin = new Thickness(0, 0, 0, 4) };
        Trace.WriteLine($"[Restaurant] POS SQLite: {navigationClock.ElapsedMilliseconds} ms; menu={menu.Count}, activeOrders={orders.Count}, lines={orderLines.Count}");

        var workflow = new DesktopRestaurantWorkflowService();
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(470) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var form = new StackPanel();
        form.Children.Add(Header(
            "POS & Orders",
            "Open a new order or resume an active order. Only unsent items are included when you send a new KOT round."));

        var serviceTypeBox = new ComboBox
        {
            Name = "PosServiceType",
            ItemsSource = new[] { "dine_in", "takeaway", "delivery", "counter" },
            SelectedIndex = 0,
            Height = 34,
            Margin = new Thickness(0, 4, 0, 6),
        };
        var orderBranchBox = Combo(branches, "Label");
        var tableBox = Combo(tables, "Label");
        var serviceReferenceBox = new TextBox
        {
            Margin = new Thickness(0, 4, 0, 10),
            Height = 34,
        };
        var guestBox = new TextBox { Text = "1", Margin = new Thickness(0, 4, 0, 10), Height = 34 };
        var menuBox = Combo(menu, "Display");
        menuBox.Name = "PosMenuItems";
        var menuSearchBox = new TextBox
        {
            Name = "PosMenuSearchBox",
            Height = 36,
            Margin = new Thickness(0, 4, 0, 6),
            ToolTip = "Search available menu products by name",
        };
        System.Windows.Automation.AutomationProperties.SetName(menuSearchBox, "Search menu items");
        var menuPrice = new TextBlock { Margin = new Thickness(0, 2, 0, 8) };
        menuPrice.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var qtyBox = new TextBox { Text = "1", Margin = new Thickness(0, 4, 0, 6), Height = 34 };
        var seatBox = new TextBox { Margin = new Thickness(0, 4, 8, 6), Height = 34, Width = 80 };
        var courseBox = new TextBox { Margin = new Thickness(0, 4, 8, 6), Height = 34, Width = 80 };
        var courseNameBox = new TextBox { Margin = new Thickness(0, 4, 0, 6), Height = 34, Width = 180 };
        var itemNotesBox = new TextBox { Margin = new Thickness(0, 4, 0, 6), MinHeight = 54, TextWrapping = TextWrapping.Wrap };
        var kitchenInstructionsBox = new TextBox { Margin = new Thickness(0, 4, 0, 6), MinHeight = 54, TextWrapping = TextWrapping.Wrap };
        var allergyBox = new TextBox { Margin = new Thickness(0, 4, 0, 6), MinHeight = 54, TextWrapping = TextWrapping.Wrap };
        var heldBox = new CheckBox { Content = "Hold for course firing", Margin = new Thickness(0, 5, 12, 5) };
        var rushBox = new CheckBox { Content = "Rush priority", Margin = new Thickness(0, 5, 12, 5) };
        var modifiersBox = new ListBox
        {
            SelectionMode = SelectionMode.Multiple,
            Height = 100,
            Margin = new Thickness(0, 4, 0, 6),
            DisplayMemberPath = "Display",
        };
        var orderIdBox = new TextBox { IsReadOnly = true, Margin = new Thickness(0, 4, 0, 10), Height = 34 };
        var fireCourseBox = new TextBox { Width = 80, Height = 34, Margin = new Thickness(0, 4, 8, 6) };
        var voidReasonBox = new TextBox { Height = 34, Margin = new Thickness(0, 4, 8, 6), MinWidth = 220 };
        var lineBox = Combo(orderLines, "Display");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var selectionSummary = new TextBlock
        {
            Name = "PosSelectionSummary",
            Text = "No order selected · Open an order or choose one from Active orders.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 12),
            FontWeight = FontWeights.SemiBold,
        };
        selectionSummary.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        form.Children.Add(Label("1 · SERVICE"));
        form.Children.Add(Label("Service type")); form.Children.Add(serviceTypeBox);
        var dineFields = new StackPanel { Name = "PosDineInFields" };
        dineFields.Children.Add(Label("Table")); dineFields.Children.Add(tableBox);
        var offsiteFields = new StackPanel { Name = "PosOffsiteFields" };
        offsiteFields.Children.Add(Label("Branch")); offsiteFields.Children.Add(orderBranchBox);
        offsiteFields.Children.Add(Label("Customer / delivery / pickup reference"));
        offsiteFields.Children.Add(serviceReferenceBox);
        form.Children.Add(dineFields);
        form.Children.Add(offsiteFields);
        form.Children.Add(Label("Guests")); form.Children.Add(guestBox);
        void UpdateServiceFields()
        {
            var dineIn = string.Equals(serviceTypeBox.SelectedItem?.ToString(), "dine_in",
                StringComparison.Ordinal);
            dineFields.Visibility = dineIn ? Visibility.Visible : Visibility.Collapsed;
            offsiteFields.Visibility = dineIn ? Visibility.Collapsed : Visibility.Visible;
        }
        serviceTypeBox.SelectionChanged += (_, _) => UpdateServiceFields();
        UpdateServiceFields();

        var open = Button("Open new order");
        open.MinHeight = 42;
        form.Children.Add(open);
        form.Children.Add(Label("2 · CURRENT ORDER"));
        form.Children.Add(Label("Selected order (read-only)")); form.Children.Add(orderIdBox);
        form.Children.Add(selectionSummary);
        var clearSelection = Button("Start another order");
        clearSelection.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        form.Children.Add(clearSelection);
        form.Children.Add(Label("3 · ADD ITEMS"));
        form.Children.Add(Label("Search menu")); form.Children.Add(menuSearchBox);
        form.Children.Add(Label("Menu item")); form.Children.Add(menuBox);
        form.Children.Add(menuPrice);
        form.Children.Add(Label("Quantity")); form.Children.Add(qtyBox);

        var seatCourseRow = new WrapPanel();
        seatCourseRow.Children.Add(Label("Seat"));
        seatCourseRow.Children.Add(seatBox);
        seatCourseRow.Children.Add(Label("Course"));
        seatCourseRow.Children.Add(courseBox);
        seatCourseRow.Children.Add(courseNameBox);
        var advancedItemDetails = new StackPanel { Name = "PosItemOptions", Visibility = Visibility.Collapsed };
        advancedItemDetails.Children.Add(seatCourseRow);

        var itemFlags = new WrapPanel();
        itemFlags.Children.Add(heldBox);
        itemFlags.Children.Add(rushBox);
        advancedItemDetails.Children.Add(itemFlags);
        advancedItemDetails.Children.Add(Label("Modifiers")); advancedItemDetails.Children.Add(modifiersBox);
        advancedItemDetails.Children.Add(Label("Item note")); advancedItemDetails.Children.Add(itemNotesBox);
        advancedItemDetails.Children.Add(Label("Kitchen instruction")); advancedItemDetails.Children.Add(kitchenInstructionsBox);
        advancedItemDetails.Children.Add(Label("Allergy / special warning")); advancedItemDetails.Children.Add(allergyBox);
        var toggleItemOptions = Button("Show modifiers & instructions");
        toggleItemOptions.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        toggleItemOptions.Click += (_, _) =>
        {
            var showing = advancedItemDetails.Visibility != Visibility.Visible;
            advancedItemDetails.Visibility = showing ? Visibility.Visible : Visibility.Collapsed;
            toggleItemOptions.Content = showing ? "Hide modifiers & instructions" : "Show modifiers & instructions";
        };
        form.Children.Add(toggleItemOptions);
        form.Children.Add(advancedItemDetails);

        var add = Button("Add item");
        // When the first photo is tapped with no current order, take the
        // operator straight to table/guest setup and remember that dish.
        // Opening the order then adds it without another menu search.
        var firstPhotoAwaitingTable = false;
        Action? showQuickOrderSetup = null;
        Action? closeQuickOrderSetup = null;
        var submit = Button("Send new KOT round");
        submit.Name = "PosSendKotButton";
        submit.ToolTip = "F9 · Send unsent items to the kitchen";
        add.MinHeight = 44;
        submit.MinHeight = 44;
        var kitchenActions = new WrapPanel { Margin = new Thickness(0, 8, 0, 6) };
        kitchenActions.Children.Add(add);
        kitchenActions.Children.Add(submit);
        form.Children.Add(kitchenActions);
        // Every tile uses the same local order-entry action and item IDs as
        // the original combo box, preserving modifiers, KOT rules and pricing.
        var photoCatalog = RestaurantPhotoPosCatalog.Build(
            menu.Select(x => new PhotoPosItem(x.Id, x.Name, x.Category, x.Price, x.ImageUrl)).ToArray(),
            menuSearchBox,
            item =>
            {
                // Dishes always join the order of the table tile chosen above;
                // the table modal shows the updated ticket immediately.
                if (selectedTable is null)
                {
                    selectionSummary.Text = "Tap a table tile above to start or resume its order.";
                    return;
                }
                openDishForSelectedTable?.Invoke(item.Id);
            });
        form.Children.Insert(form.Children.IndexOf(menuBox), photoCatalog);
        form.Children.Add(Label("4 · KITCHEN & ORDER ADJUSTMENTS"));
        var advancedOrderActions = new StackPanel
        {
            Name = "PosOrderAdjustments",
            Visibility = Visibility.Collapsed,
        };
        var courseRow = new WrapPanel();
        courseRow.Children.Add(fireCourseBox);
        var fireCourse = Button("Fire course");
        courseRow.Children.Add(fireCourse);
        advancedOrderActions.Children.Add(courseRow);

        advancedOrderActions.Children.Add(Label("Existing order line"));
        advancedOrderActions.Children.Add(lineBox);
        var voidRow = new WrapPanel();
        voidRow.Children.Add(voidReasonBox);
        var voidLine = Button("Void selected line");
        var cancelOrder = Button("Cancel order");
        voidRow.Children.Add(voidLine);
        voidRow.Children.Add(cancelOrder);
        advancedOrderActions.Children.Add(voidRow);
        var toggleOrderActions = Button("Show course firing / void / cancel");
        toggleOrderActions.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        toggleOrderActions.Click += (_, _) =>
        {
            var showing = advancedOrderActions.Visibility != Visibility.Visible;
            advancedOrderActions.Visibility = showing ? Visibility.Visible : Visibility.Collapsed;
            toggleOrderActions.Content = showing ? "Hide course firing / void / cancel" : "Show course firing / void / cancel";
        };
        form.Children.Add(toggleOrderActions);
        form.Children.Add(advancedOrderActions);
        form.Children.Add(status);
        menuSearchBox.TextChanged += (_, _) =>
        {
            var selected = menuBox.SelectedItem as MenuChoice;
            var term = menuSearchBox.Text.Trim();
            var filtered = menu.Where(x => x.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
            menuBox.ItemsSource = filtered;
            if (selected is not null && filtered.Contains(selected))
                menuBox.SelectedItem = selected;
            else if (filtered.Length == 1)
                menuBox.SelectedIndex = 0;
        };

        var grid = DataGrid(orders);
        grid.Name = "PosActiveOrdersGrid";
        grid.MinHeight = 110;
        grid.MaxHeight = 380;
        grid.SelectionMode = DataGridSelectionMode.Single;
        grid.Columns.Add(Column("Order", nameof(OrderChoice.Display), 220));
        grid.Columns.Add(Column("Service", nameof(OrderChoice.ServiceType), 110));
        grid.Columns.Add(Column("Waiter", nameof(OrderChoice.Waiter), 150));
        grid.Columns.Add(Column("Guests", nameof(OrderChoice.Guests), 80));
        grid.Columns.Add(Column("Status", nameof(OrderChoice.Status), 120));
        grid.Columns.Add(Column("Total AFN", nameof(OrderChoice.Total), 120));
        var formCard = Card(form);
        var orderListPanel = new StackPanel();
        orderListPanel.Children.Add(Header("CURRENT ORDER & PAYMENT",
            "Live local cart with all KOT rounds. Confirm service before billing; the table releases only on full settlement."));
        var currentOrderLines = new ListBox
        {
            Name = "PosCurrentOrderLines", MinHeight = 74, MaxHeight = 160,
            DisplayMemberPath = "Display", Margin = new Thickness(0, 4, 0, 8),
        };
        var currentOrderTotal = new TextBlock { Text = "Choose a table and open an order to begin.",
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 1, 0, 14),
            TextWrapping = TextWrapping.Wrap };
        currentOrderTotal.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        orderListPanel.Children.Add(currentOrderLines);
        orderListPanel.Children.Add(currentOrderTotal);
        orderListPanel.Children.Add(Header("ACTIVE ORDERS",
            "Search and select the ticket to resume ordering or send a new kitchen round."));
        var orderOverview = new TextBlock { Margin = new Thickness(0, 0, 0, 12), FontWeight = FontWeights.SemiBold };
        orderOverview.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        orderListPanel.Children.Add(orderOverview);
        var orderSearchBox = new TextBox
        {
            Name = "PosOrderSearchBox",
            Height = 38,
            Margin = new Thickness(0, 4, 8, 6),
            ToolTip = "Find an order by number, waiter or service type",
        };
        System.Windows.Automation.AutomationProperties.SetName(orderSearchBox, "Search active orders");
        var orderStatusFilter = new ComboBox
        {
            Name = "PosOrderStatusFilter",
            ItemsSource = new[] { "All statuses" }.Concat(
                orders.Select(x => x.Status).Distinct(StringComparer.OrdinalIgnoreCase)).ToArray(),
            SelectedIndex = 0, Width = 160, Height = 38,
            Margin = new Thickness(0, 4, 0, 6),
        };
        var listTools = new WrapPanel();
        var searchGroup = new StackPanel { Width = 250 };
        searchGroup.Children.Add(Label("Search orders"));
        searchGroup.Children.Add(orderSearchBox);
        listTools.Children.Add(searchGroup);
        var filterGroup = new StackPanel();
        filterGroup.Children.Add(Label("Status"));
        filterGroup.Children.Add(orderStatusFilter);
        listTools.Children.Add(filterGroup);
        orderListPanel.Children.Add(listTools);
        var selectHint = new TextBlock
        {
            Text = "Select a row to continue. Billed or cancelled orders are not editable.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10),
        };
        selectHint.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        orderListPanel.Children.Add(selectHint);
        var emptyOrderHint = new TextBlock
        {
            Text = "No orders match your search. Choose + New order to begin service.",
            Margin = new Thickness(0, 10, 0, 12),
            TextWrapping = TextWrapping.Wrap,
        };
        emptyOrderHint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        orderListPanel.Children.Add(emptyOrderHint);
        orderListPanel.Children.Add(grid);
        var orderListActions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var startOrderFromList = Button("+ New order");
        startOrderFromList.MinHeight = 42;
        orderListActions.Children.Add(startOrderFromList);
        var useSelected = Button("Continue selected order");
        useSelected.MinHeight = 42;
        orderListActions.Children.Add(useSelected);
        orderListPanel.Children.Add(orderListActions);
        var ordersCard = Card(orderListPanel);
        Grid.SetColumn(formCard, 0);
        Grid.SetColumn(ordersCard, 2);
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(formCard);
        root.Children.Add(ordersCard);
        // Full-screen photo POS is composed after all cashier/KOT event handlers
        // are wired, preserving the existing authoritative local workflows.

        var cashier = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        cashier.Children.Add(Header("Checkout", "Serve the order, issue its bill and collect payment. The active cashier is managed automatically."));
        var cashierStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,0) };
        cashierStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        // One local cashier workflow: do not expose branch selection, opening
        // cash or a redundant cashier-session picker in the checkout modal.
        // Sessions remain audited in SQLite and are resolved by bill branch
        // and the signed-in operator only when posting an actual payment.
        var orderBox = Combo(orders, nameof(OrderChoice.Display)); orderBox.Width = 360;
        var billBox = Combo(bills, "Display"); billBox.Width = 360; billBox.Name = "PosBillPicker";
        // This restaurant collects cash only, so no payment-method selection is
        // shown; postings stay fully audited against the cashier session.
        var amountBox = new TextBox { Name = "PosPaymentAmount", Text = "0", Height = 38, Width = 180, Margin = new Thickness(0,4,8,6) };
        var discountBox = new TextBox { Text = "0", Height = 34, Width = 110, Margin = new Thickness(0,4,8,6) };
        var splitCountBox = new TextBox { Text = "2", Height = 34, Width = 80, Margin = new Thickness(0,4,8,6) };

        cashier.Children.Add(Label("Order")); cashier.Children.Add(orderBox);
        var orderActions = new WrapPanel(); var serve = Button("Mark served"); var issue = Button("Issue bill"); orderActions.Children.Add(serve); orderActions.Children.Add(issue); cashier.Children.Add(orderActions);
        cashier.Children.Add(Label("Open bill")); cashier.Children.Add(billBox);
        var discountRow = new WrapPanel(); discountRow.Children.Add(discountBox); var discount = Button("Apply % discount"); discountRow.Children.Add(discount); discountRow.Children.Add(splitCountBox); var split = Button("Split equally"); discountRow.Children.Add(split);
        var adjustments = new Expander
        {
            Header = "Discounts & split payments (optional)",
            IsExpanded = false,
            Margin = new Thickness(0, 10, 0, 4),
            Content = discountRow,
        };
        cashier.Children.Add(adjustments);
        var paymentBalance = new TextBlock
        {
            Name = "PosLivePaymentBalance", Text = "Choose an open bill to view the amount due.",
            FontSize = 17, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 8), TextWrapping = TextWrapping.Wrap,
        };
        paymentBalance.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        cashier.Children.Add(paymentBalance);
        var checkoutFields = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) };
        var amountField = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        amountField.Children.Add(Label("Amount received (AFN)"));
        amountField.Children.Add(amountBox);
        checkoutFields.Children.Add(amountField);
        cashier.Children.Add(checkoutFields);
        var payRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 8) };
        var pay = Button("Post payment"); pay.MinHeight = 48;
        var receipt = Button("Queue receipt");
        var verifyBalance = Button("Check current balances");
        verifyBalance.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        payRow.Children.Add(pay); payRow.Children.Add(receipt); payRow.Children.Add(verifyBalance);
        cashier.Children.Add(payRow);
        cashier.Children.Add(cashierStatus);

        void ShowLiveBalance()
        {
            var selected = billBox.SelectedItem as BillChoice;
            paymentBalance.Text = selected is null
                ? "No outstanding bill selected."
                : $"{selected.Number}  ·  AFN {selected.Balance:N2} remaining " +
                  $"of AFN {selected.Total:N2}";
        }
        billBox.SelectionChanged += (_, _) =>
        {
            ShowLiveBalance();
            if (billBox.SelectedItem is BillChoice selected && selected.Balance > 0m)
                amountBox.Text = selected.Balance.ToString("0.00");
        };
        orderBox.SelectedIndex = orders.Count > 0 ? 0 : -1;
        billBox.SelectedIndex = bills.Count > 0 ? 0 : -1;
        ShowLiveBalance();

        async Task RefreshCashierStateAsync(string? preferredOrderId = null)
        {
            var chosenBillId = (billBox.SelectedItem as BillChoice)?.Id;

            var latest = await DesktopUiWork.RunAsync(async () =>
            {
                await using var liveDb = factory.Create();
                var currentBills = (await liveDb.Bills.AsNoTracking()
                    .Where(x => x.Status == "open")
                    .Select(x => new { x.Id, x.OrderId, x.BillNumber, x.Total, x.PaidAmount, x.BalanceDue, x.IssuedAt })
                    .ToListAsync()).OrderByDescending(x => x.IssuedAt)
                    .Select(x => new BillChoice(x.Id, x.OrderId, x.BillNumber, x.Total, x.PaidAmount, x.BalanceDue))
                    .ToList();
                var currentSessions = (await liveDb.CashierSessions.AsNoTracking()
                    .Where(x => x.Status == "open")
                    .Select(x => new CashierSessionChoice(x.Id, x.CashierName, x.Status,
                        x.OpeningCash, x.ExpectedCash, x.DeclaredCash, x.CashVariance, x.OpenedAt))
                    .ToListAsync()).OrderByDescending(x => x.OpenedAt).ToList();
                return (Bills: currentBills, Sessions: currentSessions);
            });
            bills.Clear();
            bills.AddRange(latest.Bills);
            sessions.Clear();
            sessions.AddRange(latest.Sessions);

            billBox.ItemsSource = bills.ToArray();
            billBox.SelectedItem = (preferredOrderId is not null
                ? bills.FirstOrDefault(x => x.OrderId == preferredOrderId)
                : null) ?? bills.FirstOrDefault(x => x.Id == chosenBillId) ?? bills.FirstOrDefault();
            ShowLiveBalance();
            var chosen = billBox.SelectedItem as BillChoice;
            amountBox.Text = chosen is { Balance: > 0 } ? chosen.Balance.ToString("0.00") : "0.00";
            await RefreshPosStateAsync();
            orderBox.ItemsSource = orders.ToArray();
            var matchingOrder = preferredOrderId is null
                ? orders.FirstOrDefault(x => x.ClientOrderId == orderIdBox.Text)
                : orders.FirstOrDefault(x => x.Id == preferredOrderId);
            orderBox.SelectedItem = matchingOrder ?? orders.FirstOrDefault();
            UpdateOrderList();
        }
        verifyBalance.Click += async (_, _) =>
        {
            verifyBalance.IsEnabled = false;
            try
            {
                await RefreshCashierStateAsync();
                cashierStatus.Text = "Balances verified directly from local accounting. No page refresh needed.";
            }
            catch (Exception ex)
            {
                cashierStatus.Text = "Could not verify balances: " + ex.Message;
                App.LogRecoverableException("pos-live-balance", ex);
            }
            finally { verifyBalance.IsEnabled = true; }
        };

        serve.Click += async (_, _) => { try { if (orderBox.SelectedItem is not OrderChoice o) throw new InvalidOperationException("Select an order."); await DesktopUiWork.RunAsync(() => workflow.ServeOrderAsync(o.Id)); await RefreshCashierStateAsync(o.Id); cashierStatus.Text = "Order served; recipe inventory consumption recorded."; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, cashierStatus.Text); } catch (Exception ex) { cashierStatus.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, cashierStatus.Text); } };
        issue.Click += async (_, _) => { try { if (orderBox.SelectedItem is not OrderChoice o) throw new InvalidOperationException("Select an order."); await DesktopUiWork.RunAsync(() => workflow.CreateBillAsync(o.Id)); await RefreshCashierStateAsync(o.Id); cashierStatus.Text = "Bill issued. Payment is ready in this dialog."; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, cashierStatus.Text); } catch (Exception ex) { cashierStatus.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, cashierStatus.Text); } };
        discount.Click += async (_, _) => { try { if (billBox.SelectedItem is not BillChoice b) throw new InvalidOperationException("Select a bill."); if (!decimal.TryParse(discountBox.Text, out var value)) throw new InvalidOperationException("Enter discount percent."); await DesktopUiWork.RunAsync(() => workflow.ApplyDiscountAsync(b.Id, "percent", value, "Desktop cashier discount")); await RefreshCashierStateAsync(); cashierStatus.Text = "Discount applied."; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, cashierStatus.Text); } catch (Exception ex) { cashierStatus.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, cashierStatus.Text); } };
        split.Click += async (_, _) => { try { if (billBox.SelectedItem is not BillChoice b) throw new InvalidOperationException("Select a bill."); if (!int.TryParse(splitCountBox.Text, out var count)) throw new InvalidOperationException("Enter split count."); await DesktopUiWork.RunAsync(() => workflow.CreateEqualSplitsAsync(b.Id, count)); await RefreshCashierStateAsync(); cashierStatus.Text = $"Bill split into {count} parts."; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, cashierStatus.Text); } catch (Exception ex) { cashierStatus.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, cashierStatus.Text); } };
        // Each click creates a new payment intent. Permit the next partial
        // payment only after reading back committed bill balances; never
        // require a page refresh or enable two concurrent submissions.
        var paymentSubmission = new DesktopSubmissionGate();
        var paymentBusy = false;
        var paymentNeedsManualVerification = false;
        pay.Click += async (_, _) =>
        {
            if (!paymentSubmission.TryBegin()) return;
            pay.IsEnabled = false;
            paymentBusy = true;
            var sentToDatabase = false;
            var verified = false;
            try
            {
                if (billBox.SelectedItem is not BillChoice bill)
                    throw new InvalidOperationException("Select an outstanding bill.");
                if (!decimal.TryParse(amountBox.Text, out var amount) ||
                    amount <= 0m || amount > bill.Balance)
                    throw new InvalidOperationException("Enter an amount greater than zero and no more than the balance due.");
                var method = "cash";
                cashierStatus.Text = "Verifying the local cashier session…";
                var cashierContext = await DesktopUiWork.RunAsync(async () =>
                {
                    var actor = await workflow.CurrentPrincipalAsync().ConfigureAwait(false);
                    await using var db = factory.Create();
                    var branchId = await db.Bills.AsNoTracking()
                        .Where(x => x.Id == bill.Id)
                        .Select(x => x.BranchId)
                        .SingleAsync().ConfigureAwait(false);
                    // SQLite cannot order DateTimeOffset in SQL; materialize
                    // the small active-session set before sorting by time.
                    var matchingSessions = await db.CashierSessions.AsNoTracking()
                        .Where(x => x.BranchId == branchId &&
                                    x.CashierUserId == actor.UserId &&
                                    x.Status == "open")
                        .Select(x => new { x.Id, x.OpenedAt })
                        .ToListAsync().ConfigureAwait(false);
                    var existingSessionId = matchingSessions
                        .OrderByDescending(x => x.OpenedAt)
                        .Select(x => x.Id).FirstOrDefault();
                    return (BranchId: branchId, SessionId: existingSessionId);
                });
                // First checkout creates the signed-in cashier's shift with
                // zero opening float if none exists. Manual float counts and
                // closing remain in the management workflow, not this modal.
                var sessionId = cashierContext.SessionId;
                if (sessionId is null)
                {
                    cashierStatus.Text = "Starting the current cashier session…";
                    var created = await DesktopUiWork.RunAsync(() =>
                        workflow.OpenCashierSessionAsync(cashierContext.BranchId, 0m));
                    sessionId = System.Text.Json.JsonSerializer
                        .SerializeToElement(created).GetProperty("id").GetString()
                        ?? throw new InvalidOperationException("Cashier session ID missing.");
                }
                cashierStatus.Text = "Posting payment securely. Do not close or press Pay again…";
                sentToDatabase = true;
                await DesktopUiWork.RunAsync(() =>
                    workflow.AddPaymentAsync(bill.Id, sessionId, amount, method));
                await RefreshCashierStateAsync();
                verified = true;
                paymentNeedsManualVerification = false;
                cashierStatus.Text = bills.Any(x => x.Id == bill.Id)
                    ? $"AFN {amount:N2} posted. Updated outstanding balance is shown above; additional payment is available."
                    : $"AFN {amount:N2} posted. Bill fully settled; the next outstanding bill is now selectable.";
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, cashierStatus.Text);
            }
            catch (Exception ex)
            {
                App.LogRecoverableException("pos-payment-post", ex);
                paymentNeedsManualVerification = sentToDatabase;
                cashierStatus.Text = sentToDatabase
                    ? "Payment result could not be verified. Do not pay twice. Check the cash/transaction ledger before retrying. " + ex.Message
                    : ex.Message;
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, cashierStatus.Text);
            }
            finally
            {
                paymentBusy = false;
                paymentSubmission.Finish(lockOnSuccess: paymentNeedsManualVerification || (sentToDatabase && !verified));
                // Successful readback permits a new payment on the remaining
                // balance without forcing a page reload.
                pay.IsEnabled = !paymentNeedsManualVerification &&
                    (billBox.SelectedItem is BillChoice selected && selected.Balance > 0);
            }
        };
        receipt.Click += async (_, _) => { try { if (billBox.SelectedItem is not BillChoice b) throw new InvalidOperationException("Select a bill."); await DesktopUiWork.RunAsync(() => workflow.QueueReceiptAsync(b.Id)); cashierStatus.Text = "Receipt queued for the configured restaurant receipt printer."; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, cashierStatus.Text); } catch (Exception ex) { cashierStatus.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, cashierStatus.Text); } };

        // Register mutation handlers after all POS search/list/cashier controls exist.
        open.Click += async (_, _) =>
        {
            open.IsEnabled = false; // prevent duplicate orders on rapid clicks
            try
            {
                var serviceType = serviceTypeBox.SelectedItem?.ToString() ?? "dine_in";
                if (!int.TryParse(guestBox.Text, out var guests) || guests < 1)
                    throw new InvalidOperationException("Enter a positive number of guests.");

                string? tableId = null;
                var branchId = string.Empty;
                if (serviceType == "dine_in")
                {
                    if (tableBox.SelectedItem is not Choice table)
                        throw new InvalidOperationException("Select a table for dine-in.");
                    tableId = table.Id;
                }
                else
                {
                    if (orderBranchBox.SelectedItem is not Choice branch)
                        throw new InvalidOperationException("Select a branch for this service type.");
                    branchId = branch.Id;
                }

                var reference = string.IsNullOrWhiteSpace(serviceReferenceBox.Text)
                    ? null : serviceReferenceBox.Text.Trim();
                orderIdBox.Text = await DesktopUiWork.RunAsync(() => workflow.OpenOrderAsync(
                    serviceType, tableId, branchId, reference, guests));

                await RefreshPosStateAsync();
                status.Text = "Order opened. Add dishes and send KOT using the top toolbar.";
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, status.Text);
                if (firstPhotoAwaitingTable && menuBox.SelectedItem is MenuChoice)
                {
                    firstPhotoAwaitingTable = false;
                    add.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                }
            }
            catch (Exception ex) { status.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message); }
            finally { open.IsEnabled = true; }
        };
        menuBox.SelectionChanged += (_, _) =>
        {
            if (menuBox.SelectedItem is MenuChoice selected)
            {
                modifiersBox.ItemsSource = modifierChoices.Where(x => x.MenuItemId == selected.Id).ToList();
                menuPrice.Text = $"Unit price: AFN {selected.Price:N2} (modifier charges may apply)";
            }
            else
            {
                modifiersBox.ItemsSource = Array.Empty<ModifierChoice>();
                menuPrice.Text = menu.Count == 0
                    ? "No available menu items. Synchronize your restaurant catalog before taking items."
                    : "Select an available menu item";
            }
        };

        add.Click += async (_, _) =>
        {
            add.IsEnabled = false; // prevent accidental duplicate lines
            try
            {
                // The first menu tap can open an order automatically. For
                // occupied tables, resume via Orders instead of creating a
                // conflicting second order.
                if (string.IsNullOrWhiteSpace(orderIdBox.Text))
                {
                    var serviceType = serviceTypeBox.SelectedItem?.ToString() ?? "dine_in";
                    var guests = int.TryParse(guestBox.Text, out var chosenGuests) ? chosenGuests : 1;
                    if (guests < 1 || guests > 100)
                        throw new InvalidOperationException("Guests must be between 1 and 100.");
                    string? tableId = null;
                    var branchId = string.Empty;
                    if (serviceType == "dine_in")
                    {
                        if (tableBox.SelectedItem is not Choice selectedTable)
                            throw new InvalidOperationException("Choose a table before adding the first dish.");
                        tableId = selectedTable.Id;
                    }
                    else
                    {
                        if (orderBranchBox.SelectedItem is not Choice branch)
                            throw new InvalidOperationException("Choose a branch before adding the first dish.");
                        branchId = branch.Id;
                    }
                    var refText = serviceReferenceBox.Text.Trim();
                    orderIdBox.Text = await DesktopUiWork.RunAsync(() =>
                        workflow.OpenOrderAsync(serviceType, tableId, branchId,
                            string.IsNullOrWhiteSpace(refText) ? null : refText, guests));
                    await RefreshPosStateAsync();
                }
                if (menuBox.SelectedItem is not MenuChoice item) throw new InvalidOperationException("Select a menu item.");
                if (!int.TryParse(qtyBox.Text, out var qty) || qty < 1)
                    throw new InvalidOperationException("Enter a positive item quantity.");

                int? seat = int.TryParse(seatBox.Text, out var parsedSeat) ? parsedSeat : null;
                int? course = int.TryParse(courseBox.Text, out var parsedCourse) ? parsedCourse : null;
                var selectedModifiers = modifiersBox.SelectedItems
                    .Cast<ModifierChoice>()
                    .Select(x => x.OptionId)
                    .ToArray();

                var clientOrderId = orderIdBox.Text;
                var notes = string.IsNullOrWhiteSpace(itemNotesBox.Text) ? null : itemNotesBox.Text.Trim();
                var courseName = string.IsNullOrWhiteSpace(courseNameBox.Text) ? null : courseNameBox.Text.Trim();
                var held = heldBox.IsChecked == true;
                var priority = rushBox.IsChecked == true ? "rush" : "normal";
                var allergy = string.IsNullOrWhiteSpace(allergyBox.Text) ? null : allergyBox.Text.Trim();
                var kitchenInstructions = string.IsNullOrWhiteSpace(kitchenInstructionsBox.Text)
                    ? null : kitchenInstructionsBox.Text.Trim();
                var clientLineId = await DesktopUiWork.RunAsync(() => workflow.AddItemDetailedAsync(
                    clientOrderId, item.Id, qty, notes, seat, course, courseName, held, priority,
                    allergy, kitchenInstructions, selectedModifiers));

                await RefreshPosStateAsync();
                lineBox.SelectedItem = orderLines.FirstOrDefault(x => x.ClientLineId == clientLineId);
                qtyBox.Text = "1";
                itemNotesBox.Clear();
                kitchenInstructionsBox.Clear();
                allergyBox.Clear();
                modifiersBox.UnselectAll();
                status.Text = $"{qty} × {item.Name} added. Add other dishes or press Send KOT (F9).";
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, status.Text);
                closeQuickOrderSetup?.Invoke();
            }
            catch (Exception ex) { status.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message); }
            finally { add.IsEnabled = true; }
        };
        // Prevent overlapping KOT submissions without blocking legitimate
        // later KOT rounds after the first send completes.
        var kotSubmission = new DesktopSubmissionGate();
        submit.Click += async (_, _) =>
        {
            if (!kotSubmission.TryBegin()) return;
            submit.IsEnabled = false;
            try
            {
                if (string.IsNullOrWhiteSpace(orderIdBox.Text))
                    throw new InvalidOperationException("Open an order first.");
                var clientOrderId = orderIdBox.Text;
                await DesktopUiWork.RunAsync(() => workflow.SendKotAsync(clientOrderId));
                await RefreshPosStateAsync();
                status.Text = "New KOT round sent. Previously sent items were not duplicated.";
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, status.Text);
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message);
            }
            finally
            {
                kotSubmission.Finish(lockOnSuccess: false);
                submit.IsEnabled = true;
            }
        };

        fireCourse.Click += async (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(orderIdBox.Text)) throw new InvalidOperationException("Open/select an order first.");
                if (!int.TryParse(fireCourseBox.Text, out var courseNumber)) throw new InvalidOperationException("Enter a course number.");
                var clientOrderId = orderIdBox.Text;
                await DesktopUiWork.RunAsync(() => workflow.FireCourseAsync(clientOrderId, courseNumber));
                await RefreshPosStateAsync();
                status.Text = $"Course {courseNumber} fired as a new KOT round.";
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, status.Text);
            }
            catch (Exception ex) { status.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message); }
        };

        voidLine.Click += async (_, _) =>
        {
            try
            {
                if (lineBox.SelectedItem is not OrderLineChoice line) throw new InvalidOperationException("Select an order line.");
                if (string.IsNullOrWhiteSpace(voidReasonBox.Text)) throw new InvalidOperationException("Enter a void reason.");
                if (!ConfirmOperationalChange(
                    $"Void {line.ItemName} on order {line.ClientOrderId}? This will be recorded in the audit history.",
                    "Confirm order line void"))
                    return;
                voidLine.IsEnabled = false;
                try
                {
                    var reason = voidReasonBox.Text.Trim();
                    await DesktopUiWork.RunAsync(() => workflow.VoidOrderItemAsync(line.ClientOrderId, line.ClientLineId, reason));
                    await RefreshPosStateAsync();
                }
                finally
                {
                    voidLine.IsEnabled = true;
                }
                status.Text = $"{line.ItemName} voided. Reserved stock was released when production had not started.";
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, status.Text);
            }
            catch (Exception ex) { status.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message); }
        };

        cancelOrder.Click += async (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(orderIdBox.Text)) throw new InvalidOperationException("Open/select an order first.");
                if (string.IsNullOrWhiteSpace(voidReasonBox.Text)) throw new InvalidOperationException("Enter a cancellation reason.");
                if (!ConfirmOperationalChange(
                    $"Cancel order {orderIdBox.Text}? Kitchen history and audit records will be preserved.",
                    "Confirm order cancellation"))
                    return;
                cancelOrder.IsEnabled = false;
                try
                {
                    var clientOrderId = orderIdBox.Text;
                    var reason = voidReasonBox.Text.Trim();
                    await DesktopUiWork.RunAsync(() => workflow.CancelOrderAsync(clientOrderId, reason));
                    orderIdBox.Clear();
                    await RefreshPosStateAsync();
                }
                finally
                {
                    cancelOrder.IsEnabled = true;
                }
                status.Text = "Order cancelled with audit history preserved.";
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, status.Text);
            }
            catch (Exception ex) { status.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message); }
        };

        void UpdateOrderSummary()
        {
            if (string.IsNullOrWhiteSpace(orderIdBox.Text))
            {
                selectionSummary.Text = "No order selected · Open an order or choose one from Active orders.";
                lineBox.ItemsSource = Array.Empty<OrderLineChoice>();
                currentOrderLines.ItemsSource = Array.Empty<OrderLineChoice>();
                currentOrderTotal.Text = "Choose a table and open an order to begin.";
                return;
            }

            var order = orders.FirstOrDefault(x => x.ClientOrderId == orderIdBox.Text);
            var lines = orderLines.Where(x => x.ClientOrderId == orderIdBox.Text).ToArray();
            var awaiting = lines.Count(x => x.Status is "pending" or "held");
            selectionSummary.Text = order is null
                ? $"Order {orderIdBox.Text} · {lines.Length} line(s) · {awaiting} awaiting KOT"
                : $"{order.ServiceType.Replace('_', ' ')} · {order.Guests} guest(s) · {order.Status} · AFN {order.Total:N2} · " +
                  $"{lines.Length} line(s), {awaiting} awaiting KOT";
            lineBox.ItemsSource = lines;
            currentOrderLines.ItemsSource = lines;
            currentOrderTotal.Text = order is null
                ? $"{lines.Length} line(s) on order {orderIdBox.Text}."
                : $"{lines.Sum(x => x.Quantity)} items · AFN {order.Total:N2} · {order.Status} · " +
                  (order.Status == "billed" ? "Collect payment to release table." : "Send KOT when ready.");
        }

        void UpdateOrderList()
        {
            var term = orderSearchBox.Text.Trim();
            var statusValue = orderStatusFilter.SelectedItem?.ToString() ?? "All statuses";
            var shown = orders.Where(x =>
                (statusValue == "All statuses" || x.Status == statusValue) &&
                (x.ClientOrderId.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                 (x.Waiter?.Contains(term, StringComparison.OrdinalIgnoreCase) == true) ||
                 x.ServiceType.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
            grid.ItemsSource = shown;
            grid.MinHeight = shown.Length == 0 ? 110 : 300;
            emptyOrderHint.Visibility = shown.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            orderOverview.Text = $"{orders.Count(x => x.Status is not ("closed" or "cancelled"))} open orders  ·  " +
                $"{bills.Count} open bills  ·  {sessions.Count} cashier sessions  ·  {shown.Length} shown";
            grid.SelectedItem = shown.FirstOrDefault(x => x.ClientOrderId == orderIdBox.Text);
        }

        void FocusOrder(OrderChoice order)
        {
            if (order.Status is "closed" or "cancelled" or "billed" or "paid")
            {
                status.Text = $"{order.ClientOrderId} is {order.Status} and cannot be edited from this order-entry form.";
                return;
            }
            orderIdBox.Text = order.ClientOrderId;
            orderBox.SelectedItem = orders.FirstOrDefault(x => x.Id == order.Id);
            UpdateOrderSummary();
            status.Text = $"Now editing {order.ClientOrderId}. Add items or send an unsent KOT round.";
        }

        grid.SelectionChanged += (_, _) =>
        {
            if (grid.SelectedItem is OrderChoice selected)
                FocusOrder(selected);
        };
        useSelected.Click += (_, _) =>
        {
            if (grid.SelectedItem is OrderChoice selected)
                FocusOrder(selected);
            else
                status.Text = "Select an active order row first.";
        };
        startOrderFromList.Click += (_, _) =>
        {
            orderIdBox.Clear();
            grid.SelectedItem = null;
            UpdateOrderSummary();
            formCard.BringIntoView();
            serviceTypeBox.Focus();
            status.Text = "New order entry is ready; no existing order was cancelled.";
        };
        clearSelection.Click += (_, _) =>
        {
            orderIdBox.Clear();
            grid.SelectedItem = null;
            lineBox.ItemsSource = Array.Empty<OrderLineChoice>();
            UpdateOrderSummary();
            status.Text = "Ready for a new order. This does not cancel the previous one.";
        };
        orderSearchBox.TextChanged += (_, _) => UpdateOrderList();
        orderStatusFilter.SelectionChanged += (_, _) => UpdateOrderList();
        UpdateOrderList();

        async Task RefreshPosStateAsync()
        {
            // Data changes are already committed by the local workflow service.
            // A display refresh must not misreport a successful operation as failed.
            try
            {
                var focused = orderIdBox.Text;
                var latestData = await DesktopUiWork.RunAsync(async () =>
                {
                    await using var freshDb = factory.Create();
                    var latestOrders = (await freshDb.Orders.AsNoTracking()
                        .Where(x => x.Status != "closed")
                        .Select(x => new
                        {
                            Row = new OrderChoice(x.Id, x.ClientOrderId, x.ServiceType, x.WaiterName,
                                x.Status, x.GuestCount, x.Total, x.DisplayNumber),
                            x.UpdatedAtUtc,
                        }).ToListAsync())
                    .OrderByDescending(x => x.UpdatedAtUtc).Take(100).Select(x => x.Row).ToArray();
                var latestLines = (await (
                        from line in freshDb.OrderItems.AsNoTracking()
                        join order in freshDb.Orders.AsNoTracking() on line.OrderId equals order.Id
                        where order.Status != "closed" && order.Status != "cancelled" &&
                              line.Status != "voided" && line.Status != "cancelled"
                        select new
                        {
                            Row = new OrderLineChoice(order.ClientOrderId, line.ClientLineId,
                                line.ItemName, line.Quantity, line.Status, line.RoundNumber),
                            line.CreatedAtUtc,
                        }).ToListAsync())
                    .OrderBy(x => x.CreatedAtUtc).Select(x => x.Row).ToArray();
                    return (Orders: latestOrders, Lines: latestLines);
                });
                var latestOrders = latestData.Orders;
                var latestLines = latestData.Lines;
                orders.Clear();
                orders.AddRange(latestOrders);
                orderLines.Clear();
                orderLines.AddRange(latestLines);
                var selectedStatus = orderStatusFilter.SelectedItem?.ToString() ?? "All statuses";
                var availableStatuses = new[] { "All statuses" }.Concat(
                    orders.Select(x => x.Status).Distinct(StringComparer.OrdinalIgnoreCase)).ToArray();
                orderStatusFilter.ItemsSource = availableStatuses;
                orderStatusFilter.SelectedItem = availableStatuses.Contains(selectedStatus)
                    ? selectedStatus : "All statuses";
                orderBox.ItemsSource = orders;
                UpdateOrderSummary();
                UpdateOrderList();
                if (!string.IsNullOrWhiteSpace(focused))
                    orderBox.SelectedItem = orders.FirstOrDefault(x => x.ClientOrderId == focused);
            }
            catch (Exception ex)
            {
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Warning,
                    "The action succeeded, but the order list could not refresh: " + ex.Message);
            }
        }


        // The modal layout is shared by orders, item configuration and
        // billing. Reparent the original controls, never clone workflow
        // buttons or bypass role/permission checks.
        var operatorSession = await new WindowsSessionStore().LoadAsync();
        var role = operatorSession?.User.Role?.Trim().ToLowerInvariant();
        Border? cashierCard = role is RestaurantRoles.Owner or RestaurantRoles.Manager or RestaurantRoles.Cashier
            ? Card(cashier) : null;

        form.Children.Remove(photoCatalog);
        form.Children.Remove(menuSearchBox);
        var oldSearchLabel = form.Children.OfType<TextBlock>()
            .FirstOrDefault(label => label.Text == "Search menu");
        if (oldSearchLabel is not null) form.Children.Remove(oldSearchLabel);
        // Keep the live action feedback in the ticket, not at the bottom of
        // the long order-setup form.
        form.Children.Remove(status);
        form.Children.Remove(selectionSummary);

        // A short, three-tab editor is easier to use than the previous
        // vertically endless POS form. Existing controls and click handlers
        // are moved, not duplicated or recreated.
        var originalSections = form.Children.Cast<UIElement>().ToArray();
        var itemsIndex = Array.FindIndex(originalSections,
            x => x is TextBlock text && text.Text == "3 · ADD ITEMS");
        var adjustmentsIndex = Array.FindIndex(originalSections,
            x => x is TextBlock text && text.Text == "4 · KITCHEN & ORDER ADJUSTMENTS");
        if (itemsIndex <= 0 || adjustmentsIndex <= itemsIndex)
            throw new InvalidOperationException("POS editor sections could not be organized.");
        form.Children.Clear();
        static StackPanel PosTabSection(IEnumerable<UIElement> elements)
        {
            var panel = new StackPanel { Margin = new Thickness(3, 12, 3, 4) };
            foreach (var element in elements) panel.Children.Add(element);
            return panel;
        }
        var posEditorTabs = new TabControl
        {
            Name = "PosEditorTabs", SelectedIndex = 0,
            Margin = new Thickness(0, 0, 0, 6),
        };
        posEditorTabs.Items.Add(new TabItem
        {
            Header = "1 · Order setup",
            Content = PosTabSection(originalSections.Take(itemsIndex)),
        });
        posEditorTabs.Items.Add(new TabItem
        {
            Header = "2 · Add & customize",
            Content = PosTabSection(originalSections.Skip(itemsIndex).Take(adjustmentsIndex - itemsIndex)),
        });
        posEditorTabs.Items.Add(new TabItem
        {
            Header = "3 · KOT actions",
            Content = PosTabSection(originalSections.Skip(adjustmentsIndex)),
        });
        form.Children.Add(posEditorTabs);

        root.Children.Remove(formCard);
        root.Children.Remove(ordersCard);
        root.RowDefinitions.Clear();
        root.ColumnDefinitions.Clear();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Place ALL frequent POS actions in one compact top toolbar.
        // Keep the full width below for the photo menu; the order ticket
        // remains available in a modal rather than occupying a permanent rail.
        var newOrderModal = Button("+ New order");
        newOrderModal.Name = "PosOpenNewOrderModal";
        newOrderModal.ToolTip = "F2 · Open a new order";
        var activeOrdersModal = Button("Orders");
        activeOrdersModal.Name = "PosOpenActiveOrdersModal";
        activeOrdersModal.ToolTip = "F3 · Find or resume an order";
        activeOrdersModal.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        var viewTicketModal = Button("View ticket");
        viewTicketModal.Name = "PosOpenTicketModal";
        viewTicketModal.ToolTip = "F4 · Inspect the current ticket";
        viewTicketModal.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        var editItemsModal = Button("Item options");
        editItemsModal.Name = "PosOpenItemOptionsModal";
        editItemsModal.ToolTip = "F6 · Item modifiers and kitchen notes";
        editItemsModal.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        var addGuestsModal = Button("+ Guests");
        addGuestsModal.Name = "PosOpenAddGuestsModal";
        addGuestsModal.ToolTip = "Add people to the same table and existing KOT order";
        addGuestsModal.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        var kitchenModal = Button("KOT actions");
        kitchenModal.Name = "PosOpenKitchenModal";
        kitchenModal.ToolTip = "F7 · Kitchen changes, course firing and voids";
        kitchenModal.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        var billingModal = Button("Payments");
        billingModal.Name = "PosOpenPaymentModal";
        billingModal.ToolTip = "F8 · Payments and billing";
        billingModal.MinHeight = 42;

        // Table-first ordering: pick a floor tile and everything else — new
        // order, dishes, guests, kitchen round and checkout — happens in the
        // table modal. The legacy controls remain reachable for keyboards and
        // automation (F2–F8) but no longer occupy the toolbar.
        async Task OpenTableOrderAsync(TableChoice table, TableOrderChoice? order, string? quickAddItemId = null)
        {
            var checkout = await OpenTableOrderModalAsync(
                table, "dine_in", order, selectionSummary,
                allowCheckout: true, refreshParent: false, quickAddItemId: quickAddItemId);
            if (checkout && cashierCard is not null)
            {
                billingModal.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
            else
            {
                await RefreshFloorAsync();
            }
        }

        async Task RefreshFloorAsync()
        {
            try
            {
                var refreshed = await DesktopUiWork.RunAsync(async () =>
                {
                    await using var db = factory.Create();
                    var refreshedTables = await (from table in db.DiningTables.AsNoTracking()
                                                 join area in db.DiningAreas.AsNoTracking() on table.DiningAreaId equals area.Id
                                                 join branch in db.Branches.AsNoTracking() on area.BranchId equals branch.Id
                                                 where table.IsActive && area.IsActive && branch.IsActive
                                                 orderby area.SortOrder, table.Name
                                                 select new TableChoice(table.Id, branch.Name + " · " + area.Name,
                                                     table.Code, table.Name, table.Capacity, table.Status)).ToListAsync();
                    var refreshedOrders = (await db.Orders.AsNoTracking()
                            .Where(x => x.ServiceType == "dine_in" && x.Status != "closed" && x.Status != "cancelled")
                            .Select(x => new
                            {
                                Row = new TableOrderChoice(x.Id, x.ClientOrderId, x.DiningTableId, "", x.Status, x.Total, x.GuestCount, x.DisplayNumber),
                                x.UpdatedAtUtc,
                            })
                            .ToListAsync())
                        .OrderByDescending(x => x.UpdatedAtUtc)
                        .Select(x => x.Row).ToArray();
                    return (Tables: refreshedTables, Orders: refreshedOrders);
                });
                tablesNow = refreshed.Tables;
                ordersNow = refreshed.Orders;
                RenderFloorTiles();
            }
            catch (Exception ex)
            {
                selectionSummary.Text = "Could not refresh the floor: " + ex.Message;
            }
        }

        void RenderFloorTiles()
        {
            posTableStrip.Children.Clear();
            foreach (var table in tablesNow)
            {
                posTableStrip.Children.Add(BuildPosTableTile(table, ordersNow.FirstOrDefault(x => x.TableId == table.Id)));
            }
        }

        Button BuildPosTableTile(TableChoice table, TableOrderChoice? order)
        {
            var stateBrush = table.Status switch
            {
                "available" => "SuccessBrush",
                "occupied" => "BrandPrimaryBrush",
                _ => "WarningBrush",
            };
            var title = new TextBlock
            {
                Text = table.Name, FontSize = 15, FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            var meta = new TextBlock
            {
                Text = $"Table {table.Code} · {table.Capacity} seats",
                FontSize = 10.5, Margin = new Thickness(0, 3, 0, 5),
            };
            meta.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            var state = new TextBlock
            {
                Text = table.Status.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.Bold,
            };
            state.SetResourceReference(TextBlock.ForegroundProperty, stateBrush);
            var stack = new StackPanel { Margin = new Thickness(11) };
            stack.Children.Add(title);
            stack.Children.Add(meta);
            stack.Children.Add(state);
            if (order is not null)
            {
                var caption = new TextBlock
                {
                    Text = $"{order.ShortNumber} · {order.Guests} guest(s) · AFN {order.Total:N0}",
                    FontSize = 10, Margin = new Thickness(0, 4, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                caption.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                stack.Children.Add(caption);
            }
            var surface = new Border
            {
                Width = 172, MinHeight = 96, CornerRadius = new CornerRadius(13),
                BorderThickness = new Thickness(1), Child = stack,
            };
            surface.SetResourceReference(Border.BackgroundProperty, "FloorTileBackgroundBrush");
            surface.SetResourceReference(Border.BorderBrushProperty,
                table.Id == selectedTable?.Id ? "BrandPrimaryBrush" : "FloorTileBorderBrush");
            var tile = new Button
            {
                Content = surface, Padding = new Thickness(0), BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Margin = new Thickness(0, 0, 9, 9),
                ToolTip = order is null
                    ? "Tap to start a new order at this table"
                    : "Tap to open this table's order, guests and checkout",
            };
            tile.Click += async (_, _) =>
            {
                selectedTable = table;
                RenderFloorTiles();
                await OpenTableOrderAsync(table, order);
            };
            return tile;
        }

        var quickActions = new WrapPanel
        {
            Name = "PosTopActions", Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
        };
        quickActions.Children.Add(posTableStrip);

        async Task OpenServiceOrderAsync(string type)
        {
            var checkout = await OpenTableOrderModalAsync(
                null, type, null, selectionSummary,
                allowCheckout: true, refreshParent: false, branches: branches);
            if (checkout && cashierCard is not null)
            {
                billingModal.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
            else
            {
                await RefreshFloorAsync();
            }
        }

        var takeaway = Button("Takeaway");
        takeaway.Name = "PosTakeawayButton";
        takeaway.ToolTip = "New takeaway order without a table";
        var deliveryOrder = Button("Delivery");
        deliveryOrder.Name = "PosDeliveryButton";
        deliveryOrder.ToolTip = "New delivery order without a table";
        foreach (var button in new[] { takeaway, deliveryOrder })
        {
            button.MinHeight = 40;
            button.MinWidth = 124;
            button.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        }
        takeaway.Click += async (_, _) => await OpenServiceOrderAsync("takeaway");
        deliveryOrder.Click += async (_, _) => await OpenServiceOrderAsync("delivery");
        quickActions.Children.Add(takeaway);
        quickActions.Children.Add(deliveryOrder);
        if (cashierCard is not null)
        {
            // Pay button stays visible: serve, bill and collect payment here.
            billingModal.Visibility = Visibility.Visible;
            billingModal.MinHeight = 40;
            quickActions.Children.Add(billingModal);
        }
        // Keys and Full screen belong to the workstation action row, right of
        // the payment buttons; the top menu bar stays navigation-only.
        var (posKeys, posFullScreen) = BuildWorkspaceChromeButtons();
        quickActions.Children.Add(posKeys);
        quickActions.Children.Add(posFullScreen);
        foreach (var button in new[] { newOrderModal, activeOrdersModal, viewTicketModal, editItemsModal, addGuestsModal, kitchenModal })
        {
            button.Visibility = Visibility.Collapsed;
            quickActions.Children.Add(button);
        }
        kitchenActions.Children.Remove(submit);
        submit.Visibility = Visibility.Collapsed;
        quickActions.Children.Add(submit);
        openDishForSelectedTable = itemId =>
        {
            var table = selectedTable;
            if (table is null)
            {
                selectionSummary.Text = "Tap a table tile above to start or resume its order.";
                return;
            }
            _ = OpenTableOrderAsync(table, ordersNow.FirstOrDefault(x => x.TableId == table.Id), itemId);
        };
        RenderFloorTiles();

        // Current-order context is visible even while the ticket is closed.
        var topContext = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 242, 247, 253)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(190, 225, 233, 244)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(0, 2, 0, 6),
            Child = selectionSummary,
        };
        selectionSummary.Margin = new Thickness(0);
        selectionSummary.MaxHeight = 40;
        var topToolbar = new StackPanel
        {
            Name = "PosFullscreenTopToolbar",
            Margin = new Thickness(0, 0, 0, 8),
        };
        topToolbar.Children.Add(quickActions);
        topToolbar.Children.Add(topContext);
        Grid.SetRow(topToolbar, 0);
        root.Children.Add(topToolbar);

        var catalogHeader = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        catalogHeader.Children.Add(menuSearchBox);
        var catalog = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(catalogHeader, Dock.Top);
        catalog.Children.Add(catalogHeader);
        catalog.Children.Add(new ScrollViewer
        {
            Content = photoCatalog,
            Name = "PosFullscreenPhotoGrid",
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        var menuPane = Card(catalog);
        menuPane.Name = "PosPhotoFirstPane";
        Grid.SetRow(menuPane, 1);
        root.Children.Add(menuPane);

        // Only display ticket details when needed so the photo menu always
        // retains the maximum available viewport.
        orderListPanel.Children.Remove(currentOrderLines);
        orderListPanel.Children.Remove(currentOrderTotal);
        var ticketPanel = new StackPanel { Name = "PosFullscreenOrderTicket" };
        ticketPanel.Children.Add(Header("CURRENT TICKET", "Confirm dishes and totals before sending the kitchen round."));
        ticketPanel.Children.Add(currentOrderLines);
        ticketPanel.Children.Add(currentOrderTotal);
        ticketPanel.Children.Add(status);
        var ticketCard = Card(ticketPanel);
        root.Margin = new Thickness(0);
        root.MinHeight = 0;

        // iOS-inspired frosted modal: blur ONLY the background workspace,
        // then tint/dim beneath an independently scrolling focusable dialog.
        var posHost = new Grid { Name = "FullscreenPosWorkspace", ClipToBounds = true };
        posHost.Children.Add(WorkspaceFrostedSurface.Wrap(root));
        var modalOverlay = new Grid
        {
            Name = "PosGlassModalOverlay",
            Background = new SolidColorBrush(Color.FromArgb(135, 12, 23, 42)),
            Visibility = Visibility.Collapsed,
        };
        Panel.SetZIndex(modalOverlay, 40);
        var modalCard = new Border
        {
            Name = "PosGlassModalCard",
            Background = new SolidColorBrush(Color.FromArgb(245, 248, 251, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(225, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(0),
            Width = 860,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(8, 22, 44),
                ShadowDepth = 4, BlurRadius = 16, Opacity = 0.30,
            },
        };
        var dialogPreferredWidth = 860d;
        void FitModalToViewport()
        {
            var viewportWidth = modalOverlay.ActualWidth;
            var viewportHeight = modalOverlay.ActualHeight;
            if (viewportWidth < 1 || viewportHeight < 1) return;
            modalCard.MaxHeight = Math.Max(260, viewportHeight - 36);
            modalCard.MaxWidth = Math.Max(300, viewportWidth - 36);
            modalCard.Width = Math.Min(dialogPreferredWidth, Math.Max(300, viewportWidth - 36));
        }
        modalOverlay.SizeChanged += (_, _) => FitModalToViewport();
        var modalLayout = new Grid();
        modalLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        modalLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        modalLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var modalHeader = new Grid { Margin = new Thickness(24, 20, 24, 12) };
        modalHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modalHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var modalHeaderText = new StackPanel();
        var modalTitle = HeaderText("Order setup", 24, true);
        modalTitle.Name = "PosGlassModalTitle";
        var modalDescription = new TextBlock
        {
            Text = "Set the order and add dishes.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.SlateGray,
            Margin = new Thickness(0, 4, 0, 0),
        };
        modalHeaderText.Children.Add(modalTitle);
        modalHeaderText.Children.Add(modalDescription);
        modalHeader.Children.Add(modalHeaderText);
        var closeModalButton = Button("Close  ×");
        closeModalButton.Name = "PosGlassModalClose";
        closeModalButton.MinHeight = 38;
        closeModalButton.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        Grid.SetColumn(closeModalButton, 1);
        modalHeader.Children.Add(closeModalButton);
        modalLayout.Children.Add(modalHeader);
        var modalScroll = new ScrollViewer
        {
            Name = "PosModalIndependentScroll",
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(22, 0, 22, 12),
        };
        var guestsPanel = new StackPanel { Margin = new Thickness(8, 4, 8, 12) };
        guestsPanel.Children.Add(Header("Add guests", "Keep the same table, order and earlier KOT rounds."));
        var guestsHint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10) };
        guestsPanel.Children.Add(guestsHint);
        guestsPanel.Children.Add(Label("Additional guests arriving"));
        var additionalGuests = new TextBox
        {
            Name = "PosAdditionalGuestCount", Text = "1", Height = 42,
            Margin = new Thickness(0, 6, 0, 12),
        };
        guestsPanel.Children.Add(additionalGuests);
        var guestStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 10) };
        var saveGuests = Button("Add guests to this table");
        saveGuests.Name = "PosSaveAdditionalGuests";
        saveGuests.MinHeight = 46;
        guestsPanel.Children.Add(saveGuests);
        guestsPanel.Children.Add(guestStatus);
        var guestsCard = Card(guestsPanel);

        var modalBody = new StackPanel();
        modalBody.Children.Add(formCard);
        modalBody.Children.Add(ordersCard);
        modalBody.Children.Add(ticketCard);
        modalBody.Children.Add(guestsCard);
        if (cashierCard is not null) modalBody.Children.Add(cashierCard);
        modalScroll.Content = modalBody;
        Grid.SetRow(modalScroll, 1);
        modalLayout.Children.Add(modalScroll);
        var modalFooter = new TextBlock
        {
            Text = "Esc closes this panel  •  Data is stored locally  •  Orders and payments remain audited",
            Padding = new Thickness(24, 11, 24, 13),
            Foreground = System.Windows.Media.Brushes.SlateGray,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(modalFooter, 2);
        modalLayout.Children.Add(modalFooter);
        modalCard.Child = modalLayout;
        modalOverlay.Children.Add(modalCard);
        posHost.Children.Add(modalOverlay);

        var glassBlur = new System.Windows.Media.Effects.BlurEffect
        {
            Radius = 12, KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
            RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance,
        };
        IInputElement? previouslyFocused = null;
        bool CloseModal()
        {
            if (modalOverlay.Visibility != Visibility.Visible) return false;
            if (paymentBusy)
            {
                cashierStatus.Text = "Payment is being verified. Please wait.";
                return true;
            }
            modalOverlay.Visibility = Visibility.Collapsed;
            root.Effect = null;
            root.IsHitTestVisible = true;
            if (previouslyFocused is System.Windows.UIElement focusTarget)
                focusTarget.Focus();
            return true;
        }
        void OpenModal(string section)
        {
            if (paymentBusy) return;
            previouslyFocused = System.Windows.Input.Keyboard.FocusedElement;
            formCard.Visibility = section is "order" or "items" or "kitchen"
                ? Visibility.Visible : Visibility.Collapsed;
            posEditorTabs.SelectedIndex = section switch
            {
                "items" => 1, "kitchen" => 2, _ => 0,
            };
            ordersCard.Visibility = section == "orders" ? Visibility.Visible : Visibility.Collapsed;
            ticketCard.Visibility = section == "ticket" ? Visibility.Visible : Visibility.Collapsed;
            guestsCard.Visibility = section == "guests" ? Visibility.Visible : Visibility.Collapsed;
            if (cashierCard is not null)
                cashierCard.Visibility = section == "payment" ? Visibility.Visible : Visibility.Collapsed;
            advancedOrderActions.Visibility = section == "kitchen"
                ? Visibility.Visible : Visibility.Collapsed;
            toggleOrderActions.Content = section == "kitchen"
                ? "Hide course firing / void / cancel" : "Show course firing / void / cancel";
            modalTitle.Text = section switch
            {
                "orders" => "Active orders",
                "ticket" => "Current ticket",
                "guests" => "Add guests",
                "payment" => "Payments & billing",
                "items" => "Item options & KOT",
                "kitchen" => "Kitchen adjustments",
                _ => "New order & setup",
            };
            modalDescription.Text = section switch
            {
                "orders" => "Find a ticket, resume it, or start another order.",
                "ticket" => "Review the current dishes and total. Use Send KOT in the top toolbar.",
                "guests" => "Add people to the occupied table; existing orders and kitchen rounds stay intact.",
                "payment" => "One cashier · serve, bill and collect payment without changing screens.",
                "items" => "Adjust quantity, modifiers, and kitchen notes before sending a KOT.",
                "kitchen" => "Fire courses, void individual items, or cancel an order with an audit reason.",
                _ => "Choose service type and table, then open your order.",
            };
            dialogPreferredWidth = section switch
            {
                "payment" => 670d,
                "ticket" => 590d,
                "guests" => 560d,
                "order" => 700d,
                "orders" => 820d,
                "items" => 780d,
                "kitchen" => 740d,
                _ => 820d,
            };
            FitModalToViewport();
            modalScroll.ScrollToTop();
            root.Effect = glassBlur;
            root.IsHitTestVisible = false;
            modalOverlay.Visibility = Visibility.Visible;
            closeModalButton.Focus();
        }
        closeModalButton.Click += (_, _) => CloseModal();
        showQuickOrderSetup = () =>
        {
            OpenModal("order");
            guestBox.Focus();
        };
        closeQuickOrderSetup = () =>
        {
            if (modalOverlay.Visibility == Visibility.Visible && !paymentBusy)
                CloseModal();
        };
        newOrderModal.Click += (_, _) =>
        {
            firstPhotoAwaitingTable = false;
            OpenModal("order");
        };
        activeOrdersModal.Click += (_, _) => OpenModal("orders");
        viewTicketModal.Click += (_, _) => OpenModal("ticket");
        editItemsModal.Click += (_, _) => OpenModal("items");
        addGuestsModal.Click += (_, _) =>
        {
            var order = orders.FirstOrDefault(x => x.ClientOrderId == orderIdBox.Text);
            guestsHint.Text = order is null
                ? "Choose an active dine-in order from Orders before adding guests."
                : $"Current guests: {order.Guests}. New guests join the same order and table.";
            additionalGuests.Text = "1";
            guestStatus.Text = string.Empty;
            OpenModal("guests");
        };
        saveGuests.Click += async (_, _) =>
        {
            if (!saveGuests.IsEnabled) return;
            saveGuests.IsEnabled = false;
            try
            {
                var order = orders.FirstOrDefault(x => x.ClientOrderId == orderIdBox.Text)
                    ?? throw new InvalidOperationException("Select an active dine-in order first.");
                if (order.ServiceType != "dine_in" || order.Status is "closed" or "cancelled" or "billed")
                    throw new InvalidOperationException("Guests can only be added to an unbilled dine-in order.");
                if (!int.TryParse(additionalGuests.Text, out var joining) || joining < 1)
                    throw new InvalidOperationException("Enter a positive number of additional guests.");
                var newTotal = checked(order.Guests + joining);
                // The workflow validates actual table capacity and sends an
                // idempotent LAN guest count mutation.
                await DesktopUiWork.RunAsync(() =>
                    workflow.UpdateGuestsAsync(order.ClientOrderId, newTotal));
                await RefreshPosStateAsync();
                guestStatus.Text = $"Updated to {newTotal} guests. New dishes will go in the next KOT round.";
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, guestStatus.Text);
                CloseModal();
            }
            catch (Exception ex)
            {
                guestStatus.Text = ex.Message;
                App.LogRecoverableException("pos-add-guests", ex);
            }
            finally { saveGuests.IsEnabled = true; }
        };
        kitchenModal.Click += (_, _) => OpenModal("kitchen");
        if (cashierCard is not null)
            billingModal.Click += (_, _) => OpenModal("payment");
        useSelected.Click += (_, _) =>
        {
            if (grid.SelectedItem is OrderChoice selected &&
                selected.Status is not ("closed" or "cancelled" or "billed" or "paid"))
                CloseModal();
        };
        startOrderFromList.Click += (_, _) => OpenModal("order");
        // Escape must dismiss an open modal, not leave full-screen POS.
        posHost.Tag = (Func<bool>)CloseModal;
        Trace.WriteLine($"[Restaurant] POS view constructed: {navigationClock.ElapsedMilliseconds} ms");
        return posHost;
    }


    public static async Task<FrameworkElement> TablesAsync()
    {
        var workflow = new DesktopRestaurantWorkflowService();
        // Materialize all SQLite query results on a background thread before
        // constructing any WPF controls. SQLite async providers may block
        // synchronously while waiting for locks.
        var pageData = await DesktopUiWork.RunAsync(async () =>
        {
            var factory = new LocalDatabaseFactory();
            await factory.EnsureCreatedAsync();
            // Tables keep their live status: a paid table stays occupied until
            // staff explicitly mark it vacant from the dining-room panel.
            await using var db = factory.Create();

            var rows = await (from table in db.DiningTables.AsNoTracking()
                              join area in db.DiningAreas.AsNoTracking() on table.DiningAreaId equals area.Id
                              join branch in db.Branches.AsNoTracking() on area.BranchId equals branch.Id
                              where table.IsActive && area.IsActive && branch.IsActive
                              orderby branch.Name, area.SortOrder, table.Name
                              select new TableChoice(table.Id, branch.Name + " · " + area.Name,
                                  table.Code, table.Name, table.Capacity, table.Status, table.ReservationNote))
                .ToListAsync();

            var activeOrders = (await (
                from order in db.Orders.AsNoTracking()
                join table in db.DiningTables.AsNoTracking() on order.DiningTableId equals table.Id
                where order.ServiceType == "dine_in" &&
                      order.Status != "closed" &&
                      order.Status != "cancelled" &&
                      order.Status != "billed"
                select new
                {
                    Row = new TableOrderChoice(
                        order.Id, order.ClientOrderId, table.Id, table.Name,
                        order.Status, order.Total, order.GuestCount, order.DisplayNumber),
                    order.UpdatedAtUtc,
                })
                .ToListAsync())
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Select(x => x.Row)
                .ToList();

            var unsentLines = (await (
                from line in db.OrderItems.AsNoTracking()
                join order in db.Orders.AsNoTracking() on line.OrderId equals order.Id
                where order.ServiceType == "dine_in" &&
                      line.KotRoundId == null &&
                      (line.Status == "pending" || line.Status == "held")
                select new
                {
                    Row = new TableSplitLineChoice(
                        order.Id, line.Id, line.ItemName, line.Quantity, line.Status),
                    line.CreatedAtUtc,
                })
                .ToListAsync())
                .OrderBy(x => x.CreatedAtUtc)
                .Select(x => x.Row)
                .ToList();

            // Include billed orders on table cards: paid-in-part tables must
            // never appear ownerless or available before final settlement.
            var allFloorOrders = (await (
                from order in db.Orders.AsNoTracking()
                where order.ServiceType == "dine_in" &&
                      order.Status != "closed" && order.Status != "cancelled"
                select new
                {
                    Row = new TableOrderChoice(order.Id, order.ClientOrderId,
                        order.DiningTableId, "", order.Status, order.Total, order.GuestCount, order.DisplayNumber),
                    order.UpdatedAtUtc,
                }).ToListAsync())
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Select(x => x.Row).ToArray();

            var availableTables = rows
                .Where(x => x.Status == "available")
                .Select(x => new Choice(x.Id, $"{x.Area} · {x.Name} ({x.Code})"))
                .ToList();

            var areas = await (from area in db.DiningAreas.AsNoTracking()
                join branch in db.Branches.AsNoTracking() on area.BranchId equals branch.Id
                where area.IsActive && branch.IsActive
                orderby area.SortOrder, area.Name
                select area).ToListAsync();
            var branches = await db.Branches.AsNoTracking().Where(x => x.IsActive)
                .OrderBy(x => x.Name).ToListAsync();

            return (factory, rows, activeOrders, unsentLines, allFloorOrders, availableTables, areas, branches);
        });
        var (factory, rows, activeOrders, unsentLines, allFloorOrders, availableTables, areas, branches) = pageData;

        // The translucent surface belongs to the whole floor workspace, not
        // only the table tiles. Its dynamic resources follow Classic/Glass.
        var root = new StackPanel { Margin = new Thickness(4) };
        root.Children.Add(Header(
            "Dining floor",
            "Live table state shared over LAN. Transfer, merge and split preserve KOT history; split only moves lines not yet sent to production."));

        var floorActions = new WrapPanel { Margin = new Thickness(0, 4, 0, 10) };
        var manageFloor = Button("Manage floors & tables");
        manageFloor.Click += async (_, _) => await RestaurantOperationalPages.OpenCloudManagementAsync("tables");
        floorActions.Children.Add(manageFloor);

        // Reference tables are Laravel-owned. Operators can explicitly pull
        // cross-device changes without relying on a background interval.
        var syncTables = Button("Sync from web");
        syncTables.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        floorActions.Children.Add(syncTables);
        var syncStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 10, 0, 0),
        };
        syncStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        floorActions.Children.Add(syncStatus);
        syncTables.Click += async (_, _) =>
        {
            syncTables.IsEnabled = false;
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var refreshed = await new BusinessOS.Restaurant.Sync.OperationalDataRefreshService(
                    new BusinessOS.Restaurant.Licensing.WindowsActivationStore(),
                    new WindowsSessionStore(),
                    new BusinessOS.Restaurant.Licensing.ConnectionSettingsStore(),
                    new BusinessOS.Restaurant.Sync.CloudOperationalDataClient(http),
                    new OperationalSnapshotStore(new LocalDatabaseFactory())).RefreshIfPossibleAsync();
                if (!refreshed)
                {
                    syncStatus.Text = "Offline or cloud sync disabled.";
                    return;
                }

                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, "Dining tables synchronized from Laravel.");
                if (System.Windows.Application.Current?.MainWindow?.DataContext is MainWindowViewModel vm)
                    await vm.RefreshCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                syncStatus.Text = "Sync failed. " + ex.Message;
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, "Table sync failed.");
            }
            finally
            {
                syncTables.IsEnabled = true;
            }
        };
        // New "dining room" view is the default; the classic floor/list/grid
        // view stays one click away so the team can revert instantly.
        var viewToggle = Button(UseNewDiningRoom ? "Classic floor view" : "Dining room view");
        viewToggle.Name = "TablesViewToggle";
        viewToggle.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        viewToggle.ToolTip = "Switch between the dining room and the classic floor view";
        floorActions.Children.Add(viewToggle);
        root.Children.Add(floorActions);

        var grid = DataGrid(rows);
        grid.MinHeight = 280;
        grid.Columns.Add(Column("Area", nameof(TableChoice.Area), 160));
        grid.Columns.Add(Column("Table", nameof(TableChoice.Name), 220));
        grid.Columns.Add(Column("Code", nameof(TableChoice.Code), 100));
        grid.Columns.Add(Column("Seats", nameof(TableChoice.Capacity), 80));
        grid.Columns.Add(Column("Status", nameof(TableChoice.Status), 130));
        var overrideTable = Combo(rows, "Name");
        overrideTable.Width = 180;
        var markAvailable = Button("Override → Mark Available");
        var actor = await workflow.CurrentPrincipalAsync();
        markAvailable.Visibility = actor.UserRole is "owner" or "admin" or "manager" ? Visibility.Visible : Visibility.Collapsed;
        overrideTable.Visibility = markAvailable.Visibility;
        floorActions.Children.Add(overrideTable);
        floorActions.Children.Add(markAvailable);
        markAvailable.Click += async (_, _) =>
        {
            if (overrideTable.SelectedItem is not TableChoice selected) return;
            if (!ConfirmOperationalChange($"Mark {selected.Name} available? Active or unpaid orders will block this action. This override is audited.", "Override table occupancy")) return;
            markAvailable.IsEnabled = false;
            try
            {
                await DesktopUiWork.RunAsync(async () => await new LocalCashierService(new LocalDatabaseFactory())
                    .MarkTableAvailableAsync(selected.Id, await workflow.CurrentPrincipalAsync(), CancellationToken.None,
                        "Manual table override from the Tables workspace"));
                if (System.Windows.Application.Current?.MainWindow?.DataContext is MainWindowViewModel vm)
                    await vm.RefreshCommand.ExecuteAsync(null);
            }
            catch (Exception ex) { syncStatus.Text = ex.Message; }
            finally { markAvailable.IsEnabled = true; }
        };
        // List, Grid and Floor all consume the same current SQLite table rows.
        // The Floor mode preserves dining-area grouping; Grid shows all tiles.
        grid.Visibility = Visibility.Collapsed;
        var modeChooser = new WrapPanel { Name = "DiningFloorViewModes", Margin = new Thickness(0, 4, 0, 8) };
        var listMode = Button("List");
        var gridMode = Button("Grid");
        var floorMode = Button("Floor");
        foreach (var mode in new[] { listMode, gridMode, floorMode })
        {
            mode.MinWidth = 84;
            mode.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
            modeChooser.Children.Add(mode);
        }
        root.Children.Add(modeChooser);
        root.Children.Add(grid);

        var operations = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        operations.Children.Add(Header(
            "Table & order operations",
            "Transfers keep the same order. Merge can absorb an unsent source into an active target. Move/split only touches unsent pending/held lines, so historical KOT production is never rewritten."));

        var sourceOrderBox = Combo(activeOrders, "Display");
        var targetTableBox = Combo(availableTables, "Label");
        var operationStatus = new TextBlock
        {
            Foreground = Brushes.SlateGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        };

        var visualGrid = BuildVisualFloorBoard(
            rows, allFloorOrders, activeOrders, availableTables,
            sourceOrderBox, targetTableBox, operationStatus, groupByArea: false);
        var visualFloor = BuildVisualFloorBoard(
            rows, allFloorOrders, activeOrders, availableTables,
            sourceOrderBox, targetTableBox, operationStatus, groupByArea: true);
        visualGrid.Visibility = Visibility.Collapsed;
        // View controls precede the floor board for touch interaction.
        var modeIndex = root.Children.IndexOf(modeChooser);
        root.Children.Insert(modeIndex + 1, visualFloor);
        root.Children.Insert(modeIndex + 2, visualGrid);
        void SelectFloorMode(string mode)
        {
            visualFloor.Visibility = mode == "floor" ? Visibility.Visible : Visibility.Collapsed;
            visualGrid.Visibility = mode == "grid" ? Visibility.Visible : Visibility.Collapsed;
            grid.Visibility = mode == "list" ? Visibility.Visible : Visibility.Collapsed;
        }
        listMode.Click += (_, _) => SelectFloorMode("list");
        gridMode.Click += (_, _) => SelectFloorMode("grid");
        floorMode.Click += (_, _) => SelectFloorMode("floor");

        operations.Children.Add(Label("Transfer order"));
        var transferRow = new WrapPanel();
        sourceOrderBox.Width = 300;
        targetTableBox.Width = 260;
        var transfer = Button("Transfer table");
        transferRow.Children.Add(sourceOrderBox);
        transferRow.Children.Add(targetTableBox);
        transferRow.Children.Add(transfer);
        operations.Children.Add(transferRow);

        var mergeTarget = Combo(activeOrders, "Display");
        var mergeSource = Combo(activeOrders, "Display");
        mergeTarget.Width = 300;
        mergeSource.Width = 300;
        var merge = Button("Merge unsent source");
        operations.Children.Add(Label("Merge orders"));
        var mergeRow = new WrapPanel();
        mergeRow.Children.Add(mergeTarget);
        mergeRow.Children.Add(mergeSource);
        mergeRow.Children.Add(merge);
        operations.Children.Add(mergeRow);

        var moveSource = Combo(activeOrders, "Display");
        var moveTarget = Combo(activeOrders, "Display");
        moveSource.Width = 300;
        moveTarget.Width = 300;
        var moveItems = new ListBox
        {
            SelectionMode = SelectionMode.Multiple,
            Height = 140,
            Margin = new Thickness(0, 4, 0, 8),
            DisplayMemberPath = "Display",
        };
        var move = Button("Move selected items");
        operations.Children.Add(Label("Move unsent items between existing orders"));
        var moveHeader = new WrapPanel();
        moveHeader.Children.Add(moveSource);
        moveHeader.Children.Add(moveTarget);
        operations.Children.Add(moveHeader);
        operations.Children.Add(moveItems);
        operations.Children.Add(move);

        moveSource.SelectionChanged += (_, _) =>
        {
            if (moveSource.SelectedItem is TableOrderChoice order)
            {
                moveItems.ItemsSource = unsentLines.Where(x => x.OrderId == order.Id).ToList();
            }
            else
            {
                moveItems.ItemsSource = Array.Empty<TableSplitLineChoice>();
            }
        };

        var splitSource = Combo(activeOrders, "Display");
        var splitTarget = Combo(availableTables, "Label");
        splitSource.Width = 300;
        splitTarget.Width = 260;
        var splitItems = new ListBox
        {
            SelectionMode = SelectionMode.Multiple,
            Height = 140,
            Margin = new Thickness(0, 4, 0, 8),
            DisplayMemberPath = "Display",
        };
        var split = Button("Split selected items");
        operations.Children.Add(Label("Split unsent items to another table"));
        var splitHeader = new WrapPanel();
        splitHeader.Children.Add(splitSource);
        splitHeader.Children.Add(splitTarget);
        operations.Children.Add(splitHeader);
        operations.Children.Add(splitItems);
        operations.Children.Add(split);
        operations.Children.Add(operationStatus);

        splitSource.SelectionChanged += (_, _) =>
        {
            if (splitSource.SelectedItem is TableOrderChoice order)
            {
                splitItems.ItemsSource = unsentLines.Where(x => x.OrderId == order.Id).ToList();
            }
            else
            {
                splitItems.ItemsSource = Array.Empty<TableSplitLineChoice>();
            }
        };

        transfer.Click += async (_, _) =>
        {
            try
            {
                if (sourceOrderBox.SelectedItem is not TableOrderChoice order)
                    throw new InvalidOperationException("Select an active dine-in order.");
                if (targetTableBox.SelectedItem is not Choice target)
                    throw new InvalidOperationException("Select an available target table.");

                await DesktopUiWork.RunAsync(() => workflow.TransferOrderAsync(order.Id, target.Id));
                operationStatus.Text = $"{order.ClientOrderId} transferred to {target.Label}. Refresh to see the new floor state.";
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, operationStatus.Text);
            }
            catch (Exception ex) { operationStatus.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message); }
        };

        merge.Click += async (_, _) =>
        {
            try
            {
                if (mergeTarget.SelectedItem is not TableOrderChoice target)
                    throw new InvalidOperationException("Select the target order.");
                if (mergeSource.SelectedItem is not TableOrderChoice source)
                    throw new InvalidOperationException("Select the source order.");

                await DesktopUiWork.RunAsync(() => workflow.MergeDraftOrdersAsync(target.Id, source.Id));
                operationStatus.Text = $"{source.ClientOrderId} merged into {target.ClientOrderId}.";
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, operationStatus.Text);
            }
            catch (Exception ex) { operationStatus.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message); }
        };

        move.Click += async (_, _) =>
        {
            try
            {
                if (moveSource.SelectedItem is not TableOrderChoice source)
                    throw new InvalidOperationException("Select the source order.");
                if (moveTarget.SelectedItem is not TableOrderChoice target)
                    throw new InvalidOperationException("Select the target order.");

                var ids = moveItems.SelectedItems
                    .Cast<TableSplitLineChoice>()
                    .Select(x => x.OrderItemId)
                    .ToArray();
                if (ids.Length == 0)
                    throw new InvalidOperationException("Select one or more unsent lines.");

                await DesktopUiWork.RunAsync(() => workflow.MoveUnsentItemsAsync(source.Id, target.Id, ids));
                operationStatus.Text = $"{ids.Length} unsent line(s) moved from {source.ClientOrderId} to {target.ClientOrderId}. KOT history stayed on the source order.";
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, operationStatus.Text);
            }
            catch (Exception ex) { operationStatus.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message); }
        };

        split.Click += async (_, _) =>
        {
            try
            {
                if (splitSource.SelectedItem is not TableOrderChoice source)
                    throw new InvalidOperationException("Select the source order.");
                if (splitTarget.SelectedItem is not Choice target)
                    throw new InvalidOperationException("Select an available target table.");

                var ids = splitItems.SelectedItems
                    .Cast<TableSplitLineChoice>()
                    .Select(x => x.OrderItemId)
                    .ToArray();
                if (ids.Length == 0)
                    throw new InvalidOperationException("Select one or more unsent lines.");

                await DesktopUiWork.RunAsync(() => workflow.SplitUnsentItemsAsync(source.Id, target.Id, ids));
                operationStatus.Text = $"{ids.Length} unsent line(s) split to {target.Label}. Existing KOT history was unchanged.";
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, operationStatus.Text);
            }
            catch (Exception ex) { operationStatus.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message); }
        };

        // Keep the floor board prominent; advanced order operations are
        // available on demand instead of making the default page excessively long.
        var advancedOperations = Card(operations);
        advancedOperations.Visibility = Visibility.Collapsed;
        var toggleAdvanced = Button("Show transfer / merge / split");
        toggleAdvanced.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        toggleAdvanced.Margin = new Thickness(0, 10, 0, 6);
        toggleAdvanced.Click += (_, _) =>
        {
            advancedOperations.Visibility = advancedOperations.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
            toggleAdvanced.Content = advancedOperations.Visibility == Visibility.Visible
                ? "Hide transfer / merge / split" : "Show transfer / merge / split";
        };
        root.Children.Add(toggleAdvanced);
        root.Children.Add(advancedOperations);

        var diningHost = await BuildDiningRoomAsync(factory, rows, allFloorOrders, syncStatus);
        root.Children.Add(diningHost);
        var classicElements = new UIElement[] { modeChooser, grid, visualFloor, visualGrid };
        void ApplyTablesView()
        {
            var dining = UseNewDiningRoom;
            diningHost.Visibility = dining ? Visibility.Visible : Visibility.Collapsed;
            foreach (var element in classicElements)
            {
                element.Visibility = dining ? Visibility.Collapsed : Visibility.Visible;
            }

            if (dining)
            {
                toggleAdvanced.Visibility = Visibility.Collapsed;
                advancedOperations.Visibility = Visibility.Collapsed;
            }
            else
            {
                toggleAdvanced.Visibility = Visibility.Visible;
                SelectFloorMode("floor");
            }

            viewToggle.Content = dining ? "Classic floor view" : "Dining room view";
        }
        viewToggle.Click += (_, _) =>
        {
            UseNewDiningRoom = !UseNewDiningRoom;
            ApplyTablesView();
            syncStatus.Text = UseNewDiningRoom
                ? "Dining room view. The classic floor view remains one click away."
                : "Classic floor view restored.";
        };
        ApplyTablesView();
        return new ScrollViewer
        {
            Content = WorkspaceFrostedSurface.Wrap(root),
            Margin = new Thickness(12, 8, 12, 16),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    // Dining room view preference is intentionally session-scoped so the team
    // can flip back to the classic flow instantly while evaluating the design.
    private static bool UseNewDiningRoom = true;

    /// <summary>
    /// Cash checkout for a table order: serve → bill → cash payment, with an
    /// option to mark the table vacant. Reuses the same local workflow calls
    /// (and their duplicate-payment guards) as the POS checkout.
    /// </summary>
    private static async Task<bool> OpenTablePaymentModalAsync(
        LocalDatabaseFactory factory,
        TableChoice table,
        TableOrderChoice order,
        TextBlock statusText)
    {
        var workflow = new DesktopRestaurantWorkflowService();
        var busy = false;
        var total = order.Total;

        var lines = await DesktopUiWork.RunAsync(async () =>
        {
            await using var db = factory.Create();
            var orderId = await db.Orders.AsNoTracking()
                .Where(x => x.ClientOrderId == order.ClientOrderId)
                .Select(x => x.Id)
                .FirstOrDefaultAsync();
            if (orderId is null)
            {
                return (IReadOnlyList<TableLineChoice>)[];
            }

            return (IReadOnlyList<TableLineChoice>)(await db.OrderItems.AsNoTracking()
                    .Where(x => x.OrderId == orderId && x.Status != "voided" && x.Status != "cancelled")
                    .Select(x => new { x.ClientLineId, x.ItemName, x.Quantity, x.LineTotal, x.Status, x.KotRoundId, x.CreatedAtUtc })
                    .ToListAsync())
                .OrderBy(x => x.CreatedAtUtc)
                .Select(x => new TableLineChoice(x.ClientLineId, x.ItemName, x.Quantity, x.LineTotal,
                    x.KotRoundId == null && (x.Status == "pending" || x.Status == "held")))
                .ToList();
        });

        var dialog = new Window
        {
            Title = $"Take payment · {table.Name}",
            Name = "TablePaymentDialog",
            Width = 520,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            Owner = System.Windows.Application.Current?.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var content = new StackPanel { Margin = new Thickness(26, 22, 26, 24) };
        content.Children.Add(new TextBlock { Text = table.Name, FontSize = 24, FontWeight = FontWeights.Bold });
        var caption = new TextBlock
        {
            Text = $"{order.ShortNumber} · {order.Guests} guests · cash payment",
            Margin = new Thickness(0, 4, 0, 16),
        };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        content.Children.Add(caption);

        var linesHost = new StackPanel();
        foreach (var line in lines)
        {
            var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new TextBlock { Text = $"{line.Quantity} × {line.ItemName}", TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(text, 0);
            row.Children.Add(text);
            var amount = new TextBlock
            {
                Text = $"AFN {line.LineTotal:N2}", Margin = new Thickness(10, 0, 0, 0),
                FontWeight = FontWeights.SemiBold,
            };
            Grid.SetColumn(amount, 1);
            row.Children.Add(amount);
            linesHost.Children.Add(row);
        }
        if (lines.Count == 0)
        {
            var empty = new TextBlock { Text = "No items on this order yet.", Margin = new Thickness(0, 4, 0, 4) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            linesHost.Children.Add(empty);
        }
        content.Children.Add(linesHost);

        var totalRow = new Grid { Margin = new Thickness(0, 14, 0, 4) };
        totalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        totalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        totalRow.Children.Add(new TextBlock { Text = "Total due", FontSize = 18, FontWeight = FontWeights.Bold });
        var totalText = new TextBlock { Text = $"AFN {total:N2}", FontSize = 21, FontWeight = FontWeights.Bold };
        totalText.SetResourceReference(TextBlock.ForegroundProperty, "BrandPrimaryBrush");
        Grid.SetColumn(totalText, 1);
        totalRow.Children.Add(totalText);
        content.Children.Add(totalRow);

        content.Children.Add(new TextBlock { Text = "Amount received (AFN)", Margin = new Thickness(0, 14, 0, 6) });
        var received = new TextBox { Text = total.ToString("0.00"), Height = 40, MinWidth = 180 };
        System.Windows.Automation.AutomationProperties.SetName(received, "Amount received");
        content.Children.Add(received);

        var changeText = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) };
        content.Children.Add(changeText);
        void UpdateChange()
        {
            var enough = decimal.TryParse(received.Text, out var parsed) && parsed >= total;
            changeText.Text = enough
                ? $"Change to give back: AFN {parsed - total:N2}"
                : "Enter the cash amount received (at least the total due).";
            changeText.SetResourceReference(TextBlock.ForegroundProperty, enough ? "SuccessBrush" : "TextMutedBrush");
        }
        received.TextChanged += (_, _) => UpdateChange();
        UpdateChange();

        var markVacant = new CheckBox
        {
            Name = "TablePaymentMarkVacant",
            Content = $"Mark {table.Name} vacant after payment",
            IsChecked = true,
            Margin = new Thickness(0, 14, 0, 0),
        };
        content.Children.Add(markVacant);

        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        content.Children.Add(status);

        var complete = Button("Complete payment");
        complete.Name = "TablePaymentCompleteButton";
        complete.MinWidth = 190;
        complete.MinHeight = 46;
        complete.IsDefault = true;
        var cancel = Button("Cancel");
        cancel.IsCancel = true;
        cancel.MinHeight = 46;
        cancel.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        actions.Children.Add(complete);
        actions.Children.Add(cancel);
        content.Children.Add(actions);

        complete.Click += async (_, _) =>
        {
            if (busy)
            {
                return;
            }

            busy = true;
            complete.IsEnabled = false;
            decimal amount = 0m;
            var posted = 0m;
            try
            {
                if (!decimal.TryParse(received.Text, out amount) || amount < total || total <= 0m)
                {
                    throw new InvalidOperationException("Enter a cash amount at least equal to the total due.");
                }

                status.Text = "Verifying the order, bill and cashier session…";
                var actor = await workflow.CurrentPrincipalAsync();
                await DesktopUiWork.RunBoundedAsync(async token =>
                {
                    await using var db = factory.Create();
                    var live = await db.Orders.AsNoTracking()
                        .Where(x => x.ClientOrderId == order.ClientOrderId)
                        .Select(x => new { x.Id, x.Status })
                        .FirstOrDefaultAsync(token)
                        ?? throw new InvalidOperationException("The order no longer exists.");

                    if (live.Status is "closed" or "cancelled")
                    {
                        throw new InvalidOperationException("This order is already closed.");
                    }

                    if (live.Status is not ("served" or "billed"))
                    {
                        // Serving requires every kitchen ticket to be ready; the
                        // workflow raises the precise reason otherwise.
                        await workflow.ServeOrderAsync(live.Id, token);
                    }

                    var bill = await db.Bills.AsNoTracking()
                        .Where(x => x.OrderId == live.Id && x.Status == "open")
                        .Select(x => new { x.Id, x.BranchId, x.BalanceDue })
                        .FirstOrDefaultAsync(token);
                    if (bill is null)
                    {
                        await workflow.CreateBillAsync(live.Id, token);
                        await using var recheck = factory.Create();
                        bill = await recheck.Bills.AsNoTracking()
                            .Where(x => x.OrderId == live.Id && x.Status == "open")
                            .Select(x => new { x.Id, x.BranchId, x.BalanceDue })
                            .FirstOrDefaultAsync(token);
                    }
                    if (bill is null)
                    {
                        throw new InvalidOperationException("A bill could not be issued for this order.");
                    }

                    var sessionId = (await db.CashierSessions.AsNoTracking()
                            .Where(x => x.BranchId == bill.BranchId &&
                                        x.CashierUserId == actor.UserId &&
                                        x.Status == "open")
                            .Select(x => new { x.Id, x.OpenedAt })
                            .ToListAsync(token))
                        .OrderByDescending(x => x.OpenedAt)
                        .Select(x => x.Id)
                        .FirstOrDefault();
                    if (sessionId is null)
                    {
                        var created = await workflow.OpenCashierSessionAsync(bill.BranchId, 0m, token);
                        sessionId = System.Text.Json.JsonSerializer.SerializeToElement(created)
                            .GetProperty("id").GetString();
                    }
                    if (sessionId is null)
                    {
                        throw new InvalidOperationException("Cashier session could not be opened.");
                    }

                    var applied = Math.Min(amount, bill.BalanceDue);
                    if (applied <= 0m)
                    {
                        throw new InvalidOperationException("This bill is already settled.");
                    }

                    await workflow.AddPaymentAsync(bill.Id, sessionId, applied, "cash", null, token);
                    posted = applied;
                }, TimeSpan.FromSeconds(30));

                if (markVacant.IsChecked == true)
                {
                    try
                    {
                        await DesktopUiWork.RunBoundedAsync(
                            token => new LocalCashierService(factory).MarkTableAvailableAsync(
                                table.Id, actor, token, "Payment received at table"),
                            TimeSpan.FromSeconds(25));
                    }
                    catch (Exception ex)
                    {
                        // Settlement already released the table; a failed manual
                        // release must not hide a successful payment.
                        App.LogRecoverableException("table-payment-release", ex);
                    }
                }
                else
                {
                    // Settlement releases the table; holding it keeps the table
                    // occupied until staff clear it, honoring the cashier's
                    // "do not mark vacant yet" choice.
                    try
                    {
                        await DesktopUiWork.RunBoundedAsync(
                            token => new LocalCashierService(factory).HoldTableAsync(
                                table.Id, "Paid · awaiting clearing", actor, token),
                            TimeSpan.FromSeconds(25));
                    }
                    catch (Exception ex)
                    {
                        App.LogRecoverableException("table-payment-hold", ex);
                    }
                }

                var change = Math.Max(0m, amount - posted);
                var occupancyNote = markVacant.IsChecked == true
                    ? $" {table.Name} is vacant."
                    : $" {table.Name} stays occupied until marked vacant.";
                statusText.Text = (change > 0m
                    ? $"{table.Name}: AFN {posted:N2} received; change AFN {change:N2}."
                    : $"{table.Name}: AFN {posted:N2} received; bill settled.") + occupancyNote;
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, statusText.Text);
                dialog.DialogResult = true;
            }
            catch (Exception ex)
            {
                App.LogRecoverableException("table-payment", ex);
                status.Text = ex.Message;
                status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message);
            }
            finally
            {
                busy = false;
                complete.IsEnabled = true;
            }
        };

        dialog.Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return DesktopGlassDialog.ShowDialog(dialog) == true;
    }

    /// <summary>
    /// Dining-room flow: pick a table on the left, act on its live order in the
    /// right-hand panel (add dishes/guests, send kitchen round, take payment).
    /// Reuses the same local workflow service and idempotent mutations as POS.
    /// </summary>
    private static async Task<FrameworkElement> BuildDiningRoomAsync(
        LocalDatabaseFactory factory,
        IReadOnlyList<TableChoice> initialTables,
        IReadOnlyList<TableOrderChoice> initialOrders,
        TextBlock statusText)
    {
        var workflow = new DesktopRestaurantWorkflowService();
        var actor = await workflow.CurrentPrincipalAsync();
        var tablesNow = initialTables;
        var ordersNow = initialOrders;
        string? selectedTableId = null;
        var busy = false;

        var grid = new WrapPanel { Margin = new Thickness(0, 2, 0, 12) };
        var legend = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var panel = new StackPanel();
        var panelHost = new Border
        {
            Width = 360,
            Margin = new Thickness(16, 2, 0, 0),
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };
        panelHost.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
        panelHost.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

        (Color Stripe, string Label, Color LabelColor) TableState(TableChoice table, TableOrderChoice? order)
        {
            if (table.Status == "available") return (Color.FromRgb(22, 155, 98), "Free", Color.FromRgb(22, 155, 98));
            if (table.Status == "occupied" && order?.Status == "billed") return (Color.FromRgb(185, 28, 28), "Waiting to pay", Color.FromRgb(185, 28, 28));
            if (table.Status == "occupied" && order is null) return (Color.FromRgb(217, 119, 6), "Awaiting clearing", Color.FromRgb(180, 83, 9));
            if (table.Status == "occupied") return (Color.FromRgb(217, 119, 6), "Seated", Color.FromRgb(180, 83, 9));
            if (table.Status == "reserved") return (Color.FromRgb(37, 99, 235), "Reserved", Color.FromRgb(37, 99, 235));
            return (Color.FromRgb(100, 116, 139), table.Status, Color.FromRgb(100, 116, 139));
        }

        void Legend(string count, string label, Color color)
        {
            legend.Children.Add(new TextBlock
            {
                Text = "●", Foreground = new SolidColorBrush(color), FontSize = 14,
                Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
            });
            var text = new TextBlock
            {
                Text = $"{count} {label}",
                Margin = new Thickness(0, 0, 18, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            legend.Children.Add(text);
        }

        void UpdateLegend()
        {
            legend.Children.Clear();
            Legend(tablesNow.Count(x => x.Status == "available").ToString(), "free", Color.FromRgb(22, 155, 98));
            Legend(tablesNow.Count(x => x.Status == "occupied" &&
                ordersNow.FirstOrDefault(o => o.TableId == x.Id)?.Status != "billed").ToString(), "seated", Color.FromRgb(217, 119, 6));
            Legend(tablesNow.Count(x => x.Status == "reserved").ToString(), "reserved", Color.FromRgb(37, 99, 235));
            Legend(tablesNow.Count(x => ordersNow.FirstOrDefault(o => o.TableId == x.Id)?.Status == "billed").ToString(),
                "waiting to pay", Color.FromRgb(185, 28, 28));
        }

        // Session-scoped reservation prompt: a short guest/time note stored on
        // the table row; seating a reserved table releases the hold first.
        string? PromptReservationNote(string tableName)
        {
            var dialog = new Window
            {
                Title = $"Reserve {tableName}",
                Width = 460,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                Owner = System.Windows.Application.Current?.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var form = new StackPanel { Margin = new Thickness(20) };
            form.Children.Add(new TextBlock
            {
                Text = "Reservation note (guest name, time and party size)",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
            });
            var note = new TextBox { Height = 36 };
            System.Windows.Automation.AutomationProperties.SetName(note, "Reservation note");
            form.Children.Add(note);
            var save = Button("Reserve table");
            save.MinWidth = 150;
            save.MinHeight = 40;
            save.Margin = new Thickness(0, 16, 8, 0);
            save.IsDefault = true;
            var cancel = Button("Cancel");
            cancel.IsCancel = true;
            cancel.MinHeight = 40;
            cancel.Margin = new Thickness(0, 16, 0, 0);
            cancel.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
            var actions = new WrapPanel();
            actions.Children.Add(save);
            actions.Children.Add(cancel);
            form.Children.Add(actions);
            save.Click += (_, _) => dialog.DialogResult = true;
            dialog.Content = form;
            return DesktopGlassDialog.ShowDialog(dialog) == true ? note.Text.Trim() : null;
        }

        async Task<IReadOnlyList<TableLineChoice>> LoadLinesAsync(string clientOrderId)
        {
            return await DesktopUiWork.RunAsync(async () =>
            {
                await using var db = factory.Create();
                var orderId = await db.Orders.AsNoTracking()
                    .Where(x => x.ClientOrderId == clientOrderId)
                    .Select(x => x.Id)
                    .FirstOrDefaultAsync();
                if (orderId is null)
                {
                    return (IReadOnlyList<TableLineChoice>)[];
                }

                return (IReadOnlyList<TableLineChoice>)(await db.OrderItems.AsNoTracking()
                        .Where(x => x.OrderId == orderId && x.Status != "voided" && x.Status != "cancelled")
                        .Select(x => new { x.ClientLineId, x.ItemName, x.Quantity, x.LineTotal, x.Status, x.KotRoundId, x.CreatedAtUtc })
                        .ToListAsync())
                    .OrderBy(x => x.CreatedAtUtc)
                    .Select(x => new TableLineChoice(x.ClientLineId, x.ItemName, x.Quantity, x.LineTotal,
                        x.KotRoundId == null && (x.Status == "pending" || x.Status == "held")))
                    .ToList();
            });
        }

        Button BuildCard(TableChoice table)
        {
            var order = ordersNow.FirstOrDefault(x => x.TableId == table.Id);
            var (stripeColor, label, labelColor) = TableState(table, order);
            var tint = new SolidColorBrush(Color.FromArgb(24, stripeColor.R, stripeColor.G, stripeColor.B));

            var labelText = new TextBlock
            {
                Text = table.Name, FontSize = 18, FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            labelText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            var seats = new TextBlock { Text = $"{table.Capacity} seats", FontSize = 11, Margin = new Thickness(0, 4, 0, 12) };
            seats.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            var status = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(labelColor) };
            var detail = new TextBlock
            {
                Text = order is null
                    ? (table.Status == "reserved" ? "Reserved"
                        : table.Status == "occupied" ? "Awaiting clearing" : "Ready to seat")
                    : $"{order.Guests} guests · {order.ShortNumber} · AFN {order.Total:N0}",
                FontSize = 11, Margin = new Thickness(0, 6, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

            var body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            body.Children.Add(labelText);
            body.Children.Add(RestaurantTableFurniture.Create(table.Name, table.Capacity, stripeColor));
            body.Children.Add(status);
            if (order is not null)
            {
                body.Children.Add(detail);
            }

            var stripe = new Border { Width = 6, Background = new SolidColorBrush(stripeColor) };
            var layout = new DockPanel();
            DockPanel.SetDock(stripe, Dock.Left);
            layout.Children.Add(stripe);
            layout.Children.Add(body);

            var selected = table.Id == selectedTableId;
            var surface = new Border
            {
                Width = 196, MinHeight = 124, CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(selected ? 2 : 1),
                Background = tint,
                ClipToBounds = true,
                Child = layout,
            };
            surface.SetResourceReference(Border.BorderBrushProperty,
                selected ? "BrandPrimaryBrush" : "FloorTileBorderBrush");

            var tile = new Button
            {
                Content = surface, Padding = new Thickness(0), BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Margin = new Thickness(0, 0, 9, 9),
                ToolTip = order is null ? "Select this table to seat guests" : "Select this table to work on its order",
            };
            tile.Click += async (_, _) =>
            {
                selectedTableId = table.Id;
                await RenderAsync();
            };
            return tile;
        }

        async Task RenderPanelAsync()
        {
            panel.Children.Clear();
            var table = tablesNow.FirstOrDefault(x => x.Id == selectedTableId);
            if (table is null)
            {
                panel.Children.Add(new TextBlock { Text = "Dining room", FontSize = 24, FontWeight = FontWeights.Bold });
                var hint = new TextBlock
                {
                    Text = "Select a table to open its order or seat new guests.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 0),
                };
                hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                panel.Children.Add(hint);
                return;
            }

            var order = ordersNow.FirstOrDefault(x => x.TableId == table.Id);
            panel.Children.Add(new TextBlock { Text = table.Name, FontSize = 24, FontWeight = FontWeights.Bold });
            var subtitle = new TextBlock
            {
                Text = order is null
                    ? (table.Status == "reserved"
                        ? (string.IsNullOrWhiteSpace(table.ReservationNote) ? "Reserved" : $"Reserved · {table.ReservationNote}")
                        : table.Status == "occupied"
                            ? (string.IsNullOrWhiteSpace(table.ReservationNote)
                                ? "Occupied · awaiting clearing"
                                : $"Occupied · {table.ReservationNote}")
                            : $"Free, seats {table.Capacity}")
                    : $"{order.Guests} guests · {order.ShortNumber}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 12),
            };
            subtitle.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            panel.Children.Add(subtitle);

            var lines = order is null
                ? (IReadOnlyList<TableLineChoice>)[]
                : await LoadLinesAsync(order.ClientOrderId);

            if (lines.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = order is null ? "No open order for this table." : "No items yet — add dishes to start the order.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 8),
                };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
                panel.Children.Add(empty);
            }

            // A dish can be cancelled one by one even after it reached the
            // kitchen: the order.item.void workflow voids its KOT item (started
            // production becomes waste), releases reserved stock and
            // recalculates the order total. Hidden once the order is billed.
            var cancellableOrderId = order is not null &&
                order.Status is not ("billed" or "closed" or "cancelled")
                ? order.ClientOrderId
                : null;
            foreach (var line in lines)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                if (cancellableOrderId is not null)
                {
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                }
                var text = new TextBlock
                {
                    Text = $"{line.Quantity} × {line.ItemName}{(line.Unsent ? "  · not sent" : "")}",
                    TextWrapping = TextWrapping.Wrap,
                };
                if (line.Unsent)
                {
                    text.SetResourceReference(TextBlock.ForegroundProperty, "BrandPrimaryBrush");
                }
                Grid.SetColumn(text, 0);
                row.Children.Add(text);
                var amount = new TextBlock
                {
                    Text = $"AFN {line.LineTotal:N0}", Margin = new Thickness(10, 0, 0, 0),
                    FontWeight = FontWeights.SemiBold,
                };
                Grid.SetColumn(amount, 1);
                row.Children.Add(amount);
                if (cancellableOrderId is not null)
                {
                    var cancelLine = Button("Cancel");
                    cancelLine.Name = "DiningRoomCancelLineButton";
                    cancelLine.MinWidth = 74;
                    cancelLine.Height = 28;
                    cancelLine.Margin = new Thickness(10, 0, 0, 0);
                    cancelLine.Padding = new Thickness(8, 0, 8, 0);
                    cancelLine.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
                    cancelLine.ToolTip = line.Unsent
                        ? $"Cancel {line.ItemName} before it is sent"
                        : $"Cancel {line.ItemName} — voids its kitchen ticket item";
                    var captured = line;
                    cancelLine.Click += async (_, _) =>
                    {
                        if (busy)
                        {
                            return;
                        }

                        var reason = PromptAuditReason(
                            $"Cancel {captured.ItemName}",
                            captured.Unsent
                                ? $"Cancel {captured.Quantity} × {captured.ItemName}? It has not been sent to the kitchen yet."
                                : $"Cancel {captured.Quantity} × {captured.ItemName}? It was already sent to the kitchen:\n" +
                                  "the KOT item is voided and any started production is recorded as waste.",
                            captured.Unsent ? "Cancelled before kitchen send" : "Cancelled at the table");
                        if (reason is null)
                        {
                            return;
                        }

                        busy = true;
                        try
                        {
                            await DesktopUiWork.RunBoundedAsync(
                                token => workflow.VoidOrderItemAsync(
                                    cancellableOrderId, captured.ClientLineId, reason, token),
                                TimeSpan.FromSeconds(25));
                            statusText.Text = $"{captured.ItemName} cancelled.";
                            await RefreshAsync();
                        }
                        catch (Exception ex)
                        {
                            statusText.Text = ex.Message;
                        }
                        finally
                        {
                            busy = false;
                        }
                    };
                    Grid.SetColumn(cancelLine, 2);
                    row.Children.Add(cancelLine);
                }
                panel.Children.Add(row);
            }

            var totalRow = new Grid { Margin = new Thickness(0, 14, 0, 10) };
            totalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            totalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            totalRow.Children.Add(new TextBlock { Text = "Total", FontSize = 18, FontWeight = FontWeights.Bold });
            var totalText = new TextBlock { Text = $"AFN {order?.Total ?? 0m:N2}", FontSize = 20, FontWeight = FontWeights.Bold };
            totalText.SetResourceReference(TextBlock.ForegroundProperty, "BrandPrimaryBrush");
            Grid.SetColumn(totalText, 1);
            totalRow.Children.Add(totalText);
            panel.Children.Add(totalRow);

            var addDishes = Button(order is null
                ? (table.Status == "reserved" ? "Seat now & take order" : "Seat guests & take order")
                : "Add dishes & guests");
            addDishes.Name = "DiningRoomOrderButton";
            addDishes.MinHeight = 44;
            var reserve = Button(table.Status == "reserved" ? "Cancel reservation" : "Reserve table");
            reserve.Name = "DiningRoomReserveButton";
            reserve.MinHeight = 44;
            reserve.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
            reserve.Visibility = table.Status is "available" or "reserved"
                ? Visibility.Visible : Visibility.Collapsed;
            var send = Button("Send to kitchen");
            send.Name = "DiningRoomSendButton";
            send.MinHeight = 44;
            send.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
            send.IsEnabled = order is not null && lines.Any(line => line.Unsent);
            var pay = Button("Take payment");
            pay.Name = "DiningRoomPayButton";
            pay.MinHeight = 44;
            pay.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
            pay.IsEnabled = order is not null;
            // A paid table waiting for the crew to clear it exposes the one
            // action it needs: mark it vacant (no open order required).
            var vacant = Button("Mark vacant");
            vacant.Name = "DiningRoomVacantButton";
            vacant.MinHeight = 44;
            vacant.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
            vacant.Visibility = order is null && table.Status == "occupied"
                ? Visibility.Visible : Visibility.Collapsed;
            // Whole-order cancellation: voids every line (KOT history kept,
            // produced food becomes waste) and frees the table. Hidden once
            // the order is billed — then payment is the only way forward.
            var cancelOrder = Button("Cancel order");
            cancelOrder.Name = "DiningRoomCancelOrderButton";
            cancelOrder.MinHeight = 44;
            cancelOrder.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
            cancelOrder.Visibility = order is not null &&
                order.Status is not ("billed" or "closed" or "cancelled")
                ? Visibility.Visible : Visibility.Collapsed;
            panel.Children.Add(addDishes);
            panel.Children.Add(reserve);
            panel.Children.Add(vacant);
            panel.Children.Add(send);
            panel.Children.Add(pay);
            panel.Children.Add(cancelOrder);

            addDishes.Click += async (_, _) =>
            {
                try
                {
                    if (busy)
                    {
                        return;
                    }

                    if (table.Status == "reserved")
                    {
                        // Seating a reserved table releases the hold first; the
                        // order service only accepts free tables for dine-in.
                        busy = true;
                        await DesktopUiWork.RunBoundedAsync(
                            token => new LocalCashierService(factory).CancelReservationAsync(table.Id, actor, token),
                            TimeSpan.FromSeconds(25));
                        busy = false;
                    }

                    await OpenTableOrderModalAsync(table, "dine_in", order, statusText);
                    await RefreshAsync();
                }
                catch (Exception ex)
                {
                    busy = false;
                    statusText.Text = ex.Message;
                }
            };
            reserve.Click += async (_, _) =>
            {
                if (busy)
                {
                    return;
                }

                try
                {
                    busy = true;
                    if (table.Status == "reserved")
                    {
                        if (!DesktopGlassDialog.Confirm(
                                $"Release the reservation for {table.Name}?",
                                "Cancel reservation"))
                        {
                            return;
                        }

                        await DesktopUiWork.RunBoundedAsync(
                            token => new LocalCashierService(factory).CancelReservationAsync(table.Id, actor, token),
                            TimeSpan.FromSeconds(25));
                        statusText.Text = $"{table.Name} reservation released.";
                    }
                    else
                    {
                        var note = PromptReservationNote(table.Name);
                        if (note is null)
                        {
                            return;
                        }

                        await DesktopUiWork.RunBoundedAsync(
                            token => new LocalCashierService(factory).ReserveTableAsync(table.Id, note, actor, token),
                            TimeSpan.FromSeconds(25));
                        statusText.Text = $"{table.Name} reserved.";
                    }

                    await RefreshAsync();
                }
                catch (Exception ex)
                {
                    statusText.Text = ex.Message;
                }
                finally
                {
                    busy = false;
                }
            };
            send.Click += async (_, _) =>
            {
                if (busy || order is null)
                {
                    return;
                }

                busy = true;
                send.IsEnabled = false;
                try
                {
                    await DesktopUiWork.RunBoundedAsync(
                        token => workflow.SendKotAsync(order.ClientOrderId, token),
                        TimeSpan.FromSeconds(25));
                    statusText.Text = $"{table.Name}: new items sent to the kitchen.";
                    await RefreshAsync();
                }
                catch (Exception ex)
                {
                    statusText.Text = ex.Message;
                }
                finally
                {
                    busy = false;
                }
            };
            pay.Click += async (_, _) =>
            {
                if (order is null)
                {
                    statusText.Text = $"{table.Name} has no open order to pay.";
                    return;
                }

                var paid = await OpenTablePaymentModalAsync(factory, table, order, statusText);
                if (paid)
                {
                    await RefreshAsync();
                }
            };
            cancelOrder.Click += async (_, _) =>
            {
                if (busy || order is null)
                {
                    return;
                }

                var reason = PromptAuditReason(
                    $"Cancel {table.Name}'s order",
                    $"Cancel the whole order for {table.Name} ({order.ShortNumber})?\n" +
                    "Every line is voided, kitchen history is preserved, produced food is recorded as waste and the table is freed.",
                    "Cancelled at the table");
                if (reason is null)
                {
                    return;
                }

                busy = true;
                try
                {
                    await DesktopUiWork.RunBoundedAsync(
                        token => workflow.CancelOrderAsync(order.ClientOrderId, reason, token),
                        TimeSpan.FromSeconds(25));
                    statusText.Text = $"{table.Name}: order cancelled.";
                    await RefreshAsync();
                }
                catch (Exception ex)
                {
                    statusText.Text = ex.Message;
                }
                finally
                {
                    busy = false;
                }
            };
            vacant.Click += async (_, _) =>
            {
                if (busy)
                {
                    return;
                }

                busy = true;
                try
                {
                    await DesktopUiWork.RunBoundedAsync(
                        token => new LocalCashierService(factory).ClearTableAsync(
                            table.Id, actor, token, "Cleared from the dining room"),
                        TimeSpan.FromSeconds(25));
                    statusText.Text = $"{table.Name} is vacant.";
                    await RefreshAsync();
                }
                catch (Exception ex)
                {
                    statusText.Text = ex.Message;
                }
                finally
                {
                    busy = false;
                }
            };
        }

        async Task RenderAsync()
        {
            grid.Children.Clear();
            foreach (var table in tablesNow)
            {
                grid.Children.Add(BuildCard(table));
            }
            UpdateLegend();
            await RenderPanelAsync();
        }

        async Task RefreshAsync()
        {
            try
            {
                var refreshed = await DesktopUiWork.RunAsync(async () =>
                {
                    await using var db = factory.Create();
                    var refreshedTables = await (from table in db.DiningTables.AsNoTracking()
                                                 join area in db.DiningAreas.AsNoTracking() on table.DiningAreaId equals area.Id
                                                 join branch in db.Branches.AsNoTracking() on area.BranchId equals branch.Id
                                                 where table.IsActive && area.IsActive && branch.IsActive
                                                 orderby branch.Name, area.SortOrder, table.Name
                                                 select new TableChoice(table.Id, branch.Name + " · " + area.Name,
                                                     table.Code, table.Name, table.Capacity, table.Status,
                                                     table.ReservationNote)).ToListAsync();
                    var refreshedOrders = (await db.Orders.AsNoTracking()
                            .Where(x => x.ServiceType == "dine_in" && x.Status != "closed" && x.Status != "cancelled")
                            .Select(x => new
                            {
                                Row = new TableOrderChoice(x.Id, x.ClientOrderId, x.DiningTableId, "", x.Status, x.Total, x.GuestCount, x.DisplayNumber),
                                x.UpdatedAtUtc,
                            })
                            .ToListAsync())
                        .OrderByDescending(x => x.UpdatedAtUtc)
                        .Select(x => x.Row).ToArray();
                    return (Tables: refreshedTables, Orders: refreshedOrders);
                });
                tablesNow = refreshed.Tables;
                ordersNow = refreshed.Orders;
                await RenderAsync();
            }
            catch (Exception ex)
            {
                statusText.Text = "Dining room refresh failed: " + ex.Message;
            }
        }

        var roomTitle = new TextBlock { Text = "Dining room", FontSize = 24, FontWeight = FontWeights.Bold };
        var roomHint = new TextBlock
        {
            Text = "Select a table to open its order or seat new guests.",
            Margin = new Thickness(0, 4, 0, 10),
        };
        roomHint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        var left = new StackPanel();
        left.Children.Add(roomTitle);
        left.Children.Add(roomHint);
        left.Children.Add(legend);
        left.Children.Add(grid);

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.Children.Add(left);
        Grid.SetColumn(panelHost, 1);
        layout.Children.Add(panelHost);

        await RenderAsync();
        return layout;
    }

    public static async Task<FrameworkElement> KitchenAsync()
    {
        var factory = new LocalDatabaseFactory();
        await DesktopUiWork.RunAsync(() => factory.EnsureCreatedAsync());

        var settingsService = new LocalRestaurantSettingsService(factory);
        var settings = await DesktopUiWork.RunAsync(() => settingsService.GetAsync());
        var rows = await DesktopUiWork.RunAsync(() => LoadKitchenBoardRowsAsync(factory));

        // Item ids already announced as ready so a LAN/tablet change or a local
        // tap plays the pickup cue exactly once per dish.
        var knownReadyIds = rows
            .Where(row => row.Status == "ready")
            .Select(row => row.ItemId)
            .ToHashSet(StringComparer.Ordinal);

        var workflow = new DesktopRestaurantWorkflowService();
        var principal = await DesktopUiWork.RunAsync(() => workflow.CurrentPrincipalAsync());
        var canProduce = principal.UserRole is "owner" or "manager" or "kitchen";
        var canExpo = principal.UserRole is "owner" or "manager" or "expo";
        var canRefire = principal.UserRole is "owner" or "manager" or "kitchen";
        var canRecallWaste = canRefire;

        var root = new StackPanel();
        root.Children.Add(Header(
            "Kitchen / KOT",
            settings.ReadyOnlyKotEnabled
                ? "Tap READY to notify the waiter when food can be picked up."
                : settings.ExpoEnabled
                    ? "Tickets progress through START → TO EXPO → PASS EXPO before waiter pickup."
                    : "Tickets progress through START → READY before waiter pickup."));

        var filterRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var stationFilter = new ComboBox
        {
            DisplayMemberPath = "Label",
            Width = 220,
            Height = 34,
            Margin = new Thickness(0, 4, 0, 6),
        };
        var statusFilter = new ComboBox
        {
            ItemsSource = new[] { "All states", "queued", "active", "preparing", "expo", "ready", "completed" },
            SelectedIndex = 0,
            Width = 160,
            Height = 34,
            Margin = new Thickness(0, 4, 8, 6),
        };

        var statusText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 10, 0, 0),
        };
        statusText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        filterRow.Children.Add(stationFilter);
        filterRow.Children.Add(statusFilter);
        var selectedDish = new ComboBox
        {
            Name = "KotTopSelectedDish",
            DisplayMemberPath = "Label",
            Width = 390, Height = 36,
            Margin = new Thickness(6, 4, 8, 6),
            ToolTip = "Choose the KOT dish that is ready for pickup",
        };
        var markReady = Button("READY");
        markReady.Name = "KotTopReadyButton";
        markReady.MinHeight = 42;
        markReady.ToolTip = "F2 · Advance selected dish through the workflow set for its KOT round";
        var boardRefresh = Button("Refresh board");
        boardRefresh.Name = "KotTopRefreshButton";
        boardRefresh.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        boardRefresh.ToolTip = "F5 · Reload the kitchen board";
        filterRow.Children.Add(selectedDish);
        filterRow.Children.Add(markReady);
        filterRow.Children.Add(boardRefresh);
        // Same workstation chrome as POS, right of the kitchen action row.
        var (kotKeys, kotFullScreen) = BuildWorkspaceChromeButtons();
        filterRow.Children.Add(kotKeys);
        filterRow.Children.Add(kotFullScreen);
        filterRow.Children.Add(statusText);
        root.Children.Add(filterRow);

        // Live service counters update with the KDS refresh and aging timer.
        // They reflect actual item status, station filter and delay thresholds.
        var kitchenSummary = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 14),
            TextWrapping = TextWrapping.Wrap,
        };
        kitchenSummary.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        root.Children.Add(kitchenSummary);

        // READY is a pickup queue, not a second production step or an
        // automatic service/payment event. The waiter must deliver the order
        // and mark the complete ready order SERVED from POS & Orders.
        var handoffText = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        handoffText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var handoffContents = new StackPanel();
        handoffContents.Children.Add(handoffText);
        if (principal.UserRole is "owner" or "manager" or "cashier")
        {
            var openService = Button("OPEN POS & ORDERS · MARK SERVED");
            openService.MinWidth = 0;
            openService.Margin = new Thickness(0, 10, 0, 0);
            openService.ToolTip =
                "After the waiter delivers every ready item, select the complete order in POS and mark it Served. Then bill and receive payment.";
            openService.Click += (_, _) =>
            {
                var vm = System.Windows.Application.Current?.MainWindow?.DataContext
                    as MainWindowViewModel;
                var navigate = vm?.NavigateCommand;
                if (navigate?.CanExecute("pos") == true)
                    navigate.Execute("pos");
            };
            handoffContents.Children.Add(openService);
        }
        var handoffPanel = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 11, 14, 11),
            Margin = new Thickness(0, 0, 0, 14),
            Child = handoffContents,
        };
        handoffPanel.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
        handoffPanel.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        root.Children.Add(handoffPanel);

        // Three flow columns (New → Cooking → Ready for pickup) mirror the
        // kitchen mock-up; every card keeps its one-tap READY action.
        var board = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        board.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        board.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        board.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        (StackPanel Host, TextBlock Count) KitchenColumn(string title, string accentBrush, int column)
        {
            var count = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 2, 0, 0) };
            count.SetResourceReference(TextBlock.ForegroundProperty, accentBrush);
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 12) };
            header.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Bold });
            header.Children.Add(count);
            var host = new StackPanel();
            var dock = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);
            dock.Children.Add(new ScrollViewer
            {
                Content = host,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
            var card = new Border
            {
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(column == 0 ? 0 : 8, 0, column == 2 ? 0 : 8, 0),
                Padding = new Thickness(14),
                Child = dock,
            };
            card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            Grid.SetColumn(card, column);
            board.Children.Add(card);
            return (host, count);
        }

        var (newQueueColumn, newQueueCount) = KitchenColumn("New", "BrandPrimaryBrush", 0);
        var (cookingColumn, cookingCount) = KitchenColumn("Cooking", "WarningBrush", 1);
        var (readyColumn, readyCount) = KitchenColumn("Ready for pickup", "SuccessBrush", 2);
        root.Children.Add(board);

        var timerBindings = new List<(TextBlock Label, Border Badge, TextBlock State, DateTimeOffset QueuedAt, string Priority, string Status)>();
        // Skip background KDS polling while a kitchen state transition is
        // active to avoid SQLite writer/reader lock contention.
        var kitchenWritesInProgress = 0;
        var kitchenViewActive = true;

        void RefreshStationChoices()
        {
            var selected = (stationFilter.SelectedItem as Choice)?.Id ?? "";
            var choices = new List<Choice> { new("", "All stations") };
            choices.AddRange(rows
                .GroupBy(row => new { row.StationId, row.Station })
                .OrderBy(group => group.Key.Station)
                .Select(group => new Choice(group.Key.StationId, group.Key.Station)));

            stationFilter.ItemsSource = choices;
            stationFilter.SelectedItem =
                choices.FirstOrDefault(choice => choice.Id == selected) ?? choices[0];
        }

        void UpdateAge(
            TextBlock label,
            Border badge,
            TextBlock state,
            DateTimeOffset queuedAt,
            string priority,
            string currentStatus)
        {
            // Running age only makes sense while food is being prepared.
            // READY must never display days of elapsed time as if a chef were
            // still preparing it; the card remains in pickup until served.
            if (currentStatus is "ready" or "completed")
            {
                label.Text = currentStatus == "ready" ? "PICKUP" : "SERVED";
            }
            else
            {
                var age = DateTimeOffset.UtcNow - queuedAt;
                var totalSeconds = Math.Max(0, (long)age.TotalSeconds);
                var hours = totalSeconds / 3600;
                var minutesPart = (totalSeconds / 60) % 60;
                var secondsPart = totalSeconds % 60;
                label.Text = hours > 0
                    ? $"{hours:00}:{minutesPart:00}:{secondsPart:00}"
                    : $"{minutesPart:00}:{secondsPart:00}";
            }
            var minutes = Math.Max(0, (int)Math.Floor((DateTimeOffset.UtcNow - queuedAt).TotalMinutes));

            var delayed = (currentStatus is "queued" or "active" or "preparing" or "expo") &&
                          minutes >= settings.KitchenLateMinutes;
            state.Text = delayed
                ? "DELAYED"
                : currentStatus switch
                {
                    "queued" => "NEW",
                    "active" => "ACCEPTED",
                    "preparing" => "PREPARING",
                    "expo" => "EXPO",
                    "ready" => "READY",
                    "completed" => "COMPLETED",
                    _ => currentStatus.ToUpperInvariant(),
                };

            if (priority == "rush" || delayed)
            {
                badge.Background = Brushes.IndianRed;
                label.Foreground = Brushes.White;
                if (delayed)
                {
                    state.Foreground = Brushes.IndianRed;
                }
            }
            else if (minutes >= settings.KitchenWarningMinutes &&
                     currentStatus is not ("ready" or "completed"))
            {
                badge.Background = Brushes.Goldenrod;
                label.Foreground = Brushes.White;
            }
            else
            {
                badge.Background = Brushes.Transparent;
                label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                state.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            }
        }

        Border BuildKitchenCard(KitchenItemCard row)
        {
            var panel = new StackPanel();
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = HeaderText($"{row.KotNumber} · R{row.RoundNumber}", 16, true);
            var meta = new TextBlock
            {
                Text = $"{row.Station} · {row.ServiceLabel}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 8, 0),
            };
            meta.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");

            var titleStack = new StackPanel();
            titleStack.Children.Add(title);
            titleStack.Children.Add(meta);
            Grid.SetColumn(titleStack, 0);
            header.Children.Add(titleStack);

            var timerText = new TextBlock
            {
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(8, 3, 8, 3),
            };
            var timerBadge = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(4),
                Child = timerText,
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
            };
            var stateChip = KitchenChip(row.Status.ToUpperInvariant());
            var stateText = (TextBlock)stateChip.Child;
            UpdateAge(timerText, timerBadge, stateText, row.QueuedAt, row.Priority, row.Status);
            timerBindings.Add((timerText, timerBadge, stateText, row.QueuedAt, row.Priority, row.Status));
            Grid.SetColumn(timerBadge, 1);
            header.Children.Add(timerBadge);
            panel.Children.Add(header);

            var itemTitle = new TextBlock
            {
                Text = $"{row.Quantity} × {row.ItemName}",
                FontSize = 21,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 14, 0, 6),
                TextWrapping = TextWrapping.Wrap,
            };
            itemTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            panel.Children.Add(itemTitle);

            var chips = new WrapPanel();
            // The ready column already says "Ready for pickup"; repeating a
            // READY chip on every pickup card is extra text, so it is skipped.
            if (row.Status != "ready")
            {
                chips.Children.Add(stateChip);
            }
            if (row.Priority == "rush") chips.Children.Add(KitchenChip("RUSH"));
            if (row.SeatNumber.HasValue) chips.Children.Add(KitchenChip($"Seat {row.SeatNumber.Value}"));
            if (row.CourseNumber.HasValue)
            {
                chips.Children.Add(KitchenChip(
                    $"Course {row.CourseNumber.Value}{(string.IsNullOrWhiteSpace(row.CourseName) ? "" : $" · {row.CourseName}")}"));
            }
            panel.Children.Add(chips);

            // Ready cards stay lean: the column header ("Ready for pickup") and
            // the READY chip already say everything the runner needs, and the
            // pickup handoff lives once in the board's handoff panel above.
            // (Completed tickets never load on the live board.)

            if (!string.IsNullOrWhiteSpace(row.Modifiers))
                panel.Children.Add(KitchenDetail("Modifiers", row.Modifiers));
            if (!string.IsNullOrWhiteSpace(row.KitchenInstructions))
                panel.Children.Add(KitchenDetail("Kitchen", row.KitchenInstructions));
            if (!string.IsNullOrWhiteSpace(row.Notes))
                panel.Children.Add(KitchenDetail("Note", row.Notes));
            if (!string.IsNullOrWhiteSpace(row.AllergyInstructions))
            {
                var allergy = KitchenDetail("ALLERGY", row.AllergyInstructions);
                allergy.Foreground = Brushes.IndianRed;
                allergy.FontWeight = FontWeights.Bold;
                panel.Children.Add(allergy);
            }
            if (!string.IsNullOrWhiteSpace(row.RefireReason))
            {
                var refire = KitchenDetail("REFIRE", row.RefireReason);
                refire.Foreground = Brushes.OrangeRed;
                refire.FontWeight = FontWeights.Bold;
                panel.Children.Add(refire);
            }
            if (!string.IsNullOrWhiteSpace(row.RecallReason))
                panel.Children.Add(KitchenDetail("RECALLED", row.RecallReason));
            if (!string.IsNullOrWhiteSpace(row.WasteReason))
            {
                var waste = KitchenDetail("WASTE", row.WasteReason);
                waste.Foreground = Brushes.IndianRed;
                waste.FontWeight = FontWeights.Bold;
                panel.Children.Add(waste);
            }

            var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };

            Button? primary = null;
            Func<CancellationToken, Task>? primaryAction = null;
            string? success = null;

            if (canProduce && row.Status is "queued" or "active" or "preparing" or "expo")
            {
                primary = Button("READY");
                primary.Name = "KitchenReadyItemButton";
                primary.ToolTip = "F2 · Mark this dish ready and alert the assigned waiter";
                primaryAction = token => workflow.MarkKitchenItemReadyDirectAsync(row.ItemId, token);
                success = $"{row.ItemName} is READY. Waiter pickup alert will appear on the mobile device after sync.";
            }

            if (primary is not null && primaryAction is not null)
            {
                actions.Children.Add(primary);
                primary.Click += async (_, _) =>
                {
                    if (!primary.IsEnabled) return;
                    primary.IsEnabled = false;
                    primary.Content = "UPDATING…";
                    statusText.Text = "Updating KOT. Other controls remain available.";
                    kitchenWritesInProgress++;
                    try
                    {
                        await DesktopUiWork.RunBoundedAsync(primaryAction, TimeSpan.FromSeconds(25));
                        statusText.Text = success ?? "Kitchen item updated.";
                        DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, statusText.Text);
                        knownReadyIds.Add(row.ItemId);
                        if (settings.KotSoundEnabled)
                        {
                            OperationalAlertSounds.OrderReady();
                        }
                    }
                    catch (OperationCanceledException ex)
                    {
                        App.LogRecoverableException("kitchen-transition-timeout", ex);
                        // A timed-out SQLite write could still have committed.
                        // Do not re-enable Start/Ready until the board refreshes.
                        primary.Content = "CHECK KOT STATUS";
                        statusText.Text = "The kitchen update took too long. Check the KOT state after refreshing before retrying.";
                        DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, statusText.Text);
                    }
                    catch (Exception ex)
                    {
                        App.LogRecoverableException("kitchen-transition-failed", ex);
                        primary.IsEnabled = true;
                        primary.Content = "READY · RETRY";
                        statusText.Text = ex.Message;
                        DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message);
                    }
                    finally { kitchenWritesInProgress--; }
                };
            }

            // Kitchen operators need one action only. Recall, waste and
            // re-fire remain available to audited management workflows/API;
            // no secondary production controls clutter the KOT board.

            if (!canProduce && !canExpo && !canRefire && !canRecallWaste)
            {
                var readOnly = KitchenDetail("Role", "Read-only kitchen visibility");
                readOnly.FontStyle = FontStyles.Italic;
                actions.Children.Add(readOnly);
            }

            panel.Children.Add(actions);

            var card = Card(panel);
            card.Width = 350;
            card.MinHeight = 270;
            card.Margin = new Thickness(0, 0, 12, 12);
            if (row.Priority == "rush")
            {
                card.BorderBrush = Brushes.OrangeRed;
                card.BorderThickness = new Thickness(2);
            }
            return card;
        }

        void UpdateKitchenSummary()
        {
            var stationId = (stationFilter.SelectedItem as Choice)?.Id ?? "";
            var state = statusFilter.SelectedItem?.ToString() ?? "All states";
            var visible = rows
                .Where(row => string.IsNullOrWhiteSpace(stationId) || row.StationId == stationId)
                .Where(row => state == "All states" || row.Status == state)
                .ToArray();

            var active = visible.Count(row => row.Status is "queued" or "active" or "preparing");
            var expo = visible.Count(row => row.Status == "expo");
            var ready = visible.Count(row => row.Status == "ready");
            var rush = visible.Count(row => row.Priority == "rush" &&
                row.Status is not ("completed" or "ready"));
            var overdue = visible.Count(row =>
                (row.Status is "queued" or "active" or "preparing" or "expo") &&
                DateTimeOffset.UtcNow - row.QueuedAt >=
                    TimeSpan.FromMinutes(settings.KitchenLateMinutes));

            kitchenSummary.Text =
                $"{visible.Length} visible · {active} in production · " +
                $"{expo} expo · {ready} ready · {rush} rush · {overdue} delayed";
            handoffText.Text = ready > 0
                ? settings.ReadyOnlyKotEnabled
                    ? $"{ready} READY FOR PICKUP • Waiter alerts are delivered through connected mobile/LAN sync."
                    : $"{ready} READY FOR PICKUP • The waiter receives an in-app alert after LAN sync; background push requires cloud connectivity. " +
                      "RECALL, WASTE and RE-FIRE are exception actions requiring a reason."
                : (settings.ReadyOnlyKotEnabled
                    ? "Kitchen flow: New KOT → READY → Waiter pickup → Served → Billing."
                    : settings.ExpoEnabled
                        ? "Kitchen flow: New KOT → START → TO EXPO → PASS EXPO → Waiter pickup."
                        : "Kitchen flow: New KOT → START → READY → Waiter pickup.");
        }

        void RenderBoard()
        {
            newQueueColumn.Children.Clear();
            cookingColumn.Children.Clear();
            readyColumn.Children.Clear();
            timerBindings.Clear();

            var stationId = (stationFilter.SelectedItem as Choice)?.Id ?? "";
            var state = statusFilter.SelectedItem?.ToString() ?? "All states";
            var filtered = rows
                .Where(row => string.IsNullOrWhiteSpace(stationId) || row.StationId == stationId)
                .Where(row => state == "All states" || row.Status == state)
                .OrderByDescending(row => row.Priority == "rush")
                .ThenBy(row => row.QueuedAt)
                .ToArray();

            foreach (var row in filtered)
            {
                var card = BuildKitchenCard(row);
                switch (row.Status)
                {
                    case "queued":
                    case "active":
                        newQueueColumn.Children.Add(card);
                        break;
                    case "preparing":
                    case "expo":
                        cookingColumn.Children.Add(card);
                        break;
                    case "ready":
                        readyColumn.Children.Add(card);
                        break;
                    default:
                        newQueueColumn.Children.Add(card);
                        break;
                }
            }

            newQueueCount.Text = newQueueColumn.Children.Count.ToString();
            cookingCount.Text = cookingColumn.Children.Count.ToString();
            readyCount.Text = readyColumn.Children.Count.ToString();

            UpdateKitchenSummary();

            if (filtered.Length == 0)
            {
                foreach (var emptyColumn in new[] { newQueueColumn, cookingColumn, readyColumn })
                {
                    var empty = new TextBlock
                    {
                        Text = "No tickets in this stage.",
                        Margin = new Thickness(2, 14, 0, 14),
                    };
                    empty.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
                    emptyColumn.Children.Add(empty);
                }
            }
        }

        (string Label, Func<CancellationToken, Task>? Action, string Success) KitchenNextStep(KitchenItemCard row)
        {
            // Single operational action, independent of legacy kitchen round flags.
            return ("READY", token => workflow.MarkKitchenItemReadyDirectAsync(row.ItemId, token),
                $"{row.ItemName} ready for waiter pickup. Mobile alert follows the next LAN/cloud sync.");
        }

        void RefreshSelectedDishes()
        {
            var oldId = (selectedDish.SelectedItem as Choice)?.Id;
            var options = rows.Where(x => x.Status is "queued" or "active" or "preparing" or "expo")
                .Where(x => canProduce)
                .OrderByDescending(x => x.Priority == "rush")
                .ThenBy(x => x.QueuedAt)
                .Select(x => new Choice(x.ItemId,
                    $"{x.ServiceLabel} · {x.KotNumber} · {x.ItemName} ×{x.Quantity} · {KitchenNextStep(x).Label}"))
                .ToArray();
            selectedDish.ItemsSource = options;
            selectedDish.SelectedItem = options.FirstOrDefault(x => x.Id == oldId)
                ?? options.FirstOrDefault();
            var selected = rows.FirstOrDefault(x => x.ItemId == (selectedDish.SelectedItem as Choice)?.Id);
            markReady.Content = selected is null ? "READY" : KitchenNextStep(selected).Label;
            markReady.IsEnabled = selected is not null && KitchenNextStep(selected).Action is not null;
        }

        async Task ReloadKitchenBoardAsync()
        {
            var latestRows = await DesktopUiWork.RunAsync(() => LoadKitchenBoardRowsAsync(factory));
            if (!kitchenViewActive) return;
            rows.Clear();
            rows.AddRange(latestRows);
            RefreshStationChoices();
            RefreshSelectedDishes();
            RenderBoard();
            knownReadyIds.UnionWith(
                rows.Where(row => row.Status == "ready").Select(row => row.ItemId));
        }

        markReady.Click += async (_, _) =>
        {
            if (!markReady.IsEnabled || selectedDish.SelectedItem is not Choice selected)
                return;
            var dish = rows.FirstOrDefault(x => x.ItemId == selected.Id);
            if (dish is null) return;
            markReady.IsEnabled = false;
            kitchenWritesInProgress++;
            try
            {
                var action = KitchenNextStep(dish);
                if (action.Action is null) return;
                await DesktopUiWork.RunBoundedAsync(action.Action, TimeSpan.FromSeconds(25));
                statusText.Text = action.Success;
                knownReadyIds.Add(dish.ItemId);
                if (settings.KotSoundEnabled)
                {
                    OperationalAlertSounds.OrderReady();
                }
                await ReloadKitchenBoardAsync();
            }
            catch (Exception ex)
            {
                App.LogRecoverableException("kitchen-topbar-ready", ex);
                statusText.Text = "Check KOT status before retrying: " + ex.Message;
            }
            finally
            {
                kitchenWritesInProgress--;
                RefreshSelectedDishes();
            }
        };
        boardRefresh.Click += async (_, _) =>
        {
            boardRefresh.IsEnabled = false;
            try { await ReloadKitchenBoardAsync(); }
            catch (Exception ex) { statusText.Text = "KOT refresh: " + ex.Message; }
            finally { boardRefresh.IsEnabled = true; }
        };

        RefreshStationChoices();
        RefreshSelectedDishes();
        selectedDish.SelectionChanged += (_, _) =>
        {
            var selected = rows.FirstOrDefault(x => x.ItemId == (selectedDish.SelectedItem as Choice)?.Id);
            markReady.Content = selected is null ? "READY" : KitchenNextStep(selected).Label;
            markReady.IsEnabled = selected is not null &&
                KitchenNextStep(selected).Action is not null;
        };
        stationFilter.SelectionChanged += (_, _) => RenderBoard();
        statusFilter.SelectionChanged += (_, _) => RenderBoard();
        RenderBoard();

        if (settings.KotSoundEnabled &&
            rows.Any(row => (row.Status is "queued" or "active") &&
                            DateTimeOffset.UtcNow - row.QueuedAt < TimeSpan.FromMinutes(1)))
        {
            OperationalAlertSounds.OrderPlaced();
        }

        var ageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        ageTimer.Tick += (_, _) =>
        {
            UpdateKitchenSummary();
            foreach (var binding in timerBindings)
            {
                UpdateAge(
                    binding.Label,
                    binding.Badge,
                    binding.State,
                    binding.QueuedAt,
                    binding.Priority,
                    binding.Status);
            }
        };

        var refreshInProgress = false;
        var knownIds = rows.Select(row => row.ItemId).ToHashSet(StringComparer.Ordinal);
        var boardSignature = KitchenBoardSignature(rows);
        var pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        pollTimer.Tick += async (_, _) =>
        {
            if (refreshInProgress || kitchenWritesInProgress > 0 || !kitchenViewActive)
            {
                return;
            }

            refreshInProgress = true;
            try
            {
                var result = await DesktopUiWork.RunAsync(async () =>
                {
                    var nextRows = await LoadKitchenBoardRowsAsync(factory).ConfigureAwait(false);
                    var nextSettings = await settingsService.GetAsync().ConfigureAwait(false);
                    return (Rows: nextRows, Settings: nextSettings);
                });
                if (!kitchenViewActive) return;
                var latestRows = result.Rows;
                var latestSettings = result.Settings;
                var latestSignature = KitchenBoardSignature(latestRows);

                if (!string.Equals(boardSignature, latestSignature, StringComparison.Ordinal) ||
                    settings != latestSettings)
                {
                    var newProduction = latestRows
                        .Where(row => !knownIds.Contains(row.ItemId) &&
                                      row.Status is "queued" or "active")
                        .ToArray();
                    // A dish marked ready on another LAN terminal (tablet or a
                    // second desktop) must alert the expo/runner on this board.
                    var newlyReady = latestRows
                        .Where(row => row.Status == "ready" &&
                                      !knownReadyIds.Contains(row.ItemId))
                        .ToArray();

                    rows.Clear();
                    rows.AddRange(latestRows);
                    settings = latestSettings;
                    knownIds = rows.Select(row => row.ItemId).ToHashSet(StringComparer.Ordinal);
                    knownReadyIds = rows
                        .Where(row => row.Status == "ready")
                        .Select(row => row.ItemId)
                        .ToHashSet(StringComparer.Ordinal);
                    boardSignature = latestSignature;

                    RefreshStationChoices();
                    RefreshSelectedDishes();
                    RenderBoard();

                    if (settings.KotSoundEnabled && newProduction.Length > 0)
                    {
                        OperationalAlertSounds.OrderPlaced();
                    }

                    if (settings.KotSoundEnabled && newlyReady.Length > 0)
                    {
                        OperationalAlertSounds.OrderReady();
                    }
                }
            }
            catch (Exception ex)
            {
                statusText.Text = $"KDS refresh: {ex.Message}";
            }
            finally
            {
                refreshInProgress = false;
            }
        };

        ageTimer.Start();
        pollTimer.Start();
        root.Unloaded += (_, _) =>
        {
            kitchenViewActive = false;
            ageTimer.Stop();
            pollTimer.Stop();
        };

        // Fixed action toolbar and independently scrolling KOT cards.
        var kitchenHeader = root.Children[0];
        root.Children.RemoveAt(0);
        root.Children.Remove(filterRow);
        var actionToolbar = new StackPanel { Name = "KotTopActionToolbar", Margin = new Thickness(0, 0, 0, 8) };
        actionToolbar.Children.Add(kitchenHeader);
        actionToolbar.Children.Add(filterRow);
        var toolbarGlass = Card(actionToolbar);
        var shell = new Grid { Name = "KotTopbarAndContent" };
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(toolbarGlass, 0);
        shell.Children.Add(toolbarGlass);
        var contentScroll = new ScrollViewer
        {
            Name = "KotIndependentContentScroll",
            Content = WorkspaceFrostedSurface.Wrap(root),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(contentScroll, 1);
        shell.Children.Add(contentScroll);
        return shell;
    }

    public static async Task<FrameworkElement> ClosingAsync()
    {
        // Materialize all SQLite query results on a background thread before
        // constructing any WPF controls. SQLite async providers may block
        // synchronously while waiting for locks.
        var pageData = await DesktopUiWork.RunAsync(async () =>
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

            return (branches, sessions, closings);
        });
        var (branches, sessions, closings) = pageData;

        var workflow = new DesktopRestaurantWorkflowService();
        var root = new StackPanel();
        root.Children.Add(Header("Daily closing", "Restaurant end-of-day control. Close cashier sessions and staff shifts before finalizing the business date."));

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,12) };
        var branchBox = Combo(branches, "Label"); branchBox.Width = 220;
        var dateBox = new TextBox { Text = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"), Width = 130, Height = 34, Margin = new Thickness(8,4,8,6) };
        var finalize = Button("Finalize day");
        var status = new TextBlock { Margin = new Thickness(12,10,0,0), TextWrapping = TextWrapping.Wrap };
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        controls.Children.Add(branchBox); controls.Children.Add(dateBox); controls.Children.Add(finalize); controls.Children.Add(status);
        finalize.Click += async (_, _) =>
        {
            try
            {
                if (branchBox.SelectedItem is not Choice branch) throw new InvalidOperationException("Select a branch.");
                if (!DateOnly.TryParse(dateBox.Text, out var date)) throw new InvalidOperationException("Enter a valid business date.");
                if (!ConfirmOperationalChange(
                    $"Finalize business date {date:yyyy-MM-dd} for {branch.Label}? Verify cashier and waiter shifts have closed.",
                    "Confirm end-of-day closing"))
                    return;
                finalize.IsEnabled = false;
                try
                {
                    await DesktopUiWork.RunAsync(() => workflow.FinalizeDailyClosingAsync(branch.Id, date));
                    status.Text = $"Business date {date:yyyy-MM-dd} finalized.";
                    DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, status.Text);
                }
                finally
                {
                    finalize.IsEnabled = true;
                }
            }
            catch (Exception ex) { status.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message); }
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
        // Keep action labels, statuses, confirmations and safety warnings intact,
        // but avoid a second paragraph below every workspace title.
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(HeaderText(title, 20, true));
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

    /// <summary>
    /// Table-first ordering: tap a floor tile to open its live order here —
    /// add dishes, adjust guests (up to the table capacity) and send the KOT
    /// without leaving the floor. Reuses the same local workflow service and
    /// idempotent mutations as POS, so no duplicate orders or KOT rounds occur.
    /// </summary>
    private static async Task<bool> OpenTableOrderModalAsync(
        TableChoice? table,
        string serviceType,
        TableOrderChoice? floorOrder,
        TextBlock parentStatus,
        bool allowCheckout = false,
        bool refreshParent = true,
        string? quickAddItemId = null,
        IReadOnlyList<Choice>? branches = null)
    {
        var factory = new LocalDatabaseFactory();
        var workflow = new DesktopRestaurantWorkflowService();

        var modalData = await DesktopUiWork.RunAsync(async () =>
        {
            await factory.EnsureCreatedAsync();
            await using var db = factory.Create();
            var menu = await (from item in db.MenuItems.AsNoTracking()
                              join category in db.MenuCategories.AsNoTracking() on item.MenuCategoryId equals category.Id into groups
                              from category in groups.DefaultIfEmpty()
                              where item.IsAvailable
                              orderby category == null ? "" : category.Name, item.SortOrder, item.Name
                              select new TableMenuChoice(item.Id, item.Name,
                                  category == null ? "Uncategorized" : category.Name, item.Price)).ToListAsync();
            var order = table is null
                ? null
                : (await db.Orders.AsNoTracking()
                    .Where(x => x.DiningTableId == table.Id &&
                                x.ServiceType == "dine_in" &&
                                x.Status != "closed" && x.Status != "cancelled")
                    .Select(x => new { x.ClientOrderId, x.Status, x.GuestCount, x.Total, x.DisplayNumber, x.UpdatedAtUtc })
                    .ToListAsync())
                    .OrderByDescending(x => x.UpdatedAtUtc)
                    .FirstOrDefault();
            return (Menu: menu, Order: order);
        });

        var isTableOrder = table is not null;
        var capacity = table?.Capacity ?? 100;
        var windowTitle = isTableOrder
            ? $"Table order · {table!.Name}"
            : serviceType switch
            {
                "delivery" => "Delivery order",
                "counter" => "Counter order",
                _ => "Takeaway order",
            };
        var clientOrderId = modalData.Order?.ClientOrderId;
        var orderStatus = modalData.Order?.Status ?? "available";
        var billed = orderStatus == "billed";
        var guests = Math.Max(1, modalData.Order?.GuestCount ?? floorOrder?.Guests ?? 1);
        var busy = false;
        var changed = false;

        var dialog = new Window
        {
            Title = windowTitle,
            Name = "TableOrderDialog",
            Width = 1020,
            Height = 740,
            MinWidth = 820,
            MinHeight = 560,
            Owner = System.Windows.Application.Current?.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
        };

        var statusLine = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
        var orderCaption = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var guestLabel = new TextBlock { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) };
        var guestMinus = Button("−");
        var guestPlus = Button("+");
        foreach (var guestButton in new[] { guestMinus, guestPlus })
        {
            guestButton.MinWidth = 42;
            guestButton.Height = 32;
            guestButton.Margin = new Thickness(4, 0, 4, 0);
            guestButton.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        }
        guestMinus.ToolTip = "Remove one guest";
        guestPlus.ToolTip = "Add one guest to this table";
        var guestPanel = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        guestPanel.Children.Add(new TextBlock { Text = "Guests", Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
        guestPanel.Children.Add(guestMinus);
        guestPanel.Children.Add(guestLabel);
        guestPanel.Children.Add(guestPlus);
        guestPanel.Children.Add(new TextBlock
        {
            Text = $"table seats {capacity}",
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var summaryPanel = new StackPanel();
        var totalText = new TextBlock { FontWeight = FontWeights.Bold, FontSize = 16 };
        var unsentText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
        var sendButton = Button("SEND TO KITCHEN");
        sendButton.Name = "TableOrderSendButton";
        sendButton.MinWidth = 220;
        sendButton.Height = 44;
        sendButton.ToolTip = "Send every new item to the kitchen exactly once";
        var checkoutRequested = false;
        var openPos = Button(allowCheckout ? "Checkout & payments" : "Open in POS");
        openPos.Name = "TableOrderCheckoutButton";
        if (!allowCheckout)
        {
            openPos.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        }
        openPos.MinWidth = allowCheckout ? 200 : 120;
        var close = Button("Close");
        close.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        close.MinWidth = 90;

        // Service orders (takeaway/delivery/counter) open without a table but
        // still need a branch and an optional customer reference. The values
        // are mirrored into plain strings because the workflow action executes
        // on a background thread and must not read WPF controls.
        ComboBox? branchBox = null;
        TextBox? referenceBox = null;
        var serviceBranchId = branches?.FirstOrDefault()?.Id ?? string.Empty;
        var serviceReference = string.Empty;
        async Task<string> EnsureOrderAsync(CancellationToken token)
        {
            if (!string.IsNullOrWhiteSpace(clientOrderId))
            {
                return clientOrderId!;
            }

            if (table is not null)
            {
                clientOrderId = await workflow.OpenOrderAsync(table.Id, guests, null, token);
            }
            else
            {
                clientOrderId = await workflow.OpenOrderAsync(
                    serviceType, null, serviceBranchId,
                    serviceReference.Length == 0 ? null : serviceReference,
                    guests, null, token);
            }

            return clientOrderId!;
        }

        void RefreshGuestLabel()
        {
            guestLabel.Text = $"{guests} / {capacity}";
            guestMinus.IsEnabled = !billed && !busy && guests > 1;
            guestPlus.IsEnabled = !billed && !busy && guests < capacity;
        }

        async Task<IReadOnlyList<TableLineChoice>> LoadLinesAsync()
        {
            var orderId = clientOrderId;
            if (string.IsNullOrWhiteSpace(orderId))
            {
                return [];
            }

            return await DesktopUiWork.RunAsync(async () =>
            {
                await using var db = factory.Create();
                var localOrderId = await db.Orders.AsNoTracking()
                    .Where(x => x.ClientOrderId == orderId)
                    .Select(x => x.Id)
                    .FirstOrDefaultAsync();
                if (localOrderId is null)
                {
                    return (IReadOnlyList<TableLineChoice>)[];
                }

                return (IReadOnlyList<TableLineChoice>)(await db.OrderItems.AsNoTracking()
                    .Where(x => x.OrderId == localOrderId &&
                                x.Status != "voided" && x.Status != "cancelled")
                    .Select(x => new { x.ClientLineId, x.ItemName, x.Quantity, x.LineTotal, x.Status, x.KotRoundId, x.CreatedAtUtc })
                    .ToListAsync())
                    .OrderBy(x => x.CreatedAtUtc)
                    .Select(x => new TableLineChoice(
                        x.ClientLineId, x.ItemName, x.Quantity, x.LineTotal,
                        x.KotRoundId == null && (x.Status == "pending" || x.Status == "held")))
                    .ToList();
            });
        }

        async Task RefreshSummaryAsync()
        {
            var lines = await LoadLinesAsync();
            summaryPanel.Children.Clear();

            if (lines.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = "No items yet. Tap a dish on the left to build the order.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 8),
                };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
                summaryPanel.Children.Add(empty);
            }

            foreach (var line in lines)
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var text = new TextBlock
                {
                    Text = $"{line.Quantity} × {line.ItemName}{(line.Unsent ? "  · NOT SENT" : "  · sent")}",
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                if (line.Unsent)
                {
                    text.SetResourceReference(TextBlock.ForegroundProperty, "BrandPrimaryBrush");
                }
                Grid.SetColumn(text, 0);
                row.Children.Add(text);

                var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                right.Children.Add(new TextBlock
                {
                    Text = $"AFN {line.LineTotal:N2}",
                    Margin = new Thickness(8, 0, 6, 0),
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                if (!billed)
                {
                    var remove = Button("×");
                    remove.MinWidth = 30;
                    remove.Height = 26;
                    remove.Margin = new Thickness(0);
                    remove.Padding = new Thickness(0);
                    remove.ToolTip = line.Unsent
                        ? $"Remove {line.ItemName} (audited void)"
                        : $"Cancel {line.ItemName} — voids its kitchen ticket item";
                    remove.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
                    var captured = line;
                    remove.Click += async (_, _) =>
                    {
                        var reason = captured.Unsent
                            ? "Removed while ordering at the table"
                            : PromptAuditReason(
                                $"Cancel {captured.ItemName}",
                                $"Cancel {captured.Quantity} × {captured.ItemName}? It was already sent to the kitchen:\n" +
                                "the KOT item is voided and any started production is recorded as waste.",
                                "Cancelled at the table");
                        if (reason is null)
                        {
                            return;
                        }

                        await RunActionAsync(
                            remove,
                            token => workflow.VoidOrderItemAsync(
                                clientOrderId!, captured.ClientLineId, reason, token),
                            captured.Unsent ? $"Removed {captured.ItemName}." : $"Cancelled {captured.ItemName}.");
                    };
                    right.Children.Add(remove);
                }
                Grid.SetColumn(right, 1);
                row.Children.Add(right);
                summaryPanel.Children.Add(row);
            }

            totalText.Text = $"Order total: AFN {lines.Sum(x => x.LineTotal):N2}";
            var unsent = lines.Count(x => x.Unsent);
            unsentText.Text = unsent == 0
                ? "Nothing waiting to be sent to the kitchen."
                : $"{unsent} item(s) not sent yet.";
            unsentText.SetResourceReference(TextBlock.ForegroundProperty,
                unsent == 0 ? "TextMutedBrush" : "BrandPrimaryBrush");
            sendButton.IsEnabled = !busy && !billed && unsent > 0;
        }

        async Task RunActionAsync(Button? trigger, Func<CancellationToken, Task> action, string success)
        {
            if (busy)
            {
                return;
            }

            busy = true;
            if (trigger is not null)
            {
                trigger.IsEnabled = false;
            }
            RefreshGuestLabel();
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                await DesktopUiWork.RunBoundedAsync(action, TimeSpan.FromSeconds(25));
                changed = true;
                statusLine.Text = success;
                statusLine.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
            }
            catch (Exception ex)
            {
                App.LogRecoverableException("table-order-modal", ex);
                statusLine.Text = ex.Message;
                statusLine.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
            }
            finally
            {
                busy = false;
                if (trigger is not null)
                {
                    trigger.IsEnabled = true;
                }
                await RefreshSummaryAsync();
                RefreshGuestLabel();
            }
        }

        // Menu area: search + category chips + paged dish tiles.
        var search = new TextBox { Height = 36, MinWidth = 260, Margin = new Thickness(0, 4, 8, 4) };
        search.SetResourceReference(Control.BackgroundProperty, "InputBackgroundBrush");
        System.Windows.Automation.AutomationProperties.SetName(search, "Search dishes for this table");
        search.ToolTip = "Search dishes for this table";
        dialog.Loaded += (_, _) => search.Focus();
        var categoryRow = new WrapPanel();
        var tilesPanel = new WrapPanel();
        var menuItems = modalData.Menu;
        var activeCategory = "All";

        void RenderMenu()
        {
            tilesPanel.Children.Clear();
            var text = search.Text.Trim();
            var matching = menuItems
                .Where(x => (activeCategory == "All" ||
                             string.Equals(x.Category, activeCategory, StringComparison.OrdinalIgnoreCase)) &&
                            (text.Length == 0 ||
                             x.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                             x.Category.Contains(text, StringComparison.OrdinalIgnoreCase)))
                .Take(80)
                .ToArray();

            foreach (var item in matching)
            {
                var name = new TextBlock
                {
                    Text = item.Name,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 40,
                };
                var meta = new TextBlock
                {
                    Text = $"{item.Category} · AFN {item.Price:N0}  ·  + Add",
                    FontSize = 11,
                    Margin = new Thickness(0, 4, 0, 0),
                };
                meta.SetResourceReference(TextBlock.ForegroundProperty, "BrandPrimaryBrush");
                var stack = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
                stack.Children.Add(name);
                stack.Children.Add(meta);

                var tile = new Button
                {
                    Content = stack,
                    Width = 202,
                    MinHeight = 76,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 8, 8),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    IsEnabled = !billed,
                };
                tile.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
                var captured = item;
                tile.Click += async (_, _) => await RunActionAsync(tile, async token =>
                {
                    await EnsureOrderAsync(token);
                    await workflow.AddItemAsync(clientOrderId!, captured.Id, 1, null, token);
                }, $"Added {captured.Name}.");
                tilesPanel.Children.Add(tile);
            }

            if (matching.Length == 0)
            {
                var none = new TextBlock { Text = "No dishes match this search.", Margin = new Thickness(2, 6, 0, 6) };
                none.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
                tilesPanel.Children.Add(none);
            }
        }

        guestMinus.Click += async (_, _) => await RunActionAsync(guestMinus, async token =>
        {
            var next = Math.Max(1, guests - 1);
            if (clientOrderId is not null)
            {
                await workflow.UpdateGuestsAsync(clientOrderId, next, token);
            }
            guests = next;
        }, "Guest count updated.");

        guestPlus.Click += async (_, _) => await RunActionAsync(guestPlus, async token =>
        {
            var next = Math.Min(capacity, guests + 1);
            if (clientOrderId is not null)
            {
                await workflow.UpdateGuestsAsync(clientOrderId, next, token);
            }
            guests = next;
        }, "Guest count updated.");

        sendButton.Click += async (_, _) =>
        {
            if (clientOrderId is null)
            {
                return;
            }

            await RunActionAsync(sendButton,
                token => workflow.SendKotAsync(clientOrderId, token),
                "New items sent to the kitchen · next: Checkout & payments when ready.");
        };

        openPos.Click += (_, _) =>
        {
            if (allowCheckout)
            {
                checkoutRequested = true;
                dialog.DialogResult = true;
                return;
            }

            dialog.DialogResult = true;
            var vm = System.Windows.Application.Current?.MainWindow?.DataContext as MainWindowViewModel;
            if (vm?.NavigateCommand.CanExecute("pos") == true)
            {
                vm.NavigateCommand.Execute("pos");
            }
        };
        close.Click += (_, _) => dialog.DialogResult = false;

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = isTableOrder ? table!.Name : windowTitle,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
        });
        var subtitle = new TextBlock
        {
            Text = isTableOrder
                ? $"{table!.Area} · Table {table.Code} · {table.Capacity} seats"
                : "No table needed — add dishes, send the kitchen round and checkout.",
            Margin = new Thickness(0, 3, 0, 6),
        };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        header.Children.Add(subtitle);

        if (!isTableOrder && branches is { Count: > 1 })
        {
            var serviceRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
            serviceRow.Children.Add(new TextBlock
            {
                Text = "Branch", VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
            branchBox = new ComboBox
            {
                ItemsSource = branches,
                DisplayMemberPath = "Label",
                MinWidth = 210,
                Height = 34,
                Margin = new Thickness(0, 0, 14, 0),
            };
            branchBox.SelectedItem = branches[0];
            branchBox.SelectionChanged += (_, _) =>
            {
                if (branchBox.SelectedItem is Choice choice)
                {
                    serviceBranchId = choice.Id;
                }
            };
            serviceRow.Children.Add(branchBox);
            serviceRow.Children.Add(new TextBlock
            {
                Text = "Customer / reference", VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
            referenceBox = new TextBox { MinWidth = 200, Height = 34 };
            referenceBox.TextChanged += (_, _) => serviceReference = referenceBox.Text.Trim();
            System.Windows.Automation.AutomationProperties.SetName(referenceBox, "Takeaway or delivery reference");
            serviceRow.Children.Add(referenceBox);
            header.Children.Add(serviceRow);
        }

        var displayNumber = modalData.Order?.DisplayNumber ?? 0;
        var orderLabel = displayNumber > 0 ? $"#{displayNumber}" : clientOrderId;
        orderCaption.Text = clientOrderId is null
            ? isTableOrder
                ? "New table order — the order opens with the first dish."
                : $"New {windowTitle.ToLowerInvariant()} — the order opens with the first dish."
            : $"Order {orderLabel} · {orderStatus.ToUpperInvariant()}" +
              (billed ? $" · unpaid bill AFN {modalData.Order!.Total:N2}" : "");
        orderCaption.SetResourceReference(TextBlock.ForegroundProperty, billed ? "ErrorBrush" : "TextSecondaryBrush");
        header.Children.Add(orderCaption);
        if (isTableOrder)
        {
            header.Children.Add(guestPanel);
        }
        if (billed)
        {
            var warning = new TextBlock
            {
                Text = "This table has an unpaid bill. Settle it in POS before adding items or changing guests.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
                FontWeight = FontWeights.SemiBold,
            };
            warning.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
            header.Children.Add(warning);
        }

        var menuToolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        menuToolbar.Children.Add(search);
        var categoryScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, 6),
            Content = categoryRow,
        };
        var left = new DockPanel { Margin = new Thickness(0, 0, 14, 0) };
        DockPanel.SetDock(menuToolbar, Dock.Top);
        left.Children.Add(menuToolbar);
        DockPanel.SetDock(categoryScroller, Dock.Top);
        left.Children.Add(categoryScroller);
        left.Children.Add(new ScrollViewer
        {
            Content = tilesPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        var right = new DockPanel { Width = 330, Margin = new Thickness(6, 0, 0, 0) };
        var rightTitle = new TextBlock { Text = "Order summary", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(rightTitle, Dock.Top);
        right.Children.Add(rightTitle);
        var rightFooter = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        rightFooter.Children.Add(totalText);
        rightFooter.Children.Add(unsentText);
        rightFooter.Children.Add(sendButton);
        DockPanel.SetDock(rightFooter, Dock.Bottom);
        right.Children.Add(rightFooter);
        summaryPanel.Margin = new Thickness(0, 0, 0, 8);
        // The scrolling summary is added last so DockPanel fills the middle
        // area between the title and the pinned send footer.
        right.Children.Add(new ScrollViewer
        {
            Content = summaryPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 200,
        });

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.Children.Add(left);
        Grid.SetColumn(right, 1);
        body.Children.Add(right);

        var footer = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        footer.Children.Add(openPos);
        footer.Children.Add(close);
        statusLine.Margin = new Thickness(0, 8, 0, 0);

        var rootGrid = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(header, Dock.Top);
        rootGrid.Children.Add(header);
        DockPanel.SetDock(statusLine, Dock.Bottom);
        rootGrid.Children.Add(statusLine);
        DockPanel.SetDock(footer, Dock.Bottom);
        rootGrid.Children.Add(footer);
        rootGrid.Children.Add(body);
        dialog.Content = rootGrid;

        foreach (var category in new[] { "All" }
            .Concat(menuItems.Select(x => x.Category).Distinct(StringComparer.OrdinalIgnoreCase)))
        {
            var chip = Button(category);
            chip.MinWidth = 0;
            chip.Height = 30;
            chip.Margin = new Thickness(0, 0, 6, 6);
            chip.Padding = new Thickness(10, 0, 10, 0);
            chip.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
            var captured = category;
            chip.Click += (_, _) =>
            {
                activeCategory = captured;
                RenderMenu();
            };
            categoryRow.Children.Add(chip);
        }

        search.TextChanged += (_, _) => RenderMenu();
        RenderMenu();
        await RefreshSummaryAsync();
        RefreshGuestLabel();

        // A dish tapped on the POS page opens this modal already carrying it.
        if (!string.IsNullOrWhiteSpace(quickAddItemId) && !billed)
        {
            var quickItem = menuItems.FirstOrDefault(x => x.Id == quickAddItemId);
            if (quickItem is not null)
            {
                await RunActionAsync(null, async token =>
                {
                    await EnsureOrderAsync(token);
                    await workflow.AddItemAsync(clientOrderId!, quickItem.Id, 1, null, token);
                }, $"Added {quickItem.Name}.");
            }
        }

        DesktopGlassDialog.ShowDialog(dialog);

        if (changed && parentStatus is not null && refreshParent)
        {
            parentStatus.Text = $"{table?.Name ?? windowTitle}: order updated; floor refreshed.";
            if (System.Windows.Application.Current?.MainWindow?.DataContext is MainWindowViewModel vm)
            {
                await vm.RefreshCommand.ExecuteAsync(null);
            }
        }

        return checkoutRequested;
    }

    private static FrameworkElement BuildVisualFloorBoard(
        IReadOnlyList<TableChoice> tables,
        IReadOnlyList<TableOrderChoice> activeOrders,
        IReadOnlyList<TableOrderChoice> transferableOrders,
        IReadOnlyList<Choice> availableTables,
        ComboBox sourceOrderBox,
        ComboBox targetTableBox,
        TextBlock statusText,
        bool groupByArea = true)
    {
        var floor = new StackPanel { Margin = new Thickness(0, 10, 0, 12) };
        // Colour key doubles as live status counts, like the dining-room design.
        var freeTables = tables.Count(x => x.Status == "available");
        var seatedTables = tables.Count(x => x.Status == "occupied" &&
            activeOrders.FirstOrDefault(o => o.TableId == x.Id)?.Status != "billed");
        var waitingTables = tables.Count(x => activeOrders.FirstOrDefault(o => o.TableId == x.Id)?.Status == "billed");
        var reservedTables = tables.Count(x => x.Status == "reserved");
        var legend = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        void LegendEntry(string count, string label, Color color)
        {
            legend.Children.Add(new TextBlock
            {
                Text = "●", Foreground = new SolidColorBrush(color), FontSize = 14,
                Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
            });
            var text = new TextBlock
            {
                Text = $"{count} {label}",
                Margin = new Thickness(0, 0, 18, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            legend.Children.Add(text);
        }
        LegendEntry(freeTables.ToString(), "free", Color.FromRgb(22, 155, 98));
        LegendEntry(seatedTables.ToString(), "seated", Color.FromRgb(217, 119, 6));
        LegendEntry(reservedTables.ToString(), "reserved", Color.FromRgb(37, 99, 235));
        LegendEntry(waitingTables.ToString(), "waiting to pay", Color.FromRgb(185, 28, 28));
        floor.Children.Add(legend);

        if (tables.Count == 0)
        {
            floor.Children.Add(new TextBlock
            {
                Text = "No active dining tables yet. Configure the restaurant floor in the tenant platform.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 12),
            });
            return floor;
        }

        foreach (var area in groupByArea
            ? tables.GroupBy(x => x.Area)
            : tables.GroupBy(_ => "All tables"))
        {
            var areaTitle = new TextBlock
            {
                Text = area.Key, FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 6, 0, 9),
            };
            areaTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            floor.Children.Add(areaTitle);

            var tiles = new WrapPanel();
            foreach (var table in area)
            {
                var order = activeOrders.FirstOrDefault(x => x.TableId == table.Id);
                var waitingToPay = order?.Status == "billed";
                var (stripeColor, label, labelColor) = table.Status switch
                {
                    "available" => (Color.FromRgb(22, 155, 98), "Free", Color.FromRgb(22, 155, 98)),
                    "occupied" when waitingToPay => (Color.FromRgb(185, 28, 28), "Waiting to pay", Color.FromRgb(185, 28, 28)),
                    "occupied" => (Color.FromRgb(217, 119, 6), "Seated", Color.FromRgb(180, 83, 9)),
                    "reserved" => (Color.FromRgb(37, 99, 235), "Reserved", Color.FromRgb(37, 99, 235)),
                    _ => (Color.FromRgb(100, 116, 139), table.Status, Color.FromRgb(100, 116, 139)),
                };
                var tint = new SolidColorBrush(Color.FromArgb(24, stripeColor.R, stripeColor.G, stripeColor.B));

                var labelText = new TextBlock
                {
                    Text = table.Name,
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                labelText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
                var seats = new TextBlock
                {
                    Text = $"{table.Capacity} seats",
                    FontSize = 11,
                    Margin = new Thickness(0, 4, 0, 12),
                };
                seats.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
                var status = new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(labelColor),
                };
                var detailText = new TextBlock
                {
                    Text = order is null
                        ? (table.Status == "reserved" ? "Reserved" : "Ready to seat")
                        : $"{order.Guests} guests · {order.ShortNumber} · AFN {order.Total:N0}",
                    FontSize = 11,
                    Margin = new Thickness(0, 6, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                detailText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

                var body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
                body.Children.Add(labelText);
                body.Children.Add(RestaurantTableFurniture.Create(table.Name, table.Capacity, stripeColor));
                body.Children.Add(status);
                if (order is not null)
                {
                    body.Children.Add(detailText);
                }

                // Coloured status stripe plus a soft tint, matching the dining-room mock-up.
                var stripe = new Border
                {
                    Width = 6,
                    Background = new SolidColorBrush(stripeColor),
                };
                var layout = new DockPanel();
                DockPanel.SetDock(stripe, Dock.Left);
                layout.Children.Add(stripe);
                layout.Children.Add(body);

                var surface = new Border
                {
                    Width = 196, MinHeight = 124, CornerRadius = new CornerRadius(12),
                    BorderThickness = new Thickness(1),
                    Background = tint,
                    ClipToBounds = true,
                    Child = layout,
                };
                surface.SetResourceReference(Border.BorderBrushProperty, "FloorTileBorderBrush");

                var tile = new Button
                {
                    Content = surface,
                    Padding = new Thickness(0),
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Margin = new Thickness(0, 0, 11, 11),
                    ToolTip = "Tap to open this table's order: add dishes, add guests and send to the kitchen",
                };
                tile.Click += async (_, _) =>
                {
                    // Preserve the transfer/merge convenience selection, then open
                    // the live table order so staff can order without leaving the floor.
                    if (order is not null)
                    {
                        sourceOrderBox.SelectedItem = transferableOrders.FirstOrDefault(x => x.Id == order.Id);
                    }
                    else if (table.Status == "available")
                    {
                        targetTableBox.SelectedItem = availableTables.FirstOrDefault(x => x.Id == table.Id);
                    }

                    await OpenTableOrderModalAsync(table, "dine_in", order, statusText);
                };
                tiles.Children.Add(tile);
            }
            var areaSurface = new Border
            {
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12),
                Child = tiles,
            };
            areaSurface.SetResourceReference(Border.BackgroundProperty, "FloorAreaBackgroundBrush");
            areaSurface.SetResourceReference(Border.BorderBrushProperty, "FloorTileBorderBrush");
            floor.Children.Add(areaSurface);
        }

        return floor;
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

    /// <summary>
    /// Workstation chrome that rides with each operational page: Keys and Full
    /// screen sit at the right edge of the page's own action row (POS payment
    /// buttons, Kitchen filter row) instead of the global top bar. The buttons
    /// talk to the shell through <see cref="OperationalWorkspaceChrome"/>.
    /// </summary>
    private static (Button Keys, Button FullScreen) BuildWorkspaceChromeButtons()
    {
        var keys = Button("⌨  Keys");
        keys.Name = "WorkspaceKeysButton";
        keys.ToolTip = "F1 · Show this page's keyboard shortcuts";
        var fullScreen = Button("⛶  Full screen");
        fullScreen.Name = "WorkspaceFullScreenButton";
        fullScreen.ToolTip = "F11 · Toggle full screen for this workstation";
        foreach (var button in new[] { keys, fullScreen })
        {
            button.MinHeight = 40;
            button.MinWidth = 122;
            button.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        }
        keys.Click += (_, _) => OperationalWorkspaceChrome.RequestShortcuts();
        fullScreen.Click += (_, _) => OperationalWorkspaceChrome.RequestFullScreenToggle();
        return (keys, fullScreen);
    }

    /// <summary>
    /// Audited reason prompt used when cancelling a single dish. Works for
    /// items still waiting to be sent and for items already sent to the kitchen
    /// (post-KOT void). Returns null when the operator keeps the item, closes
    /// the prompt or leaves the reason empty.
    /// </summary>
    private static string? PromptAuditReason(string title, string prompt, string initialReason)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 470,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            Owner = System.Windows.Application.Current?.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var content = new StackPanel { Margin = new Thickness(22) };
        var message = new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap };
        content.Children.Add(message);
        var reason = new TextBox
        {
            Text = initialReason,
            Margin = new Thickness(0, 12, 0, 0),
            MinHeight = 36,
        };
        System.Windows.Automation.AutomationProperties.SetName(reason, "Cancellation reason");
        content.Children.Add(reason);
        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var confirm = Button("Cancel item");
        confirm.Name = "VoidReasonConfirmButton";
        confirm.MinWidth = 140;
        confirm.IsDefault = true;
        var keep = Button("Keep item");
        keep.IsCancel = true;
        keep.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        actions.Children.Add(confirm);
        actions.Children.Add(keep);
        content.Children.Add(actions);

        var accepted = false;
        confirm.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(reason.Text))
            {
                message.Text = "Enter a short reason so the cancellation is auditable.";
                reason.Focus();
                return;
            }

            accepted = true;
            dialog.DialogResult = true;
        };
        keep.Click += (_, _) => dialog.DialogResult = false;
        dialog.Loaded += (_, _) =>
        {
            reason.Focus();
            reason.SelectAll();
        };
        dialog.Content = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        DesktopGlassDialog.ShowDialog(dialog);
        return accepted ? reason.Text.Trim() : null;
    }

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
            BlurRadius = 8,
            ShadowDepth = 2,
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
        grid.SetResourceReference(System.Windows.Controls.DataGrid.BackgroundProperty, "SurfaceBrush");
        grid.SetResourceReference(System.Windows.Controls.DataGrid.BorderBrushProperty, "BorderBrush");
        return grid;
    }
    private static async Task<List<KitchenItemCard>> LoadKitchenBoardRowsAsync(
        LocalDatabaseFactory factory)
    {
        await using var db = factory.Create();

        var recentCompletedCutoff = DateTimeOffset.UtcNow.AddHours(-2);
        // SQLite cannot translate relational comparisons or ordering for
        // DateTimeOffset. Keep the status predicate server-side, then evaluate
        // the completed-ticket age and stable KDS ordering in managed code.
        // The previous LINQ query prevented Kitchen from opening at all.
        // Completed (served) tickets never appear on the live board.
        var ticketCandidates = await db.KitchenTickets
            .AsNoTracking()
            .Where(x => x.Status == "queued" ||
                        x.Status == "active" ||
                        x.Status == "preparing" ||
                        x.Status == "expo" ||
                        x.Status == "ready")
            .ToArrayAsync();
        var tickets = ticketCandidates
            .Where(x => x.Status != "completed" || x.CompletedAt >= recentCompletedCutoff)
            .OrderByDescending(x => x.Priority == "rush")
            .ThenBy(x => x.QueuedAt)
            .ToArray();

        // A closed or cancelled order must never strand a KOT card: the board
        // only shows work that still belongs to a live order. This also clears
        // cards left over from before settlement cascaded ticket cleanup.
        var boardOrderIds = tickets.Select(x => x.OrderId).Distinct().ToArray();
        var closedOrderIds = boardOrderIds.Length == 0
            ? []
            : (await db.Orders.AsNoTracking()
                    .Where(x => boardOrderIds.Contains(x.Id) &&
                                (x.Status == "closed" || x.Status == "cancelled"))
                    .Select(x => x.Id)
                    .ToArrayAsync())
                .ToHashSet(StringComparer.Ordinal);
        if (closedOrderIds.Count > 0)
        {
            tickets = tickets.Where(x => !closedOrderIds.Contains(x.OrderId)).ToArray();
        }

        var ticketIds = tickets.Select(x => x.Id).ToArray();
        var ticketItems = ticketIds.Length == 0
            ? []
            : await db.KitchenTicketItems
                .AsNoTracking()
                .Where(x => ticketIds.Contains(x.KitchenTicketId) &&
                            x.Status != "voided" &&
                            x.Status != "cancelled")
                .ToArrayAsync();

        var stationIds = tickets.Select(x => x.KitchenStationId).Distinct().ToArray();
        var stations = stationIds.Length == 0
            ? new Dictionary<string, LocalKitchenStation>(StringComparer.Ordinal)
            : await db.KitchenStations
                .AsNoTracking()
                .Where(x => stationIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, StringComparer.Ordinal);

        var orderIds = tickets.Select(x => x.OrderId).Distinct().ToArray();
        var orders = orderIds.Length == 0
            ? new Dictionary<string, LocalOrder>(StringComparer.Ordinal)
            : await db.Orders
                .AsNoTracking()
                .Where(x => orderIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, StringComparer.Ordinal);

        var tableIds = orders.Values
            .Where(x => !string.IsNullOrWhiteSpace(x.DiningTableId))
            .Select(x => x.DiningTableId)
            .Distinct()
            .ToArray();
        var tables = tableIds.Length == 0
            ? new Dictionary<string, LocalDiningTable>(StringComparer.Ordinal)
            : await db.DiningTables
                .AsNoTracking()
                .Where(x => tableIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, StringComparer.Ordinal);

        var roundIds = tickets
            .Where(x => !string.IsNullOrWhiteSpace(x.KotRoundId))
            .Select(x => x.KotRoundId!)
            .Distinct()
            .ToArray();
        var rounds = roundIds.Length == 0
            ? new Dictionary<string, LocalKotRound>(StringComparer.Ordinal)
            : await db.KotRounds
                .AsNoTracking()
                .Where(x => roundIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, StringComparer.Ordinal);

        var rows = new List<KitchenItemCard>();
        foreach (var ticket in tickets)
        {
            if (!stations.TryGetValue(ticket.KitchenStationId, out var station))
            {
                continue;
            }

            orders.TryGetValue(ticket.OrderId, out var order);
            LocalKotRound? round = null;
            if (!string.IsNullOrWhiteSpace(ticket.KotRoundId))
            {
                rounds.TryGetValue(ticket.KotRoundId!, out round);
            }

            var serviceLabel = order?.ServiceType switch
            {
                "takeaway" => $"Takeaway · {order.ServiceReference ?? order.ClientOrderId}",
                "delivery" => $"Delivery · {order.ServiceReference ?? order.ClientOrderId}",
                "counter" => $"Counter · {order.ServiceReference ?? order.ClientOrderId}",
                _ when order is not null &&
                       !string.IsNullOrWhiteSpace(order.DiningTableId) &&
                       tables.TryGetValue(order.DiningTableId, out var table)
                    => table.Name.StartsWith("Table", StringComparison.OrdinalIgnoreCase)
                        ? table.Name
                        : $"Table {table.Name}",
                _ => "Dine-in",
            };

            foreach (var item in ticketItems.Where(x => x.KitchenTicketId == ticket.Id))
            {
                rows.Add(new KitchenItemCard(
                    item.Id,
                    ticket.Id,
                    ticket.KotNumber ?? ticket.TicketNumber,
                    ticket.RoundNumber,
                    station.Id,
                    station.Name,
                    serviceLabel,
                    item.ItemName,
                    item.Quantity,
                    item.Notes,
                    item.AllergyInstructions,
                    item.KitchenInstructions,
                    ModifierText(item.ModifiersJson),
                    item.SeatNumber,
                    item.CourseNumber,
                    item.CourseName,
                    item.Status,
                    item.Priority,
                    ticket.QueuedAt,
                    round?.QueueEnabled ?? true,
                    round?.PreparingEnabled ?? true,
                    round?.ExpoEnabled ?? false,
                    item.RefireReason,
                    item.RecallReason,
                    item.WasteReason));
            }
        }

        return rows;
    }

    private static string KitchenBoardSignature(IEnumerable<KitchenItemCard> rows) =>
        string.Join(
            "|",
            rows
                .OrderBy(row => row.ItemId, StringComparer.Ordinal)
                .Select(row =>
                    $"{row.ItemId}:{row.Status}:{row.Priority}:{row.RoundNumber}:{row.ExpoEnabled}:{row.PreparingEnabled}:{row.RecallReason}:{row.WasteReason}"));

    private static Border KitchenChip(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        var chip = new Border
        {
            Child = block,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 6, 6),
            BorderThickness = new Thickness(1),
        };
        chip.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        chip.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return chip;
    }

    private static TextBlock KitchenDetail(string label, string value)
    {
        var block = new TextBlock
        {
            Text = $"{label}: {value}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return block;
    }

    private static string? ModifierText(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            if (root.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var values = new List<string>();
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var raw = item.GetString();
                    if (!string.IsNullOrWhiteSpace(raw)) values.Add(raw);
                    continue;
                }

                if (item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("name", out var name) &&
                    !string.IsNullOrWhiteSpace(name.GetString()))
                {
                    values.Add(name.GetString()!);
                }
            }

            return values.Count == 0 ? null : string.Join(", ", values);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DataGridTextColumn Column(string header, string property, double width) => new() { Header = header, Binding = new System.Windows.Data.Binding(property), Width = width };

    private sealed record Choice(string Id, string Label);
    private sealed record MenuChoice(string Id, string Name, decimal Price, string Category, string? ImageUrl)
    {
        public string Display => $"{Name} — AFN {Price:N2}";
    }
    private sealed record ModifierChoice(string MenuItemId, string OptionId, string Name, decimal PriceDelta)
    {
        public string Display => PriceDelta == 0m ? Name : $"{Name} ({PriceDelta:+0.##;-0.##} AFN)";
    }
    private sealed record OrderLineChoice(string ClientOrderId, string ClientLineId, string ItemName, int Quantity, string Status, int? RoundNumber)
    {
        public string Display => $"{Quantity} × {ItemName} · {Status}{(RoundNumber.HasValue ? $" · R{RoundNumber}" : "")}";
    }
    private sealed record OrderChoice(string Id, string ClientOrderId, string ServiceType, string Waiter, string Status, int Guests, decimal Total, int DisplayNumber)
    {
        public string Display => DisplayNumber > 0
            ? $"#{DisplayNumber} · {ServiceType} · {Status}"
            : ClientOrderId;
    }
    private sealed record BillChoice(string Id, string OrderId, string Number, decimal Total, decimal Paid, decimal Balance) { public string Display => $"{Number} — AFN {Balance:N2} due"; }
    private sealed record TableChoice(string Id, string Area, string Code, string Name, int Capacity, string Status, string? ReservationNote = null);
    private sealed record TableOrderChoice(string Id, string ClientOrderId, string TableId, string Table, string Status, decimal Total, int Guests, int DisplayNumber)
    {
        public string Display => DisplayNumber > 0
            ? $"#{DisplayNumber} · {Table} · {Status} · {Guests} guest(s) · AFN {Total:N2}"
            : $"{ClientOrderId} · {Table} · {Status} · {Guests} guest(s) · AFN {Total:N2}";
        public string ShortNumber => DisplayNumber > 0 ? $"#{DisplayNumber}" : ClientOrderId;
    }
    private sealed record TableMenuChoice(string Id, string Name, string Category, decimal Price);
    private sealed record TableLineChoice(string ClientLineId, string ItemName, int Quantity, decimal LineTotal, bool Unsent);
    private sealed record TableSplitLineChoice(string OrderId, string OrderItemId, string ItemName, int Quantity, string Status)
    {
        public string Display => $"{Quantity} × {ItemName} · {Status}";
    }
    private sealed record KitchenItemCard(
        string ItemId,
        string TicketId,
        string KotNumber,
        int RoundNumber,
        string StationId,
        string Station,
        string ServiceLabel,
        string ItemName,
        int Quantity,
        string? Notes,
        string? AllergyInstructions,
        string? KitchenInstructions,
        string? Modifiers,
        int? SeatNumber,
        int? CourseNumber,
        string? CourseName,
        string Status,
        string Priority,
        DateTimeOffset QueuedAt,
        bool QueueEnabled,
        bool PreparingEnabled,
        bool ExpoEnabled,
        string? RefireReason,
        string? RecallReason,
        string? WasteReason);
    private sealed record CashierSessionChoice(string Id, string Cashier, string Status, decimal OpeningCash, decimal? ExpectedCash, decimal? DeclaredCash, decimal? Variance, DateTimeOffset OpenedAt);
    private sealed record ClosingChoice(string Id, DateOnly BusinessDate, string Status, DateTimeOffset? FinalizedAt);
}
