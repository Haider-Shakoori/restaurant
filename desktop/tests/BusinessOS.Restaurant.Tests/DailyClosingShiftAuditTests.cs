using System.Text.Json;
using BusinessOS.Restaurant.Application.OperationalData;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class DailyClosingShiftAuditTests
{
    [Fact]
    public async Task Closing_is_blocked_by_open_operations_then_versions_and_audits_after_reopen()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalog = new OperationalSnapshotStore(factory);
            await catalog.ApplyAsync(Snapshot());

            var kitchen = new LocalKitchenService(factory);
            var sync = new LocalSyncService(factory, catalog, kitchen);
            var cashier = new LocalCashierService(factory);
            var operations = new LocalOperationsControlService(factory);

            var waiter = Waiter();
            var cashierUser = Cashier();
            var manager = Manager();
            var businessDate = DateOnly.FromDateTime(DateTime.UtcNow);

            var shiftJson = JsonSerializer.SerializeToElement(
                await operations.StartShiftAsync(
                    "branch-1",
                    waiter,
                    CancellationToken.None));
            var shiftId = shiftJson.GetProperty("id").GetString()!;

            var sessionJson = JsonSerializer.SerializeToElement(
                await cashier.OpenSessionAsync(
                    "branch-1",
                    100m,
                    cashierUser,
                    CancellationToken.None));
            var sessionId = sessionJson.GetProperty("id").GetString()!;

            var blocked = await Assert.ThrowsAsync<LocalSyncConflictException>(() =>
                operations.FinalizeDailyClosingAsync(
                    "branch-1",
                    businessDate,
                    cashierUser,
                    CancellationToken.None));

            Assert.Equal("closing_blocked", blocked.Code);

            await PushOneAsync(sync, waiter, "OPEN", "order.open", new
            {
                client_order_id = "ORDER-1",
                dining_table_id = "table-1",
                guest_count = 2,
            });

            await PushOneAsync(sync, waiter, "ADD", "order.item.add", new
            {
                client_order_id = "ORDER-1",
                client_line_id = "LINE-1",
                menu_item_id = "item-1",
                quantity = 2,
            });

            await PushOneAsync(sync, waiter, "SUBMIT", "order.submit", new
            {
                client_order_id = "ORDER-1",
            });

            string orderId;
            string[] ticketIds;

            await using (var db = factory.Create())
            {
                orderId = (await db.Orders.SingleAsync()).Id;
                ticketIds = await db.KitchenTickets
                    .Select(value => value.Id)
                    .ToArrayAsync();
            }

            foreach (var ticketId in ticketIds)
            {
                await kitchen.ReadyAsync(ticketId, Kitchen(), CancellationToken.None);
            }

            await cashier.ServeOrderAsync(orderId, cashierUser, CancellationToken.None);

            var billJson = JsonSerializer.SerializeToElement(
                await cashier.CreateBillAsync(
                    orderId,
                    cashierUser,
                    CancellationToken.None));
            var billId = billJson.GetProperty("id").GetString()!;

            await cashier.AddPaymentAsync(
                billId,
                new LocalPaymentRequest(
                    sessionId,
                    500m,
                    "cash",
                    "CLOSE-PAY-1"),
                cashierUser,
                CancellationToken.None);

            await cashier.CloseSessionAsync(
                sessionId,
                600m,
                cashierUser,
                CancellationToken.None);

            await operations.EndShiftAsync(
                shiftId,
                15,
                "Normal close",
                waiter,
                CancellationToken.None);

            var finalized = JsonSerializer.SerializeToElement(
                await operations.FinalizeDailyClosingAsync(
                    "branch-1",
                    businessDate,
                    cashierUser,
                    CancellationToken.None));

            Assert.Equal("finalized", finalized.GetProperty("status").GetString());

            var snapshots = finalized.GetProperty("snapshots").EnumerateArray().ToArray();
            Assert.Single(snapshots);
            Assert.Equal(1, snapshots[0].GetProperty("version").GetInt32());
            Assert.Equal("500.00", snapshots[0].GetProperty("gross_sales").GetString());
            Assert.Equal("500.00", snapshots[0].GetProperty("net_sales").GetString());
            Assert.Equal("500.00", snapshots[0].GetProperty("payments_total").GetString());
            Assert.Equal("500.00", snapshots[0].GetProperty("cash_payments").GetString());
            Assert.Equal("600.00", snapshots[0].GetProperty("expected_cash").GetString());
            Assert.Equal("600.00", snapshots[0].GetProperty("declared_cash").GetString());
            Assert.Equal("0.00", snapshots[0].GetProperty("cash_variance").GetString());

            var closingId = finalized.GetProperty("id").GetString()!;

            var reopened = JsonSerializer.SerializeToElement(
                await operations.ReopenDailyClosingAsync(
                    closingId,
                    "Manager correction",
                    manager,
                    CancellationToken.None));

            Assert.Equal("reopened", reopened.GetProperty("status").GetString());

            var refinalized = JsonSerializer.SerializeToElement(
                await operations.FinalizeDailyClosingAsync(
                    "branch-1",
                    businessDate,
                    manager,
                    CancellationToken.None));

            var versions = refinalized.GetProperty("snapshots").EnumerateArray().ToArray();
            Assert.Equal(2, versions.Length);
            Assert.Equal(1, versions[0].GetProperty("version").GetInt32());
            Assert.Equal(2, versions[1].GetProperty("version").GetInt32());

            var audit = await operations.AuditAsync(
                0,
                250,
                null,
                manager,
                CancellationToken.None);

            var serializedAudit = JsonSerializer.SerializeToElement(audit);
            var eventTypes = serializedAudit
                .EnumerateArray()
                .Select(value => value.GetProperty("event_type").GetString())
                .ToArray();

            Assert.Contains("shift.opened", eventTypes);
            Assert.Contains("shift.closed", eventTypes);
            Assert.Contains("cashier.session_opened", eventTypes);
            Assert.Contains("cashier.session_closed", eventTypes);
            Assert.Contains("bill.issued", eventTypes);
            Assert.Contains("payment.posted", eventTypes);
            Assert.Contains("daily_closing.finalized", eventTypes);
            Assert.Contains("daily_closing.reopened", eventTypes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Reopen_requires_management_and_reason()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalog = new OperationalSnapshotStore(factory);
            await catalog.ApplyAsync(Snapshot());
            var operations = new LocalOperationsControlService(factory);
            var date = DateOnly.FromDateTime(DateTime.UtcNow);

            var finalized = JsonSerializer.SerializeToElement(
                await operations.FinalizeDailyClosingAsync(
                    "branch-1",
                    date,
                    Cashier(),
                    CancellationToken.None));
            var closingId = finalized.GetProperty("id").GetString()!;

            var forbidden = await Assert.ThrowsAsync<LocalSyncConflictException>(() =>
                operations.ReopenDailyClosingAsync(
                    closingId,
                    "Correction",
                    Cashier(),
                    CancellationToken.None));

            Assert.Equal("forbidden", forbidden.Code);

            var invalid = await Assert.ThrowsAsync<LocalSyncConflictException>(() =>
                operations.ReopenDailyClosingAsync(
                    closingId,
                    " ",
                    Manager(),
                    CancellationToken.None));

            Assert.Equal("invalid_payload", invalid.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<JsonElement> PushOneAsync(
        LocalSyncService sync,
        LocalTerminalPrincipal principal,
        string mutationId,
        string operation,
        object payload)
    {
        var request = new LocalSyncPushRequest(
            "batch-09",
            [
                new LocalSyncMutationRequest(
                    mutationId,
                    operation,
                    DateTimeOffset.UtcNow,
                    JsonSerializer.SerializeToElement(payload)),
            ]);

        var response = JsonSerializer.SerializeToElement(
            await sync.PushAsync(principal, request, CancellationToken.None));

        return response.GetProperty("results")[0];
    }

    private static LocalTerminalPrincipal Waiter() =>
        new("waiter-device", 1, "waiter-1", "Waiter One", "waiter", "tenant-1");

    private static LocalTerminalPrincipal Kitchen() =>
        new("kitchen-device", 2, "kitchen-1", "Kitchen One", "kitchen", "tenant-1");

    private static LocalTerminalPrincipal Cashier() =>
        new("cashier-device", 3, "cashier-1", "Cashier One", "cashier", "tenant-1");

    private static LocalTerminalPrincipal Manager() =>
        new("manager-device", 4, "manager-1", "Manager One", "manager", "tenant-1");

    private static OperationalSnapshot Snapshot() =>
        new(
            1,
            DateTimeOffset.UtcNow,
            0,
            "tenant-1",
            [
                new BranchSnapshot("branch-1", "MAIN", "Main Branch", true),
            ],
            [
                new StaffSnapshot(1, "waiter-1", "Waiter One", "waiter@example.test", "waiter", true),
                new StaffSnapshot(2, "kitchen-1", "Kitchen One", "kitchen@example.test", "kitchen", true),
                new StaffSnapshot(3, "cashier-1", "Cashier One", "cashier@example.test", "cashier", true),
                new StaffSnapshot(4, "manager-1", "Manager One", "manager@example.test", "manager", true),
            ],
            [
                new MenuCategorySnapshot(
                    "category-1",
                    "Main",
                    1,
                    [
                        new MenuItemSnapshot(
                            "item-1",
                            "category-1",
                            "FOOD-1",
                            "Kabuli Pulao",
                            null,
                            250m,
                            "AFN",
                            1,
                            []),
                    ]),
            ],
            [
                new DiningTableSnapshot(
                    "table-1",
                    "T-01",
                    "Table 1",
                    4,
                    "available",
                    true,
                    new DiningAreaSummary("area-1", "Main Hall"),
                    new BranchSummary("branch-1", "Main Branch")),
            ],
            new KitchenSnapshot(
                [
                    new KitchenStationSnapshot(
                        "station-1",
                        "branch-1",
                        "GENERAL",
                        "General Kitchen",
                        1,
                        true),
                ],
                [
                    new MenuItemKitchenRouteSnapshot(
                        "route-1",
                        "item-1",
                        "branch-1",
                        "station-1"),
                ]));

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "BusinessOS.Restaurant.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
