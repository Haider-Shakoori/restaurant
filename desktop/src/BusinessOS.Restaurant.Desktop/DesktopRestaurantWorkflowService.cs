using System.Text.Json;
using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.Desktop;

public sealed class DesktopRestaurantWorkflowService
{
    private readonly LocalDatabaseFactory _factory = new();
    private readonly WindowsSessionStore _sessions = new();
    private readonly LocalKitchenService _kitchen;
    private readonly LocalSyncService _sync;
    private readonly LocalCashierService _cashier;
    private readonly LocalOperationsControlService _operations;
    private readonly LocalExpenseService _expenses;
    private readonly LocalRestaurantSettingsService _settings;

    public DesktopRestaurantWorkflowService()
    {
        _kitchen = new LocalKitchenService(_factory);
        _cashier = new LocalCashierService(_factory);
        _operations = new LocalOperationsControlService(_factory);
        _expenses = new LocalExpenseService(_factory);
        _settings = new LocalRestaurantSettingsService(_factory);
        _sync = new LocalSyncService(_factory, new OperationalSnapshotStore(_factory), _kitchen);
    }

    public async Task<LocalTerminalPrincipal> CurrentPrincipalAsync(CancellationToken token = default)
    {
        var session = await _sessions.LoadAsync(token)
            ?? throw new InvalidOperationException("Sign in to the Restaurant desktop before running operational actions.");

        return new LocalTerminalPrincipal(
            $"desktop-{Environment.MachineName}".ToLowerInvariant(),
            session.User.Id,
            session.User.PublicId,
            session.User.Name,
            session.User.Role,
            session.TenantId);
    }

