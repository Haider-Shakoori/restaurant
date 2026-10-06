using System.Text.Json;
using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;

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

    public DesktopRestaurantWorkflowService()
    {
        _kitchen = new LocalKitchenService(_factory);
        _cashier = new LocalCashierService(_factory);
        _operations = new LocalOperationsControlService(_factory);
        _expenses = new LocalExpenseService(_factory);
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

    public async Task<string> OpenOrderAsync(string tableId, int guestCount, string? notes = null, CancellationToken token = default)
    {
        var clientOrderId = $"DESK-{Guid.CreateVersion7():N}";
        var payload = JsonSerializer.SerializeToElement(new
        {
            client_order_id = clientOrderId,
            dining_table_id = tableId,
            guest_count = guestCount,
            notes
        });
        await PushSingleAsync("order.open", payload, token);
        return clientOrderId;
    }

    public async Task AddItemAsync(string clientOrderId, string menuItemId, int quantity, string? notes = null, CancellationToken token = default)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            client_order_id = clientOrderId,
            client_line_id = $"LINE-{Guid.CreateVersion7():N}",
            menu_item_id = menuItemId,
            quantity,
            notes
        });
        await PushSingleAsync("order.item.add", payload, token);
    }

    public async Task SubmitOrderAsync(string clientOrderId, CancellationToken token = default)
    {
        var payload = JsonSerializer.SerializeToElement(new { client_order_id = clientOrderId });
        await PushSingleAsync("order.submit", payload, token);
    }

    public async Task StartKitchenTicketAsync(string ticketId, CancellationToken token = default)
        => _ = await _kitchen.StartAsync(ticketId, await CurrentPrincipalAsync(token), token);

    public async Task MarkKitchenTicketReadyAsync(string ticketId, CancellationToken token = default)
        => _ = await _kitchen.ReadyAsync(ticketId, await CurrentPrincipalAsync(token), token);


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
