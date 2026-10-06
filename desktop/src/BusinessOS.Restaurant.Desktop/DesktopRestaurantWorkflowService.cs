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

    public DesktopRestaurantWorkflowService()
    {
        _kitchen = new LocalKitchenService(_factory);
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