    /// <summary>
    /// Operator-confirmed print recovery. Never creates another KOT round or bill.
    /// A 'printing' state after a crash is ambiguous: staff must inspect the paper
    /// ticket before confirming a retry to avoid duplicate food preparation.
    /// </summary>
    public async Task RequeuePrintJobAsync(
        string jobId, bool isReceipt, CancellationToken token = default)
    {
        var actor = await CurrentPrincipalAsync(token);
        if (actor.UserRole.Trim().ToLowerInvariant() is not ("owner" or "manager"))
            throw new UnauthorizedAccessException("Only an Owner or Manager can retry print jobs.");

        await _factory.EnsureCreatedAsync(token);
        await using var db = _factory.Create();

        string previousStatus;
        string printerName;
        if (isReceipt)
        {
            var job = await db.ReceiptPrintJobs
                .SingleOrDefaultAsync(value => value.Id == jobId, token)
                ?? throw new InvalidOperationException("Receipt print job was not found.");
            previousStatus = job.Status;
            printerName = job.PrinterName;
            if (previousStatus is not ("failed" or "printing"))
                throw new InvalidOperationException("Only failed or interrupted print jobs can be retried.");
            job.Status = "pending";
            job.Attempts = 0;
            job.LastError = null;
            job.PrintedAtUtc = null;
        }
        else
        {
            var job = await db.PrintJobs
                .SingleOrDefaultAsync(value => value.Id == jobId, token)
                ?? throw new InvalidOperationException("KOT print job was not found.");
            previousStatus = job.Status;
            printerName = job.PrinterName;
            if (previousStatus is not ("failed" or "printing"))
                throw new InvalidOperationException("Only failed or interrupted print jobs can be retried.");
            job.Status = "pending";
            job.Attempts = 0;
            job.LastError = null;
            job.PrintedAtUtc = null;
        }

        db.AuditEvents.Add(new LocalAuditEvent
        {
            EventId = Guid.CreateVersion7().ToString("N"),
            Category = "printing",
            EventType = "print.manual_retry",
            ActorUserId = actor.UserId,
            ActorName = actor.UserName,
            ActorRole = actor.UserRole,
            EntityType = isReceipt ? "receipt_print_job" : "kot_print_job",
            EntityId = jobId,
            PayloadJson = JsonSerializer.Serialize(new
            {
                previous_status = previousStatus,
                printer = printerName,
                operator_confirmed_possible_duplicate = true,
            }),
            OccurredAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(token);
    }

    public async Task<string> OpenOrderAsync(string tableId, int guestCount, string? notes = null, CancellationToken token = default)
        => await OpenOrderAsync("dine_in", tableId, string.Empty, null, guestCount, notes, token);

    public async Task<string> OpenOrderAsync(
        string serviceType,
        string? tableId,
        string branchId,
        string? serviceReference,
        int guestCount,
        string? notes = null,
        CancellationToken token = default)
    {
        var clientOrderId = $"DESK-{Guid.CreateVersion7():N}";
        var payload = JsonSerializer.SerializeToElement(new
        {
            client_order_id = clientOrderId,
            dining_table_id = tableId,
            branch_id = branchId,
            service_type = serviceType,
            service_reference = serviceReference,
            guest_count = guestCount,
            notes
        });
        await PushSingleAsync("order.open", payload, token);
        return clientOrderId;
    }

    public async Task AddItemAsync(string clientOrderId, string menuItemId, int quantity, string? notes = null, CancellationToken token = default)
        => await AddItemAsync(
            clientOrderId,
            menuItemId,
            quantity,
            notes,
            seatNumber: null,
            courseNumber: null,
            courseName: null,
            held: false,
            priority: "normal",
            allergyInstructions: null,
            kitchenInstructions: null,
            modifierOptionIds: null,
            token);

    public async Task AddItemAsync(
        string clientOrderId,
        string menuItemId,
        int quantity,
        string? notes,
        int? seatNumber,
        int? courseNumber,
        string? courseName,
        bool held,
        string priority,
        string? allergyInstructions,
        string? kitchenInstructions,
        IReadOnlyList<string>? modifierOptionIds,
        CancellationToken token = default)
        => _ = await AddItemDetailedAsync(
            clientOrderId,
            menuItemId,
            quantity,
            notes,
            seatNumber,
            courseNumber,
            courseName,
            held,
            priority,
            allergyInstructions,
            kitchenInstructions,
            modifierOptionIds,
            token);

    public async Task SubmitOrderAsync(string clientOrderId, CancellationToken token = default)
        => await SendKotAsync(clientOrderId, token);

    public async Task<string> AddItemDetailedAsync(
        string clientOrderId,
        string menuItemId,
        int quantity,
        string? notes,
        int? seatNumber,
        int? courseNumber,
        string? courseName,
        bool held,
        string priority,
        string? allergyInstructions,
        string? kitchenInstructions,
        IReadOnlyList<string>? modifierOptionIds,
        CancellationToken token = default)
    {
        var clientLineId = $"LINE-{Guid.CreateVersion7():N}";
        var modifiers = (modifierOptionIds ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => new { option_id = value })
            .ToArray();

        var payload = JsonSerializer.SerializeToElement(new
        {
            client_order_id = clientOrderId,
            client_line_id = clientLineId,
            menu_item_id = menuItemId,
            quantity,
            notes,
            seat_number = seatNumber,
            course_number = courseNumber,
            course_name = courseName,
            held,
            priority,
            allergy_instructions = allergyInstructions,
            kitchen_instructions = kitchenInstructions,
            modifiers,
        });
        await PushSingleAsync("order.item.add", payload, token);
        return clientLineId;
    }

    public async Task SendKotAsync(string clientOrderId, CancellationToken token = default)
    {
        var payload = JsonSerializer.SerializeToElement(new { client_order_id = clientOrderId });
        await PushSingleAsync("order.kot.send", payload, token);
    }

    public async Task FireCourseAsync(
        string clientOrderId,
        int courseNumber,
        CancellationToken token = default)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            client_order_id = clientOrderId,
            course_number = courseNumber,
        });
        await PushSingleAsync("course.fire", payload, token);
    }

    public async Task VoidOrderItemAsync(
        string clientOrderId,
        string clientLineId,
        string reason,
        CancellationToken token = default)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            client_order_id = clientOrderId,
            client_line_id = clientLineId,
            reason,
        });
        await PushSingleAsync("order.item.void", payload, token);
    }

    public async Task CancelOrderAsync(
        string clientOrderId,
        string reason,
        CancellationToken token = default)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            client_order_id = clientOrderId,
            reason,
        });
        await PushSingleAsync("order.cancel", payload, token);
    }

    public async Task StartKitchenTicketAsync(string ticketId, CancellationToken token = default)
        => _ = await _kitchen.StartAsync(ticketId, await CurrentPrincipalAsync(token), token);

    public async Task MarkKitchenTicketReadyAsync(string ticketId, CancellationToken token = default)
        => _ = await _kitchen.ReadyAsync(ticketId, await CurrentPrincipalAsync(token), token);

    public async Task StartKitchenItemAsync(string itemId, CancellationToken token = default)
        => _ = await _kitchen.StartItemAsync(itemId, await CurrentPrincipalAsync(token), token);

    public async Task MarkKitchenItemReadyAsync(string itemId, CancellationToken token = default)
        => _ = await _kitchen.ReadyItemAsync(itemId, await CurrentPrincipalAsync(token), token);

    public async Task PassExpoItemAsync(string itemId, CancellationToken token = default)
        => _ = await _kitchen.PassExpoItemAsync(itemId, await CurrentPrincipalAsync(token), token);

    public async Task RecallKitchenItemAsync(
        string itemId,
        string reason,
        CancellationToken token = default)
        => _ = await _kitchen.RecallItemAsync(
            itemId,
            reason,
            await CurrentPrincipalAsync(token),
            token);

    public async Task RecordKitchenWasteAsync(
        string itemId,
        string reason,
        CancellationToken token = default)
        => _ = await _kitchen.RecordWasteAsync(
            itemId,
            reason,
            await CurrentPrincipalAsync(token),
            token);

    public async Task RefireKitchenItemAsync(
        string itemId,
        string reason,
        CancellationToken token = default)
        => _ = await _kitchen.RefireItemAsync(
            itemId,
            $"DESK-REFIRE-{Guid.CreateVersion7():N}",
            reason,
            await CurrentPrincipalAsync(token),
            token);

    public async Task<object> TransferOrderAsync(
        string orderId,
        string targetTableId,
        CancellationToken token = default)
        => await _cashier.TransferOrderAsync(orderId, targetTableId, await CurrentPrincipalAsync(token), token);

    public async Task<object> MoveUnsentItemsAsync(
        string sourceOrderId,
        string targetOrderId,
        IReadOnlyList<string> orderItemIds,
        CancellationToken token = default)
        => await _cashier.MoveUnsentItemsAsync(
            sourceOrderId,
            targetOrderId,
            orderItemIds,
            await CurrentPrincipalAsync(token),
            token);

    public async Task<object> SplitUnsentItemsAsync(
        string sourceOrderId,
        string targetTableId,
        IReadOnlyList<string> orderItemIds,
        CancellationToken token = default)
        => await _cashier.SplitUnsentItemsAsync(
            sourceOrderId,
            targetTableId,
            orderItemIds,
            await CurrentPrincipalAsync(token),
            token);

    public async Task<object> MergeDraftOrdersAsync(
        string targetOrderId,
        string sourceOrderId,
        CancellationToken token = default)
        => await _cashier.MergeDraftOrdersAsync(targetOrderId, sourceOrderId, await CurrentPrincipalAsync(token), token);

    public Task<RestaurantWorkflowSettings> RestaurantSettingsAsync(CancellationToken token = default)
        => _settings.GetAsync(token);

    public async Task<RestaurantWorkflowSettings> UpdateRestaurantSettingsAsync(
        RestaurantWorkflowSettingsUpdate update,
        CancellationToken token = default)
        => await _settings.UpdateAsync(update, await CurrentPrincipalAsync(token), token);


    public async Task<object> OpenCashierSessionAsync(string branchId, decimal openingCash, CancellationToken token = default)
        => await _cashier.OpenSessionAsync(branchId, openingCash, await CurrentPrincipalAsync(token), token);

    public async Task<object> CloseCashierSessionAsync(string sessionId, decimal declaredCash, CancellationToken token = default)
        => await _cashier.CloseSessionAsync(sessionId, declaredCash, await CurrentPrincipalAsync(token), token);

    public async Task<object> ServeOrderAsync(string orderId, CancellationToken token = default)
        => await _cashier.ServeOrderAsync(orderId, await CurrentPrincipalAsync(token), token);

    public async Task<object> CreateBillAsync(string orderId, CancellationToken token = default)
        => await _cashier.CreateBillAsync(orderId, await CurrentPrincipalAsync(token), token);

    public async Task<object> ApplyDiscountAsync(string billId, string type, decimal value, string? reason, CancellationToken token = default)
        => await _cashier.ApplyDiscountAsync(billId, type, value, reason, await CurrentPrincipalAsync(token), token);

    public async Task<object> AddPaymentAsync(string billId, string sessionId, decimal amount, string method, string? reference = null, CancellationToken token = default)
        => await _cashier.AddPaymentAsync(
            billId,
            new LocalPaymentRequest(sessionId, amount, method, $"DESK-PAY-{Guid.CreateVersion7():N}", reference),
            await CurrentPrincipalAsync(token),
            token);

    public async Task<object> CreateEqualSplitsAsync(string billId, int splitCount, CancellationToken token = default)
    {
        if (splitCount < 2 || splitCount > 20)
        {
            throw new InvalidOperationException("Split count must be between 2 and 20.");
        }

        await _factory.EnsureCreatedAsync(token);
        await using var db = _factory.Create();
        var bill = await db.Bills.FindAsync([billId], token)
            ?? throw new InvalidOperationException("Bill does not exist.");

        var baseAmount = decimal.Round(bill.Total / splitCount, 2, MidpointRounding.AwayFromZero);
        var parts = new List<LocalBillSplitPart>(splitCount);
        var allocated = 0m;

        for (var index = 1; index <= splitCount; index++)
        {
            var amount = index == splitCount ? bill.Total - allocated : baseAmount;
            amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
            allocated += amount;
            parts.Add(new LocalBillSplitPart($"Split {index}", amount));
        }

        return await _cashier.CreateSplitsAsync(billId, parts, await CurrentPrincipalAsync(token), token);
    }

    public async Task<object> AddSplitPaymentAsync(string billId, string splitId, string sessionId, decimal amount,
        string method, string? reference = null, CancellationToken token = default)
        => await _cashier.AddPaymentAsync(
            billId,
            new LocalPaymentRequest(sessionId, amount, method, $"DESK-PAY-{Guid.CreateVersion7():N}", reference, splitId),
            await CurrentPrincipalAsync(token),
            token);

    public async Task QueueReceiptAsync(string billId, CancellationToken token = default)
        => await _cashier.QueueReceiptAsync(billId, await CurrentPrincipalAsync(token), token);

    public Task<object[]> OpenBillsAsync(CancellationToken token = default)
        => _cashier.OpenBillsAsync(token);

    public async Task<object> FinalizeDailyClosingAsync(string branchId, DateOnly businessDate, CancellationToken token = default)
        => await _operations.FinalizeDailyClosingAsync(branchId, businessDate, await CurrentPrincipalAsync(token), token);

    public async Task<object> RecordExpenseAsync(string branchId, string category, string description, decimal amount,
        string paymentMethod, DateOnly expenseDate, string? reference = null, CancellationToken token = default)
        => await _expenses.RecordAsync(branchId, category, description, amount, paymentMethod, expenseDate, reference,
            await CurrentPrincipalAsync(token), token);

    public async Task<object[]> ExpensesAsync(string? branchId = null, DateOnly? from = null, DateOnly? to = null,
        CancellationToken token = default)
        => await _expenses.ListAsync(branchId, from, to, await CurrentPrincipalAsync(token), token);

    private async Task PushSingleAsync(string operation, JsonElement payload, CancellationToken token)
    {
        var mutation = new LocalSyncMutationRequest(
            Guid.CreateVersion7().ToString("N"),
            operation,
            DateTimeOffset.UtcNow,
            payload);
        var request = new LocalSyncPushRequest(
            Guid.CreateVersion7().ToString("N"),
            [mutation]);

        var result = await _sync.PushAsync(await CurrentPrincipalAsync(token), request, token);
        var json = JsonSerializer.Serialize(result);

        if (json.Contains("\"status\":\"rejected\"", StringComparison.OrdinalIgnoreCase) ||
            json.Contains("\"status\":\"conflict\"", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Restaurant operation was rejected: {json}");
        }
    }
}
