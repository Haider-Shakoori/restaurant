using Xunit;

namespace BusinessOS.Restaurant.Tests;

/// <summary>
/// Batch UI guard: tapping a table tile opens the order modal from the floor,
/// where staff add dishes, add guests up to the table capacity, remove unsent
/// lines and send the KOT — reusing the existing idempotent workflow calls.
/// </summary>
public sealed class TableOrderModalTests
{
    [Fact]
    public void Floor_tiles_open_the_table_order_modal()
    {
        var source = Source("src", "BusinessOS.Restaurant.Desktop", "OperationalActionViews.cs");
        Assert.Contains("OpenTableOrderModalAsync", source);
        Assert.Contains("await OpenTableOrderModalAsync(table, \"dine_in\", order, statusText);", source);
        Assert.Contains("Tap to open this table's order", source);
        Assert.Contains("Name = \"TableOrderDialog\"", source);
    }

    [Fact]
    public void Table_order_modal_adds_dishes_from_the_floor_and_sends_kitchen_once()
    {
        var source = Source("src", "BusinessOS.Restaurant.Desktop", "OperationalActionViews.cs");
        Assert.Contains("TableMenuChoice", source);
        Assert.Contains("TableLineChoice", source);
        Assert.Contains("SEND TO KITCHEN", source);
        Assert.Contains("Name = \"TableOrderSendButton\"", source);
        Assert.Contains("workflow.AddItemAsync(clientOrderId!, captured.Id, 1, null, token)", source);
        Assert.Contains("workflow.SendKotAsync(clientOrderId, token)", source);
        Assert.Contains("workflow.OpenOrderAsync(table.Id, guests, null, token)", source);
        Assert.Contains("NOT SENT", source);

        // Takeaway and delivery open the same modal without a table and pass a
        // branch plus optional customer reference instead.
        Assert.Contains("Name = \"PosTakeawayButton\"", source);
        Assert.Contains("Name = \"PosDeliveryButton\"", source);
        Assert.Contains("OpenServiceOrderAsync(\"takeaway\")", source);
        Assert.Contains("OpenServiceOrderAsync(\"delivery\")", source);
        Assert.Contains("serviceType, null, serviceBranchId", source);
        Assert.Contains("Customer / reference", source);
    }

    [Fact]
    public void Table_order_modal_manages_guests_and_audited_removals()
    {
        var source = Source("src", "BusinessOS.Restaurant.Desktop", "OperationalActionViews.cs");
        Assert.Contains("Remove one guest", source);
        Assert.Contains("Add one guest to this table", source);
        Assert.Contains("workflow.UpdateGuestsAsync(clientOrderId, next, token)", source);
        Assert.Contains("workflow.VoidOrderItemAsync(", source);
        Assert.Contains("Removed while ordering at the table", source);
        Assert.Contains("guests < capacity", source);
        Assert.Contains("guests > 1", source);
        Assert.Contains("unpaid bill", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pos_is_table_first_and_moves_extra_buttons_out_of_the_toolbar()
    {
        var source = Source("src", "BusinessOS.Restaurant.Desktop", "OperationalActionViews.cs");
        Assert.Contains("Name = \"PosTableStrip\"", source);
        Assert.Contains("BuildPosTableTile", source);
        Assert.Contains("RefreshFloorAsync", source);
        Assert.Contains("allowCheckout: true", source);
        Assert.Contains("Checkout & payments", source);
        Assert.Contains("Name = \"TableOrderCheckoutButton\"", source);
        Assert.Contains("billingModal.RaiseEvent(", source);
        // The visible Pay button lives in the toolbar; other legacy controls
        // stay hidden but keep keyboard/automation access.
        Assert.Contains("billingModal.Visibility = Visibility.Visible;", source);
        Assert.Contains("button.Visibility = Visibility.Collapsed;", source);

        // Staff see short daily order numbers instead of long identifiers.
        Assert.Contains("order.ShortNumber", source);
        Assert.Contains("nameof(OrderChoice.Display)", source);
        Assert.Contains("DisplayNumber", source);
        Assert.Contains("quickAddItemId", source);

        // Legacy controls stay in the tree for keyboard shortcuts/automation
        // but are removed from the toolbar surface.
        Assert.Contains("button.Visibility = Visibility.Collapsed;", source);
        Assert.Contains("\"PosOpenNewOrderModal\"", source);
        Assert.Contains("\"PosOpenPaymentModal\"", source);
    }

    [Fact]
    public void Table_order_modal_orders_by_time_in_memory_because_sqlite_cannot()
    {
        var source = Source("src", "BusinessOS.Restaurant.Desktop", "OperationalActionViews.cs");
        Assert.Contains(".OrderByDescending(x => x.UpdatedAtUtc)", source);
        Assert.Contains(".OrderBy(x => x.CreatedAtUtc)", source);

        var guard = Source("tests", "BusinessOS.Restaurant.Tests", "DashboardWindowQueryTests.cs");
        Assert.Contains("DateTimeOffset_ordering_must_run_after_materialization", guard);
    }

    private static string Source(params string[] relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;

        return File.ReadAllText(Path.Combine(
            dir?.FullName ?? throw new DirectoryNotFoundException(),
            "desktop", Path.Combine(relativePath)));
    }
}
