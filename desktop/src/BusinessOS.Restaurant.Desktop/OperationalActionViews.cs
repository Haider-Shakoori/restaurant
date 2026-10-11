using System.Media;
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
                    .Select(x => new { Row = new OrderChoice(x.Id, x.ClientOrderId, x.ServiceType, x.WaiterName, x.Status, x.GuestCount, x.Total), x.UpdatedAtUtc })
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
            // SQLite cannot translate ORDER BY on DateTimeOffset; sort the projected rows in memory.
            var orderLines = (await (
                from line in db.OrderItems.AsNoTracking()
                join order in db.Orders.AsNoTracking() on line.OrderId equals order.Id
                where order.Status != "closed" && order.Status != "cancelled" &&
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

            return (factory, tables, menu, modifierChoices, orders, branches, sessions, bills, orderLines);
        });
        var (factory, tables, menu, modifierChoices, orders, branches, sessions, bills, orderLines) = pageData;

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
                menuBox.SelectedItem = menu.FirstOrDefault(x => x.Id == item.Id);
                if (menuBox.SelectedItem is MenuChoice)
                    add.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
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
        grid.Columns.Add(Column("Order", nameof(OrderChoice.ClientOrderId), 220));
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
        var orderBox = Combo(orders, "ClientOrderId"); orderBox.Width = 360;
        var billBox = Combo(bills, "Display"); billBox.Width = 360; billBox.Name = "PosBillPicker";
        var paymentMethod = new ComboBox { ItemsSource = new[] { "cash", "card", "bank", "mobile_money", "other" }, SelectedIndex = 0, Height = 34, Width = 160, Margin = new Thickness(0,4,8,6) };
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
        amountField.Children.Add(Label("Amount (AFN)"));
        amountField.Children.Add(amountBox);
        var methodField = new StackPanel();
        methodField.Children.Add(Label("Payment method"));
        methodField.Children.Add(paymentMethod);
        checkoutFields.Children.Add(amountField);
        checkoutFields.Children.Add(methodField);
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
                var method = paymentMethod.SelectedItem?.ToString() ?? "cash";
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
                status.Text = "Order opened locally. Add items, or resume it later from the active orders list.";
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, status.Text);
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
                if (string.IsNullOrWhiteSpace(orderIdBox.Text)) throw new InvalidOperationException("Open an order first.");
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
                status.Text = $"{qty} × {item.Name} added. Send KOT when this round is ready.";
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, status.Text);
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
                : $"{order.ServiceType.Replace('_', ' ')} · {order.Status} · AFN {order.Total:N2} · " +
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
                                x.Status, x.GuestCount, x.Total),
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
        var kitchenModal = Button("KOT actions");
        kitchenModal.Name = "PosOpenKitchenModal";
        kitchenModal.ToolTip = "F7 · Kitchen changes, course firing and voids";
        kitchenModal.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        var billingModal = Button("Payments");
        billingModal.Name = "PosOpenPaymentModal";
        billingModal.ToolTip = "F8 · Payments and billing";
        billingModal.MinHeight = 42;

        var quickActions = new WrapPanel
        {
            Name = "PosTopActions", Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
        };
        foreach (var button in new[] { newOrderModal, activeOrdersModal, viewTicketModal, editItemsModal })
        {
            button.MinHeight = 40;
            quickActions.Children.Add(button);
        }
        kitchenActions.Children.Remove(submit);
        submit.MinHeight = 40;
        quickActions.Children.Add(submit);
        kitchenModal.MinHeight = 40;
        quickActions.Children.Add(kitchenModal);
        if (cashierCard is not null) quickActions.Children.Add(billingModal);

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
                ShadowDepth = 8, BlurRadius = 30, Opacity = 0.36,
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
        var modalBody = new StackPanel();
        modalBody.Children.Add(formCard);
        modalBody.Children.Add(ordersCard);
        modalBody.Children.Add(ticketCard);
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
                "payment" => "Payments & billing",
                "items" => "Item options & KOT",
                "kitchen" => "Kitchen adjustments",
                _ => "New order & setup",
            };
            modalDescription.Text = section switch
            {
                "orders" => "Find a ticket, resume it, or start another order.",
                "ticket" => "Review the current dishes and total. Use Send KOT in the top toolbar.",
                "payment" => "One cashier · serve, bill and collect payment without changing screens.",
                "items" => "Adjust quantity, modifiers, and kitchen notes before sending a KOT.",
                "kitchen" => "Fire courses, void individual items, or cancel an order with an audit reason.",
                _ => "Choose service type and table, then open your order.",
            };
            dialogPreferredWidth = section switch
            {
                "payment" => 670d,
                "ticket" => 590d,
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
        newOrderModal.Click += (_, _) => OpenModal("order");
        activeOrdersModal.Click += (_, _) => OpenModal("orders");
        viewTicketModal.Click += (_, _) => OpenModal("ticket");
        editItemsModal.Click += (_, _) => OpenModal("items");
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
        return posHost;
    }


    public static async Task<FrameworkElement> TablesAsync()
    {
        // Materialize all SQLite query results on a background thread before
        // constructing any WPF controls. SQLite async providers may block
        // synchronously while waiting for locks.
        var pageData = await DesktopUiWork.RunAsync(async () =>
        {
            var factory = new LocalDatabaseFactory();
            await factory.EnsureCreatedAsync();
            await using var db = factory.Create();

            var rows = await (from table in db.DiningTables.AsNoTracking()
                              join area in db.DiningAreas.AsNoTracking() on table.DiningAreaId equals area.Id
                              join branch in db.Branches.AsNoTracking() on area.BranchId equals branch.Id
                              where table.IsActive && area.IsActive && branch.IsActive
                              orderby branch.Name, area.SortOrder, table.Name
                              select new TableChoice(table.Id, branch.Name + " · " + area.Name,
                                  table.Code, table.Name, table.Capacity, table.Status))
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
                        order.Status, order.Total),
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
                        order.DiningTableId, "", order.Status, order.Total),
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

            return (rows, activeOrders, unsentLines, allFloorOrders, availableTables, areas, branches);
        });
        var (rows, activeOrders, unsentLines, allFloorOrders, availableTables, areas, branches) = pageData;

        var workflow = new DesktopRestaurantWorkflowService();
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
        root.Children.Add(floorActions);

        var grid = DataGrid(rows);
        grid.MinHeight = 280;
        grid.Columns.Add(Column("Area", nameof(TableChoice.Area), 160));
        grid.Columns.Add(Column("Table", nameof(TableChoice.Name), 220));
        grid.Columns.Add(Column("Code", nameof(TableChoice.Code), 100));
        grid.Columns.Add(Column("Seats", nameof(TableChoice.Capacity), 80));
        grid.Columns.Add(Column("Status", nameof(TableChoice.Status), 130));
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
        return new ScrollViewer
        {
            Content = WorkspaceFrostedSurface.Wrap(root),
            Margin = new Thickness(12, 8, 12, 16),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    public static async Task<FrameworkElement> KitchenAsync()
    {
        var factory = new LocalDatabaseFactory();
        await DesktopUiWork.RunAsync(() => factory.EnsureCreatedAsync());

        var settingsService = new LocalRestaurantSettingsService(factory);
        var settings = await DesktopUiWork.RunAsync(() => settingsService.GetAsync());
        var rows = await DesktopUiWork.RunAsync(() => LoadKitchenBoardRowsAsync(factory));

        var workflow = new DesktopRestaurantWorkflowService();
        var principal = await DesktopUiWork.RunAsync(() => workflow.CurrentPrincipalAsync());
        var canProduce = principal.UserRole is "owner" or "manager" or "kitchen";
        var canExpo = principal.UserRole is "owner" or "manager" or "expo";
        var canRefire = principal.UserRole is "owner" or "manager" or "kitchen";
        var canRecallWaste = canRefire;

        var root = new StackPanel();
        root.Children.Add(Header(
            "Kitchen / KOT",
            "Tap READY when food can be picked up. The waiter sees the pickup alert in the mobile app after synchronization."));

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
            DisplayMemberPath = "ItemName",
            Width = 225, Height = 36,
            Margin = new Thickness(6, 4, 8, 6),
            ToolTip = "Choose the KOT dish that is ready for pickup",
        };
        var markReady = Button("READY");
        markReady.Name = "KotTopReadyButton";
        markReady.MinHeight = 42;
        markReady.ToolTip = "F2 · Notify waiter that selected dish is ready for pickup";
        var boardRefresh = Button("Refresh board");
        boardRefresh.Name = "KotTopRefreshButton";
        boardRefresh.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
        boardRefresh.ToolTip = "F5 · Reload the kitchen board";
        filterRow.Children.Add(selectedDish);
        filterRow.Children.Add(markReady);
        filterRow.Children.Add(boardRefresh);
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

        var board = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
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
            chips.Children.Add(stateChip);
            if (row.Priority == "rush") chips.Children.Add(KitchenChip("RUSH"));
            if (row.SeatNumber.HasValue) chips.Children.Add(KitchenChip($"Seat {row.SeatNumber.Value}"));
            if (row.CourseNumber.HasValue)
            {
                chips.Children.Add(KitchenChip(
                    $"Course {row.CourseNumber.Value}{(string.IsNullOrWhiteSpace(row.CourseName) ? "" : $" · {row.CourseName}")}"));
            }
            panel.Children.Add(chips);

            if (row.Status == "ready")
            {
                var pickup = KitchenDetail(
                    "NEXT STEP",
                    "Ready for pickup. The waiter delivers the food, then marks the complete order SERVED in POS & Orders. Billing follows service.");
                pickup.FontWeight = FontWeights.SemiBold;
                pickup.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
                panel.Children.Add(pickup);
            }
            else if (row.Status == "completed")
            {
                panel.Children.Add(KitchenDetail(
                    "SERVED",
                    "The order was marked served in POS. Keep this ticket for its audit history."));
            }

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

            var actionReason = new TextBox
            {
                MinWidth = 300,
                Height = 34,
                Margin = new Thickness(0, 8, 8, 4),
                ToolTip = "Reason for recall, waste or re-fire",
            };
            if (canRecallWaste && (row.Status is "ready" or "completed" or "expo"))
            {
                panel.Children.Add(actionReason);
            }

            var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };

            Button? primary = null;
            Func<CancellationToken, Task>? primaryAction = null;
            string? success = null;

            if (canProduce && row.Status is "queued" or "active" or "preparing" or "expo")
            {
                primary = Button("READY");
                primaryAction = token => workflow.MarkKitchenItemReadyAsync(row.ItemId, token);
                success = $"{row.ItemName} READY — the assigned waiter will see the pickup alert after sync.";
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

            if (canRecallWaste && (row.Status is "ready" or "completed"))
            {
                var recall = Button("RECALL");
                recall.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
                actions.Children.Add(recall);
                recall.Click += async (_, _) =>
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(actionReason.Text))
                            throw new InvalidOperationException("Enter a recall reason.");
                        if (!ConfirmOperationalChange(
                            $"Recall {row.ItemName} for a new kitchen handling step? Previous consumption will not be reversed.",
                            "Confirm kitchen recall"))
                            return;
                        recall.IsEnabled = false;
                        var reason = actionReason.Text.Trim();
                        kitchenWritesInProgress++;
                        try
                        {
                            await DesktopUiWork.RunBoundedAsync(
                                token => workflow.RecallKitchenItemAsync(row.ItemId, reason, token),
                                TimeSpan.FromSeconds(25));
                        }
                        finally { kitchenWritesInProgress--; }
                        statusText.Text = $"{row.ItemName} recalled to the kitchen without reversing its prior consumption.";
                        DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, statusText.Text);
                    }
                    catch (OperationCanceledException ex)
                    {
                        App.LogRecoverableException("kitchen-recall-timeout", ex);
                        statusText.Text = "KOT update timed out. Check kitchen history before attempting another action.";
                        DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, statusText.Text);
                    }
                    catch (Exception ex)
                    {
                        recall.IsEnabled = true;
                        statusText.Text = ex.Message;
                        DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message);
                    }
                };
            }

            if (canRecallWaste && (row.Status is "preparing" or "expo" or "ready" or "completed"))
            {
                var waste = Button("MARK WASTE");
                waste.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryActionButton");
                actions.Children.Add(waste);
                waste.Click += async (_, _) =>
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(actionReason.Text))
                            throw new InvalidOperationException("Enter a waste reason.");
                        if (!ConfirmOperationalChange(
                            $"Record {row.ItemName} as production waste? Consumed ingredients will not be returned.",
                            "Confirm kitchen waste"))
                            return;
                        waste.IsEnabled = false;
                        var reason = actionReason.Text.Trim();
                        kitchenWritesInProgress++;
                        try
                        {
                            await DesktopUiWork.RunBoundedAsync(
                                token => workflow.RecordKitchenWasteAsync(row.ItemId, reason, token),
                                TimeSpan.FromSeconds(25));
                        }
                        finally { kitchenWritesInProgress--; }
                        statusText.Text = $"{row.ItemName} recorded as production waste; inventory was not returned.";
                        DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, statusText.Text);
                    }
                    catch (OperationCanceledException ex)
                    {
                        App.LogRecoverableException("kitchen-waste-timeout", ex);
                        statusText.Text = "KOT update timed out. Check kitchen history before attempting another action.";
                        DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, statusText.Text);
                    }
                    catch (Exception ex)
                    {
                        waste.IsEnabled = true;
                        statusText.Text = ex.Message;
                        DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message);
                    }
                };
            }

            if (canRefire && (row.Status is "ready" or "expo" or "completed"))
            {
                var refire = Button("RE-FIRE");
                actions.Children.Add(refire);
                refire.Click += async (_, _) =>
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(actionReason.Text))
                            throw new InvalidOperationException("Enter a re-fire reason.");
                        if (!ConfirmOperationalChange(
                            $"Re-fire {row.ItemName} as a new rush production event? This records another production.",
                            "Confirm kitchen re-fire"))
                            return;
                        refire.IsEnabled = false;
                        var reason = actionReason.Text.Trim();
                        kitchenWritesInProgress++;
                        try
                        {
                            await DesktopUiWork.RunBoundedAsync(
                                token => workflow.RefireKitchenItemAsync(row.ItemId, reason, token),
                                TimeSpan.FromSeconds(25));
                        }
                        finally { kitchenWritesInProgress--; }
                        statusText.Text = $"{row.ItemName} re-fired as a new rush production event.";
                        DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, statusText.Text);
                    }
                    catch (OperationCanceledException ex)
                    {
                        App.LogRecoverableException("kitchen-refire-timeout", ex);
                        statusText.Text = "KOT update timed out. Check kitchen history before attempting another action.";
                        DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, statusText.Text);
                    }
                    catch (Exception ex)
                    {
                        refire.IsEnabled = true;
                        statusText.Text = ex.Message;
                        DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message);
                    }
                };
            }

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
                ? $"{ready} READY FOR PICKUP • The waiter receives an in-app alert after LAN sync; background push requires cloud connectivity. " +
                  "RECALL, WASTE and RE-FIRE are exception actions requiring a reason."
                : "Kitchen flow: New KOT → READY → Waiter pickup → Served → Billing. No Start step.";
        }

        void RenderBoard()
        {
            board.Children.Clear();
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
                board.Children.Add(BuildKitchenCard(row));
            }

            UpdateKitchenSummary();

            if (filtered.Length == 0)
            {
                var empty = new TextBlock
                {
                    Text = "No active kitchen items match this filter.",
                    Margin = new Thickness(0, 18, 0, 18),
                };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
                board.Children.Add(empty);
            }
        }

        void RefreshSelectedDishes()
        {
            var oldId = (selectedDish.SelectedItem as KitchenItemCard)?.ItemId;
            var options = rows.Where(x => x.Status is "queued" or "active" or "preparing" or "expo")
                .OrderByDescending(x => x.Priority == "rush")
                .ThenBy(x => x.QueuedAt).ToArray();
            selectedDish.ItemsSource = options;
            selectedDish.SelectedItem = options.FirstOrDefault(x => x.ItemId == oldId)
                ?? options.FirstOrDefault();
            markReady.IsEnabled = canProduce && options.Length > 0;
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
        }

        markReady.Click += async (_, _) =>
        {
            if (!markReady.IsEnabled || selectedDish.SelectedItem is not KitchenItemCard dish)
                return;
            markReady.IsEnabled = false;
            kitchenWritesInProgress++;
            try
            {
                await DesktopUiWork.RunBoundedAsync(
                    token => workflow.MarkKitchenItemReadyAsync(dish.ItemId, token),
                    TimeSpan.FromSeconds(25));
                statusText.Text = $"{dish.ItemName} READY for waiter pickup.";
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
        stationFilter.SelectionChanged += (_, _) => RenderBoard();
        statusFilter.SelectionChanged += (_, _) => RenderBoard();
        RenderBoard();

        if (settings.KotSoundEnabled &&
            rows.Any(row => (row.Status is "queued" or "active") &&
                            DateTimeOffset.UtcNow - row.QueuedAt < TimeSpan.FromMinutes(1)))
        {
            SystemSounds.Exclamation.Play();
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

                    rows.Clear();
                    rows.AddRange(latestRows);
                    settings = latestSettings;
                    knownIds = rows.Select(row => row.ItemId).ToHashSet(StringComparer.Ordinal);
                    boardSignature = latestSignature;

                    RefreshStationChoices();
                    RefreshSelectedDishes();
                    RenderBoard();

                    if (settings.KotSoundEnabled && newProduction.Length > 0)
                    {
                        SystemSounds.Exclamation.Play();
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
        var summary = new TextBlock
        {
            Text = $"{tables.Count(x => x.Status == "available")} available · " +
                   $"{tables.Count(x => x.Status == "occupied")} occupied · " +
                   $"{tables.Count(x => x.Status != "available" && x.Status != "occupied")} other",
            FontSize = 13, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        };
        summary.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        floor.Children.Add(summary);

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
                var stateBrush = table.Status switch
                {
                    "available" => "SuccessBrush",
                    "occupied" => "BrandPrimaryBrush",
                    _ => "TextMutedBrush",
                };

                var label = new TextBlock
                {
                    Text = table.Name,
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
                var subtitle = new TextBlock
                {
                    Text = $"Table {table.Code} · {table.Capacity} seats",
                    FontSize = 11,
                    Margin = new Thickness(0, 5, 0, 12),
                };
                subtitle.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
                var state = new TextBlock
                {
                    Text = table.Status.ToUpperInvariant(),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                };
                state.SetResourceReference(TextBlock.ForegroundProperty, stateBrush);

                var body = new StackPanel { Margin = new Thickness(13) };
                body.Children.Add(label);
                body.Children.Add(subtitle);
                body.Children.Add(state);
                if (order is not null)
                {
                    var orderCaption = new TextBlock
                    {
                        Text = $"{order.ClientOrderId} · AFN {order.Total:N2}",
                        Margin = new Thickness(0, 6, 0, 0),
                        FontSize = 10,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    };
                    orderCaption.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                    body.Children.Add(orderCaption);
                }

                var surface = new Border
                {
                    Width = 190, MinHeight = 119, CornerRadius = new CornerRadius(15),
                    BorderThickness = new Thickness(1),
                    Child = body,
                };
                // Frosted glass panels keep readable text over the restaurant
                // artwork while respecting the current light/glass theme.
                surface.SetResourceReference(Border.BackgroundProperty, "FloorTileBackgroundBrush");
                surface.SetResourceReference(Border.BorderBrushProperty, "FloorTileBorderBrush");

                var tile = new Button
                {
                    Content = surface,
                    Padding = new Thickness(0),
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Margin = new Thickness(0, 0, 11, 11),
                    ToolTip = order is null
                        ? "Available table · select as a target for transfer/split"
                        : "Occupied table · select its active order for operations",
                };
                tile.Click += (_, _) =>
                {
                    if (order is not null)
                    {
                        sourceOrderBox.SelectedItem = transferableOrders.FirstOrDefault(x => x.Id == order.Id);
                        statusText.Text = order.Status == "billed"
                            ? $"{table.Name} has an unpaid bill · AFN {order.Total:N2}. Open POS & Cashier and settle the balance before freeing the table."
                            : $"Selected {table.Name} / {order.ClientOrderId}. Choose a target table for transfer.";
                    }
                    else if (table.Status == "available")
                    {
                        targetTableBox.SelectedItem = availableTables.FirstOrDefault(x => x.Id == table.Id);
                        statusText.Text = $"Selected available table {table.Name} as transfer target.";
                    }
                    else
                    {
                        statusText.Text = $"{table.Name} is {table.Status}; review the table status before assigning it.";
                    }
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
        var ticketCandidates = await db.KitchenTickets
            .AsNoTracking()
            .Where(x => x.Status == "queued" ||
                        x.Status == "active" ||
                        x.Status == "preparing" ||
                        x.Status == "expo" ||
                        x.Status == "ready" ||
                        x.Status == "completed")
            .ToArrayAsync();
        var tickets = ticketCandidates
            .Where(x => x.Status != "completed" || x.CompletedAt >= recentCompletedCutoff)
            .OrderByDescending(x => x.Priority == "rush")
            .ThenBy(x => x.QueuedAt)
            .ToArray();

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
                    => $"Table {table.Name}",
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
    private sealed record OrderChoice(string Id, string ClientOrderId, string ServiceType, string Waiter, string Status, int Guests, decimal Total);
    private sealed record BillChoice(string Id, string OrderId, string Number, decimal Total, decimal Paid, decimal Balance) { public string Display => $"{Number} — AFN {Balance:N2} due"; }
    private sealed record TableChoice(string Id, string Area, string Code, string Name, int Capacity, string Status);
    private sealed record TableOrderChoice(string Id, string ClientOrderId, string TableId, string Table, string Status, decimal Total)
    {
        public string Display => $"{ClientOrderId} · {Table} · {Status} · AFN {Total:N2}";
    }
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
