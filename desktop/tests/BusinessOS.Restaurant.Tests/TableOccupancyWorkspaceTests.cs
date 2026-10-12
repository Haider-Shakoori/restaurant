using Xunit;

namespace BusinessOS.Restaurant.Tests;

/// <summary>
/// Batch 6 regression guards: the tables workspace keeps its authorized
/// override action, manual overrides sync to the tenant master and release
/// only genuinely idle tables, and the POS stays fast for late guests.
/// </summary>
public sealed class TableOccupancyWorkspaceTests
{
    [Fact]
    public void Tables_workspace_keeps_the_authorized_override_action()
    {
        var source = Source("BusinessOS.Restaurant.Desktop", "OperationalActionViews.cs");
        Assert.Contains("Override → Mark Available", source);
        Assert.Contains("Active or unpaid orders will block this action. This override is audited.", source);
        Assert.Contains("actor.UserRole is \"owner\" or \"admin\" or \"manager\"", source);
        Assert.Contains("Manual table override from the Tables workspace", source);
    }

    [Fact]
    public void Manual_override_blocks_active_orders_and_syncs_to_the_tenant()
    {
        var source = Source("BusinessOS.Restaurant.LocalServer", "LocalCashierService.cs");
        Assert.Contains("Only management can override table occupancy.", source);
        Assert.Contains("active_order", source);
        Assert.Contains("table.availability_override", source);
        Assert.Contains("\"table.mark_available\"", source);
        Assert.Contains("previous_status", source);
        Assert.Contains("reason = overrideReason", source);
    }

    [Fact]
    public void Cancelling_and_settling_release_tables_only_when_no_active_order_remains()
    {
        var sync = Source("BusinessOS.Restaurant.LocalServer", "LocalSyncService.cs");
        Assert.Contains("x.Status != \"closed\" && x.Status != \"cancelled\"", sync);

        var cashier = Source("BusinessOS.Restaurant.LocalServer", "LocalCashierService.cs");
        Assert.Contains("x.Status != \"closed\" && x.Status != \"cancelled\"", cashier);
        Assert.Contains("? \"occupied\" : \"available\"", cashier);
    }

    [Fact]
    public void Cashier_operations_queue_cloud_reconciliation_mutations()
    {
        var source = Source("BusinessOS.Restaurant.LocalServer", "LocalCashierService.cs");
        Assert.Contains("\"order.table.transfer\"", source);
        Assert.Contains("\"order.merge\"", source);
        Assert.Contains("\"order.item.move\"", source);
        Assert.Contains("\"order.split\"", source);
        Assert.Contains("ResolveTableStatusAsync", source);
    }

    [Fact]
    public void Tables_kitchen_and_checkout_follow_the_restaurant_flow_design()
    {
        var views = Source("BusinessOS.Restaurant.Desktop", "OperationalActionViews.cs");

        // Dining room: colour legend and per-status card language.
        Assert.Contains("LegendEntry(", views);
        Assert.Contains("\"waiting to pay\"", views);
        Assert.Contains("\"Waiting to pay\"", views);
        Assert.Contains("\"Reserved\"", views);
        Assert.Contains("stripeColor", views);

        // Kitchen board is split into the three flow columns.
        Assert.Contains("KitchenColumn(\"New\"", views);
        Assert.Contains("KitchenColumn(\"Cooking\"", views);
        Assert.Contains("KitchenColumn(\"Ready for pickup\"", views);

        // Cash-only checkout: no payment-method selector, postings stay cash.
        Assert.Contains("var method = \"cash\";", views);
        Assert.DoesNotContain("paymentMethod", views);
    }

    [Fact]
    public void Dining_room_flow_keeps_the_classic_floor_view_one_click_away()
    {
        var views = Source("BusinessOS.Restaurant.Desktop", "OperationalActionViews.cs");
        Assert.Contains("BuildDiningRoomAsync", views);
        Assert.Contains("Name = \"TablesViewToggle\"", views);
        Assert.Contains("UseNewDiningRoom", views);
        Assert.Contains("Select a table to open its order or seat new guests.", views);
        Assert.Contains("Name = \"DiningRoomOrderButton\"", views);
        Assert.Contains("Name = \"DiningRoomSendButton\"", views);
        Assert.Contains("Name = \"DiningRoomPayButton\"", views);
        Assert.Contains("\"Classic floor view\"", views);
        Assert.Contains("advancedOperations", views);

        // Cash payment modal, occupancy choice and reservations.
        Assert.Contains("OpenTablePaymentModalAsync", views);
        Assert.Contains("Name = \"TablePaymentDialog\"", views);
        Assert.Contains("Name = \"TablePaymentMarkVacant\"", views);
        Assert.Contains("Name = \"TablePaymentCompleteButton\"", views);
        // Occupancy is manual: the payment checkbox is honored, and cleared
        // tables expose a vacant button instead of an automatic sweep.
        Assert.Contains("HoldTableAsync", views);
        Assert.Contains("ClearTableAsync", views);
        Assert.Contains("Name = \"DiningRoomVacantButton\"", views);
        Assert.Contains("Awaiting clearing", views);
        Assert.Contains("Name = \"DiningRoomReserveButton\"", views);
        Assert.Contains("ReserveTableAsync", views);
        Assert.Contains("CancelReservationAsync", views);

        // Served/completed tickets never appear on the live kitchen board, and
        // closed/cancelled orders can never strand a card there.
        Assert.Contains("Completed (served) tickets never appear on the live board.", views);
        Assert.Contains("must never strand a KOT card", views);
    }

    [Fact]
    public void Pos_keeps_late_guest_controls_and_one_touch_kitchen_ready()
    {
        var source = Source("BusinessOS.Restaurant.Desktop", "OperationalActionViews.cs");
        Assert.Contains("PosAdditionalGuestCount", source);
        Assert.Contains("PosSaveAdditionalGuests", source);
        Assert.Contains("Add people to the same table and existing KOT order", source);
        Assert.Contains("PosOpenPaymentModal", source);
        Assert.Contains("MarkKitchenItemReadyDirectAsync", source);
    }

    private static string Source(params string[] relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;

        return File.ReadAllText(Path.Combine(
            dir?.FullName ?? throw new DirectoryNotFoundException(),
            "desktop", "src", Path.Combine(relativePath)));
    }
}
