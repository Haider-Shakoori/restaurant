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
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
        == MessageBoxResult.Yes;

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

        var workflow = new DesktopRestaurantWorkflowService();
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(440) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var form = new StackPanel();
        form.Children.Add(Header(
            "Restaurant order",
            "Open dine-in, takeaway, delivery or counter orders. Send KOT repeatedly; only unsent items go in the next round."));

        var serviceTypeBox = new ComboBox
        {
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
        var status = new TextBlock { Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };

        form.Children.Add(Label("Service type")); form.Children.Add(serviceTypeBox);
        form.Children.Add(Label("Branch (required for non-dine-in)")); form.Children.Add(orderBranchBox);
        form.Children.Add(Label("Table (required for dine-in)")); form.Children.Add(tableBox);
        form.Children.Add(Label("Takeaway / delivery / counter reference")); form.Children.Add(serviceReferenceBox);
        form.Children.Add(Label("Guests")); form.Children.Add(guestBox);
        var open = Button("Open order");
        form.Children.Add(open);
        form.Children.Add(Label("Current order")); form.Children.Add(orderIdBox);
        form.Children.Add(Label("Menu item")); form.Children.Add(menuBox);
        form.Children.Add(Label("Quantity")); form.Children.Add(qtyBox);

        var seatCourseRow = new WrapPanel();
        seatCourseRow.Children.Add(Label("Seat"));
        seatCourseRow.Children.Add(seatBox);
        seatCourseRow.Children.Add(Label("Course"));
        seatCourseRow.Children.Add(courseBox);
        seatCourseRow.Children.Add(courseNameBox);
        form.Children.Add(seatCourseRow);

        var itemFlags = new WrapPanel();
        itemFlags.Children.Add(heldBox);
        itemFlags.Children.Add(rushBox);
        form.Children.Add(itemFlags);

        form.Children.Add(Label("Modifiers")); form.Children.Add(modifiersBox);
        form.Children.Add(Label("Item note")); form.Children.Add(itemNotesBox);
        form.Children.Add(Label("Kitchen instruction")); form.Children.Add(kitchenInstructionsBox);
        form.Children.Add(Label("Allergy / special warning")); form.Children.Add(allergyBox);

        var add = Button("Add item");
        var submit = Button("Send new KOT round");
        form.Children.Add(add);
        form.Children.Add(submit);

        var courseRow = new WrapPanel();
        courseRow.Children.Add(fireCourseBox);
        var fireCourse = Button("Fire course");
        courseRow.Children.Add(fireCourse);
        form.Children.Add(courseRow);

        form.Children.Add(Label("Existing order line"));
        form.Children.Add(lineBox);
        var voidRow = new WrapPanel();
        voidRow.Children.Add(voidReasonBox);
        var voidLine = Button("Void selected line");
        var cancelOrder = Button("Cancel order");
        voidRow.Children.Add(voidLine);
        voidRow.Children.Add(cancelOrder);
        form.Children.Add(voidRow);
        form.Children.Add(status);

        open.Click += async (_, _) =>
        {
            try
            {
                var serviceType = serviceTypeBox.SelectedItem?.ToString() ?? "dine_in";
                if (!int.TryParse(guestBox.Text, out var guests)) guests = 1;

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

                orderIdBox.Text = await workflow.OpenOrderAsync(
                    serviceType,
                    tableId,
                    branchId,
                    string.IsNullOrWhiteSpace(serviceReferenceBox.Text) ? null : serviceReferenceBox.Text.Trim(),
                    guests);

                lineBox.ItemsSource = orderLines.Where(x => x.ClientOrderId == orderIdBox.Text).ToList();
                status.Text = "Order opened locally. Add items now or later; each Send KOT creates only the next unsent production round.";
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, status.Text);
            }
            catch (Exception ex) { status.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message); }
        };
        menuBox.SelectionChanged += (_, _) =>
        {
            if (menuBox.SelectedItem is MenuChoice selected)
            {
                modifiersBox.ItemsSource = modifierChoices.Where(x => x.MenuItemId == selected.Id).ToList();
            }
            else
            {
                modifiersBox.ItemsSource = Array.Empty<ModifierChoice>();
            }
        };

        add.Click += async (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(orderIdBox.Text)) throw new InvalidOperationException("Open an order first.");
                if (menuBox.SelectedItem is not MenuChoice item) throw new InvalidOperationException("Select a menu item.");
                if (!int.TryParse(qtyBox.Text, out var qty)) qty = 1;

                int? seat = int.TryParse(seatBox.Text, out var parsedSeat) ? parsedSeat : null;
                int? course = int.TryParse(courseBox.Text, out var parsedCourse) ? parsedCourse : null;
                var selectedModifiers = modifiersBox.SelectedItems
                    .Cast<ModifierChoice>()
                    .Select(x => x.OptionId)
                    .ToArray();

                var clientLineId = await workflow.AddItemDetailedAsync(
                    orderIdBox.Text,
                    item.Id,
                    qty,
                    string.IsNullOrWhiteSpace(itemNotesBox.Text) ? null : itemNotesBox.Text.Trim(),
                    seat,
                    course,
                    string.IsNullOrWhiteSpace(courseNameBox.Text) ? null : courseNameBox.Text.Trim(),
                    heldBox.IsChecked == true,
                    rushBox.IsChecked == true ? "rush" : "normal",
                    string.IsNullOrWhiteSpace(allergyBox.Text) ? null : allergyBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(kitchenInstructionsBox.Text) ? null : kitchenInstructionsBox.Text.Trim(),
                    selectedModifiers);

                var localLine = new OrderLineChoice(
                    orderIdBox.Text,
                    clientLineId,
                    item.Name,
                    qty,
                    heldBox.IsChecked == true ? "held" : "pending",
                    null);
                orderLines.Add(localLine);
                lineBox.ItemsSource = orderLines.Where(x => x.ClientOrderId == orderIdBox.Text).ToList();
                lineBox.SelectedItem = localLine;

                status.Text = $"{qty} × {item.Name} added. Send KOT when this round is ready.";
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, status.Text);
            }
            catch (Exception ex) { status.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message); }
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
                await workflow.SendKotAsync(orderIdBox.Text);
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
                await workflow.FireCourseAsync(orderIdBox.Text, courseNumber);
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
                    await workflow.VoidOrderItemAsync(line.ClientOrderId, line.ClientLineId, voidReasonBox.Text.Trim());
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
                    await workflow.CancelOrderAsync(orderIdBox.Text, voidReasonBox.Text.Trim());
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

        var grid = DataGrid(orders);
        grid.Columns.Add(Column("Order", nameof(OrderChoice.ClientOrderId), 220));
        grid.Columns.Add(Column("Service", nameof(OrderChoice.ServiceType), 110));
        grid.Columns.Add(Column("Waiter", nameof(OrderChoice.Waiter), 150));
        grid.Columns.Add(Column("Guests", nameof(OrderChoice.Guests), 80));
        grid.Columns.Add(Column("Status", nameof(OrderChoice.Status), 120));
        grid.Columns.Add(Column("Total AFN", nameof(OrderChoice.Total), 120));
        var formCard = Card(form);
        Grid.SetColumn(formCard, 0);
        Grid.SetColumn(grid, 2);
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(formCard);
        root.Children.Add(grid);
        root.SizeChanged += (_, _) =>
        {
            // On smaller Windows displays place the POS form above the orders
            // list. The grid retains horizontal scrolling for detailed columns.
            var compact = root.ActualWidth < 960;
            root.ColumnDefinitions[0].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(440);
            root.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 16);
            root.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(grid, compact ? 0 : 2);
            Grid.SetRow(grid, compact ? 1 : 0);
            grid.Margin = compact ? new Thickness(0, 12, 0, 0) : new Thickness(0);
        };

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

        openSession.Click += async (_, _) => { try { if (branchBox.SelectedItem is not Choice b) throw new InvalidOperationException("Select a branch."); if (!decimal.TryParse(openingCash.Text, out var cash)) throw new InvalidOperationException("Enter opening cash."); await workflow.OpenCashierSessionAsync(b.Id, cash); cashierStatus.Text = "Cashier session opened. Refresh to load it."; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, cashierStatus.Text); } catch (Exception ex) { cashierStatus.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, cashierStatus.Text); } };
        serve.Click += async (_, _) => { try { if (orderBox.SelectedItem is not OrderChoice o) throw new InvalidOperationException("Select an order."); await workflow.ServeOrderAsync(o.Id); cashierStatus.Text = "Order served; recipe inventory consumption recorded."; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, cashierStatus.Text); } catch (Exception ex) { cashierStatus.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, cashierStatus.Text); } };
        issue.Click += async (_, _) => { try { if (orderBox.SelectedItem is not OrderChoice o) throw new InvalidOperationException("Select an order."); await workflow.CreateBillAsync(o.Id); cashierStatus.Text = "Bill issued. Refresh to load it for payment."; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, cashierStatus.Text); } catch (Exception ex) { cashierStatus.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, cashierStatus.Text); } };
        discount.Click += async (_, _) => { try { if (billBox.SelectedItem is not BillChoice b) throw new InvalidOperationException("Select a bill."); if (!decimal.TryParse(discountBox.Text, out var value)) throw new InvalidOperationException("Enter discount percent."); await workflow.ApplyDiscountAsync(b.Id, "percent", value, "Desktop cashier discount"); cashierStatus.Text = "Discount applied."; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, cashierStatus.Text); } catch (Exception ex) { cashierStatus.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, cashierStatus.Text); } };
        split.Click += async (_, _) => { try { if (billBox.SelectedItem is not BillChoice b) throw new InvalidOperationException("Select a bill."); if (!int.TryParse(splitCountBox.Text, out var count)) throw new InvalidOperationException("Enter split count."); await workflow.CreateEqualSplitsAsync(b.Id, count); cashierStatus.Text = $"Bill split into {count} parts."; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, cashierStatus.Text); } catch (Exception ex) { cashierStatus.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, cashierStatus.Text); } };
        // AddPaymentAsync creates a fresh client payment ID per invocation.
        // Lock the button on successful posting until this cashier page is refreshed;
        // repeated clicks must never create a second payment intent.
        var paymentSubmission = new DesktopSubmissionGate();
        pay.Click += async (_, _) =>
        {
            if (!paymentSubmission.TryBegin()) return;
            pay.IsEnabled = false;
            var posted = false;
            try
            {
                if (billBox.SelectedItem is not BillChoice bill)
                    throw new InvalidOperationException("Select a bill.");
                if (sessionBox.SelectedItem is not CashierSessionChoice session)
                    throw new InvalidOperationException("Select an open cashier session.");
                if (!decimal.TryParse(amountBox.Text, out var amount) || amount <= 0)
                    throw new InvalidOperationException("Enter a positive payment amount.");
                await workflow.AddPaymentAsync(bill.Id, session.Id, amount,
                    paymentMethod.SelectedItem?.ToString() ?? "cash");
                posted = true;
                cashierStatus.Text = "Payment posted. Refresh the cashier workspace before entering another payment.";
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, cashierStatus.Text);
            }
            catch (Exception ex)
            {
                cashierStatus.Text = ex.Message;
                DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, cashierStatus.Text);
            }
            finally
            {
                paymentSubmission.Finish(lockOnSuccess: posted);
                if (!posted) pay.IsEnabled = true;
            }
        };
        receipt.Click += async (_, _) => { try { if (billBox.SelectedItem is not BillChoice b) throw new InvalidOperationException("Select a bill."); await workflow.QueueReceiptAsync(b.Id); cashierStatus.Text = "Receipt queued for the configured restaurant receipt printer."; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, cashierStatus.Text); } catch (Exception ex) { cashierStatus.Text = ex.Message; DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, cashierStatus.Text); } };

        var page = new StackPanel();
        page.Children.Add(root);
        // Waiters may enter orders but never see cashier-only billing/session
        // controls. Local service authorization remains the final action gate.
        var operatorSession = await new WindowsSessionStore().LoadAsync();
        var role = operatorSession?.User.Role?.Trim().ToLowerInvariant();
        if (role is RestaurantRoles.Owner or RestaurantRoles.Manager or RestaurantRoles.Cashier)
            page.Children.Add(Card(cashier));
        return new ScrollViewer
        {
            Content = page,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
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
                          select new TableChoice(table.Id, area.Name, table.Code, table.Name, table.Capacity, table.Status))
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

        var availableTables = rows
            .Where(x => x.Status == "available")
            .Select(x => new Choice(x.Id, $"{x.Area} · {x.Name} ({x.Code})"))
            .ToList();

        var areas = await db.DiningAreas.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync();
        var branches = await db.Branches.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.Name).ToListAsync();

        var workflow = new DesktopRestaurantWorkflowService();
        var root = new StackPanel();
        root.Children.Add(Header(
            "Dining floor",
            "Live table state shared over LAN. Transfer, merge and split preserve KOT history; split only moves lines not yet sent to production."));

        var manageFloor = Button("+ Add / Edit Floors & Tables");
        manageFloor.Margin = new Thickness(0, 6, 0, 10);
        manageFloor.Click += async (_, _) => await RestaurantOperationalPages.OpenCloudManagementAsync("tables");
        root.Children.Add(manageFloor);

        var grid = DataGrid(rows);
        grid.MinHeight = 280;
        grid.Columns.Add(Column("Area", nameof(TableChoice.Area), 160));
        grid.Columns.Add(Column("Table", nameof(TableChoice.Name), 220));
        grid.Columns.Add(Column("Code", nameof(TableChoice.Code), 100));
        grid.Columns.Add(Column("Seats", nameof(TableChoice.Capacity), 80));
        grid.Columns.Add(Column("Status", nameof(TableChoice.Status), 130));
        // The visual floor map is the primary touch view; the detailed grid
        // remains available without duplicating the underlying table records.
        grid.Visibility = Visibility.Collapsed;
        var showTableList = Button("Show table list");
        showTableList.Click += (_, _) =>
        {
            grid.Visibility = grid.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
            showTableList.Content = grid.Visibility == Visibility.Visible
                ? "Hide table list" : "Show table list";
        };
        root.Children.Add(showTableList);
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

        root.Children.Insert(1, BuildVisualFloorBoard(
            rows, activeOrders, availableTables,
            sourceOrderBox, targetTableBox, operationStatus));

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

                await workflow.TransferOrderAsync(order.Id, target.Id);
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

                await workflow.MergeDraftOrdersAsync(target.Id, source.Id);
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

                await workflow.MoveUnsentItemsAsync(source.Id, target.Id, ids);
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

                await workflow.SplitUnsentItemsAsync(source.Id, target.Id, ids);
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
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    public static async Task<FrameworkElement> KitchenAsync()
    {
        var factory = new LocalDatabaseFactory();
        await factory.EnsureCreatedAsync();

        var settingsService = new LocalRestaurantSettingsService(factory);
        var settings = await settingsService.GetAsync();
        var rows = await LoadKitchenBoardRowsAsync(factory);

        var workflow = new DesktopRestaurantWorkflowService();
        var principal = await workflow.CurrentPrincipalAsync();
        var canProduce = principal.UserRole is "owner" or "manager" or "kitchen";
        var canExpo = principal.UserRole is "owner" or "manager" or "expo";
        var canRefire = principal.UserRole is "owner" or "manager" or "kitchen";
        var canRecallWaste = canRefire;

        var root = new StackPanel();
        root.Children.Add(Header(
            "Kitchen / KOT",
            "Item-level KDS. Queue and Preparing are independent; Expo appears only when enabled. Rush and aging stay visible without changing the app shell."));

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
            Foreground = Brushes.SlateGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 10, 0, 0),
        };

        filterRow.Children.Add(stationFilter);
        filterRow.Children.Add(statusFilter);
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

        var board = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        root.Children.Add(board);

        var timerBindings = new List<(TextBlock Label, Border Badge, TextBlock State, DateTimeOffset QueuedAt, string Priority, string Status)>();

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
            var age = DateTimeOffset.UtcNow - queuedAt;
            var minutes = Math.Max(0, (int)Math.Floor(age.TotalMinutes));
            label.Text = $"{minutes:00}:{Math.Max(0, age.Seconds):00}";

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
            Func<Task>? primaryAction = null;
            string? success = null;

            if (canProduce && (row.Status is "queued" or "active"))
            {
                if (row.PreparingEnabled)
                {
                    primary = Button("START");
                    primaryAction = () => workflow.StartKitchenItemAsync(row.ItemId);
                    success = $"{row.ItemName} started.";
                }
                else
                {
                    primary = Button(row.ExpoEnabled ? "SEND TO EXPO" : "READY");
                    primaryAction = () => workflow.MarkKitchenItemReadyAsync(row.ItemId);
                    success = row.ExpoEnabled ? $"{row.ItemName} sent to Expo." : $"{row.ItemName} ready.";
                }
            }
            else if (canProduce && row.Status == "preparing")
            {
                primary = Button(row.ExpoEnabled ? "SEND TO EXPO" : "READY");
                primaryAction = () => workflow.MarkKitchenItemReadyAsync(row.ItemId);
                success = row.ExpoEnabled ? $"{row.ItemName} sent to Expo." : $"{row.ItemName} ready.";
            }
            else if (canExpo && row.Status == "expo")
            {
                primary = Button("PASS EXPO");
                primaryAction = () => workflow.PassExpoItemAsync(row.ItemId);
                success = $"{row.ItemName} passed Expo.";
            }

            if (primary is not null && primaryAction is not null)
            {
                actions.Children.Add(primary);
                primary.Click += async (_, _) =>
                {
                    try
                    {
                        primary.IsEnabled = false;
                        await primaryAction();
                        statusText.Text = success ?? "Kitchen item updated.";
                        DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, statusText.Text);
                    }
                    catch (Exception ex)
                    {
                        primary.IsEnabled = true;
                        statusText.Text = ex.Message;
                        DesktopNoticeEvents.Publish(DesktopNoticeLevel.Error, ex.Message);
                    }
                };
            }

            if (canRecallWaste && (row.Status is "ready" or "completed"))
            {
                var recall = Button("RECALL");
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
                        await workflow.RecallKitchenItemAsync(row.ItemId, actionReason.Text.Trim());
                        statusText.Text = $"{row.ItemName} recalled to the kitchen without reversing its prior consumption.";
                        DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, statusText.Text);
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
                        await workflow.RecordKitchenWasteAsync(row.ItemId, actionReason.Text.Trim());
                        statusText.Text = $"{row.ItemName} recorded as production waste; inventory was not returned.";
                        DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, statusText.Text);
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
                        await workflow.RefireKitchenItemAsync(row.ItemId, actionReason.Text.Trim());
                        statusText.Text = $"{row.ItemName} re-fired as a new rush production event.";
                        DesktopNoticeEvents.Publish(DesktopNoticeLevel.Success, statusText.Text);
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

        RefreshStationChoices();
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
            if (refreshInProgress)
            {
                return;
            }

            refreshInProgress = true;
            try
            {
                var latestRows = await LoadKitchenBoardRowsAsync(factory);
                var latestSettings = await settingsService.GetAsync();
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
            ageTimer.Stop();
            pollTimer.Stop();
        };

        return new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
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
                    await workflow.FinalizeDailyClosingAsync(branch.Id, date);
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

    private static FrameworkElement BuildVisualFloorBoard(
        IReadOnlyList<TableChoice> tables,
        IReadOnlyList<TableOrderChoice> activeOrders,
        IReadOnlyList<Choice> availableTables,
        ComboBox sourceOrderBox,
        ComboBox targetTableBox,
        TextBlock statusText)
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

        foreach (var area in tables.GroupBy(x => x.Area))
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
                surface.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
                surface.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

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
                        sourceOrderBox.SelectedItem = order;
                        statusText.Text = $"Selected {table.Name} / {order.ClientOrderId}. Choose a target table for transfer.";
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
            floor.Children.Add(tiles);
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
    private sealed record MenuChoice(string Id, string Name, decimal Price) { public string Display => $"{Name} — AFN {Price:N2}"; }
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
