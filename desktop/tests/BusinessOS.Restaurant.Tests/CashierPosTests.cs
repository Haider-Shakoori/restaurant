using System.Text.Json;
using BusinessOS.Restaurant.Application.OperationalData;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class CashierPosTests
{
    [Fact]
    public async Task Served_order_can_be_billed_split_paid_and_closed_with_receipt()
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
            var waiter = Waiter();
            var cashierUser = Cashier();
            var manager = Manager();

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
                ticketIds = await db.KitchenTickets.Select(value => value.Id).ToArrayAsync();
            }

            foreach (var ticketId in ticketIds)
            {
                await kitchen.StartAsync(ticketId, Kitchen(), CancellationToken.None);
                await kitchen.ReadyAsync(ticketId, Kitchen(), CancellationToken.None);
            }

            await cashier.ServeOrderAsync(orderId, cashierUser, CancellationToken.None);

            var sessionElement = JsonSerializer.SerializeToElement(
                await cashier.OpenSessionAsync(
                    "branch-1",
                    1000m,
                    cashierUser,
                    CancellationToken.None));
            var sessionId = sessionElement.GetProperty("id").GetString()!;

            await cashier.ConfigureReceiptPrinterAsync(
                "Receipt Thermal",
                1,
                true,
                manager,
                CancellationToken.None);

            var billElement = JsonSerializer.SerializeToElement(
                await cashier.CreateBillAsync(orderId, cashierUser, CancellationToken.None));
            var billId = billElement.GetProperty("id").GetString()!;

            var discounted = JsonSerializer.SerializeToElement(
                await cashier.ApplyDiscountAsync(
                    billId,
                    "percent",
                    10m,
                    "VIP",
                    cashierUser,
                    CancellationToken.None));

            Assert.Equal("450.00", discounted.GetProperty("total").GetString());

            var splitBill = JsonSerializer.SerializeToElement(
                await cashier.CreateSplitsAsync(
                    billId,
                    [
                        new LocalBillSplitPart("Guest 1", 225m),
                        new LocalBillSplitPart("Guest 2", 225m),
                    ],
                    cashierUser,
                    CancellationToken.None));

            var splits = splitBill.GetProperty("splits").EnumerateArray().ToArray();
            Assert.Equal(2, splits.Length);

            await cashier.AddPaymentAsync(
                billId,
                new LocalPaymentRequest(
                    sessionId,
                    225m,
                    "cash",
                    "PAY-1",
                    BillSplitId: splits[0].GetProperty("id").GetString()),
                cashierUser,
                CancellationToken.None);

            // A partial cash payment must not release the table. Network/LAN
            // retries with the same idempotency key must never count twice.
            await cashier.AddPaymentAsync(
                billId,
                new LocalPaymentRequest(
                    sessionId, 225m, "cash", "PAY-1",
                    BillSplitId: splits[0].GetProperty("id").GetString()),
                cashierUser, CancellationToken.None);
            await using (var checkpoint = factory.Create())
            {
                var partial = await checkpoint.Bills.SingleAsync();
                Assert.Equal("open", partial.Status);
                Assert.Equal(225m, partial.PaidAmount);
                Assert.Equal(225m, partial.BalanceDue);
                Assert.Equal("occupied", (await checkpoint.DiningTables
                    .SingleAsync(x => x.Id == "table-1")).Status);
                Assert.Single(await checkpoint.Payments.ToListAsync());
            }

            await Assert.ThrowsAsync<LocalSyncConflictException>(() =>
                cashier.AddPaymentAsync(billId,
                    new LocalPaymentRequest(sessionId, 226m, "cash", "OVERPAY-1"),
                    cashierUser, CancellationToken.None));

            await cashier.AddPaymentAsync(
                billId,
                new LocalPaymentRequest(
                    sessionId,
                    225m,
                    "card",
                    "PAY-2",
                    Reference: "CARD-REF",
                    BillSplitId: splits[1].GetProperty("id").GetString()),
                cashierUser,
                CancellationToken.None);

            await using (var db = factory.Create())
            {
                var bill = await db.Bills.SingleAsync();
                var order = await db.Orders.SingleAsync();
                var table = await db.DiningTables.SingleAsync(value => value.Id == "table-1");
                var receipt = await db.ReceiptPrintJobs.SingleAsync();

                Assert.Equal(2, await db.Payments.CountAsync());
                Assert.Equal(450m, await db.Payments.SumAsync(x => x.Amount));
                Assert.Equal("paid", bill.Status);
                Assert.Equal(450m, bill.PaidAmount);
                Assert.Equal(0m, bill.BalanceDue);
                Assert.Equal("closed", order.Status);
                Assert.Equal("available", table.Status);
                Assert.Equal("pending", receipt.Status);
                Assert.Equal("Receipt Thermal", receipt.PrinterName);
                Assert.Contains("TOTAL: 450.00 AFN", receipt.PayloadText, StringComparison.Ordinal);
                Assert.Contains("CASH: 225.00 AFN", receipt.PayloadText, StringComparison.Ordinal);
                Assert.Contains("CARD: 225.00 AFN", receipt.PayloadText, StringComparison.Ordinal);
            }

            var closedSession = JsonSerializer.SerializeToElement(
                await cashier.CloseSessionAsync(
                    sessionId,
                    1220m,
                    cashierUser,
                    CancellationToken.None));

            Assert.Equal("1225.00", closedSession.GetProperty("expected_cash").GetString());
            Assert.Equal("-5.00", closedSession.GetProperty("cash_variance").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Draft_orders_can_merge_and_active_order_can_transfer_to_empty_table()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalog = new OperationalSnapshotStore(factory);
            await catalog.ApplyAsync(Snapshot());
            var sync = new LocalSyncService(factory, catalog);
            var cashier = new LocalCashierService(factory);
            var waiter = Waiter();

            await PushOneAsync(sync, waiter, "OPEN-1", "order.open", new
            {
                client_order_id = "ORDER-A",
                dining_table_id = "table-1",
            });
            await PushOneAsync(sync, waiter, "ADD-1", "order.item.add", new
            {
                client_order_id = "ORDER-A",
                client_line_id = "LINE-A",
                menu_item_id = "item-1",
                quantity = 1,
            });

            await PushOneAsync(sync, waiter, "OPEN-2", "order.open", new
            {
                client_order_id = "ORDER-B",
                dining_table_id = "table-2",
            });
            await PushOneAsync(sync, waiter, "ADD-2", "order.item.add", new
            {
                client_order_id = "ORDER-B",
                client_line_id = "LINE-B",
                menu_item_id = "item-1",
                quantity = 2,
            });

            string targetId;
            string sourceId;
            await using (var db = factory.Create())
            {
                targetId = (await db.Orders.SingleAsync(value => value.ClientOrderId == "ORDER-A")).Id;
                sourceId = (await db.Orders.SingleAsync(value => value.ClientOrderId == "ORDER-B")).Id;
            }

            await cashier.MergeDraftOrdersAsync(
                targetId,
                sourceId,
                Cashier(),
                CancellationToken.None);

            await using (var db = factory.Create())
            {
                var target = await db.Orders.SingleAsync(value => value.Id == targetId);
                var source = await db.Orders.SingleAsync(value => value.Id == sourceId);

                Assert.Equal(750m, target.Total);
                Assert.Equal(2, await db.OrderItems.CountAsync(value => value.OrderId == targetId));
                Assert.Equal("closed", source.Status);
                Assert.Equal("available", (await db.DiningTables.SingleAsync(value => value.Id == "table-2")).Status);
            }

            await cashier.TransferOrderAsync(
                targetId,
                "table-3",
                Cashier(),
                CancellationToken.None);

            await using (var db = factory.Create())
            {
                var target = await db.Orders.SingleAsync(value => value.Id == targetId);
                Assert.Equal("table-3", target.DiningTableId);
                Assert.Equal("available", (await db.DiningTables.SingleAsync(value => value.Id == "table-1")).Status);
                Assert.Equal("occupied", (await db.DiningTables.SingleAsync(value => value.Id == "table-3")).Status);
            }
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
            "batch-pos",
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
                Table("table-1", "T-01", "Table 1"),
                Table("table-2", "T-02", "Table 2"),
                Table("table-3", "T-03", "Table 3"),
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

    private static DiningTableSnapshot Table(string id, string code, string name) =>
        new(
            id,
            code,
            name,
            4,
            "available",
            true,
            new DiningAreaSummary("area-1", "Main Hall"),
            new BranchSummary("branch-1", "Main Branch"));

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
