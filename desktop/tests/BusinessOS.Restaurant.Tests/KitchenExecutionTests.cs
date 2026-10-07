using System.Text.Json;
using BusinessOS.Restaurant.Application.OperationalData;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class KitchenExecutionTests
{
    [Fact]
    public async Task Submitted_order_routes_items_to_station_tickets_and_print_queue()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalogStore = new OperationalSnapshotStore(factory);
            await catalogStore.ApplyAsync(Snapshot());

            var kitchen = new LocalKitchenService(factory);
            await kitchen.ConfigurePrinterAsync(
                "station-grill",
                "Kitchen Thermal",
                copies: 2,
                enabled: true,
                CancellationToken.None);

            var sync = new LocalSyncService(factory, catalogStore, kitchen);
            var waiter = Waiter();

            await PushOneAsync(sync, waiter, "OPEN", "order.open", new
            {
                client_order_id = "ORDER-1",
                dining_table_id = "table-1",
                guest_count = 2,
            });

            await PushOneAsync(sync, waiter, "ADD-1", "order.item.add", new
            {
                client_order_id = "ORDER-1",
                client_line_id = "LINE-1",
                menu_item_id = "item-grill",
                quantity = 2,
            });

            await PushOneAsync(sync, waiter, "ADD-2", "order.item.add", new
            {
                client_order_id = "ORDER-1",
                client_line_id = "LINE-2",
                menu_item_id = "item-general",
                quantity = 1,
                notes = "No onion",
            });

            var submit = await PushOneAsync(sync, waiter, "SUBMIT", "order.submit", new
            {
                client_order_id = "ORDER-1",
            });

            Assert.Equal("accepted", submit.GetProperty("status").GetString());

            await using var db = factory.Create();
            var tickets = await db.KitchenTickets.AsNoTracking().ToArrayAsync();
            var ticketItems = await db.KitchenTicketItems.AsNoTracking().ToArrayAsync();
            var printJobs = await db.PrintJobs.AsNoTracking().ToArrayAsync();

            Assert.Equal(2, tickets.Length);
            Assert.Equal(2, ticketItems.Length);
            Assert.All(tickets, value => Assert.Equal("queued", value.Status));
            Assert.Contains(tickets, value => value.KitchenStationId == "station-grill");
            Assert.Contains(
                await db.KitchenStations.AsNoTracking().ToArrayAsync(),
                value => value.Code == "GENERAL" && value.BranchId == "branch-1");

            Assert.Single(printJobs);
            Assert.Equal("Kitchen Thermal", printJobs[0].PrinterName);
            Assert.Equal(2, printJobs[0].Copies);
            Assert.Equal("pending", printJobs[0].Status);
            Assert.Contains("TABLE: Table 1", printJobs[0].PayloadText, StringComparison.Ordinal);
            Assert.Contains("2 x Grilled Chicken", printJobs[0].PayloadText, StringComparison.Ordinal);

            var order = await db.Orders.AsNoTracking().SingleAsync();
            Assert.Equal("submitted", order.Status);
            Assert.All(
                await db.OrderItems.AsNoTracking().ToArrayAsync(),
                value => Assert.Equal("queued", value.Status));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Kitchen_transitions_propagate_preparing_and_ready_to_order_pull_stream()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalogStore = new OperationalSnapshotStore(factory);
            await catalogStore.ApplyAsync(Snapshot());
            var kitchen = new LocalKitchenService(factory);
            var sync = new LocalSyncService(factory, catalogStore, kitchen);
            var waiter = Waiter();

            await PushOneAsync(sync, waiter, "OPEN", "order.open", new
            {
                client_order_id = "ORDER-1",
                dining_table_id = "table-1",
            });
            await PushOneAsync(sync, waiter, "ADD-1", "order.item.add", new
            {
                client_order_id = "ORDER-1",
                client_line_id = "LINE-1",
                menu_item_id = "item-grill",
                quantity = 1,
            });
            await PushOneAsync(sync, waiter, "ADD-2", "order.item.add", new
            {
                client_order_id = "ORDER-1",
                client_line_id = "LINE-2",
                menu_item_id = "item-general",
                quantity = 1,
            });
            await PushOneAsync(sync, waiter, "SUBMIT", "order.submit", new
            {
                client_order_id = "ORDER-1",
            });

            string[] ticketIds;
            await using (var db = factory.Create())
            {
                ticketIds = await db.KitchenTickets
                    .Select(value => value.Id)
                    .ToArrayAsync();
            }

            var kitchenUser = new LocalTerminalPrincipal(
                "kitchen-device",
                99,
                "kitchen-99",
                "Kitchen User",
                "kitchen",
                "tenant-1");

            await kitchen.StartAsync(ticketIds[0], kitchenUser, CancellationToken.None);

            await using (var db = factory.Create())
            {
                Assert.Equal("preparing", (await db.Orders.SingleAsync()).Status);
            }

            foreach (var ticketId in ticketIds)
            {
                await kitchen.StartAsync(ticketId, kitchenUser, CancellationToken.None);
                await kitchen.ReadyAsync(ticketId, kitchenUser, CancellationToken.None);
            }

            await using (var db = factory.Create())
            {
                Assert.Equal("ready", (await db.Orders.SingleAsync()).Status);
            }

            var pull = JsonSerializer.SerializeToElement(
                await sync.PullAsync(waiter, 0, 100, CancellationToken.None));

            var orderChanges = pull.GetProperty("changes")
                .EnumerateArray()
                .Where(value => value.GetProperty("entity_type").GetString() == "order")
                .ToArray();

            Assert.NotEmpty(orderChanges);
            Assert.Contains(
                orderChanges,
                value => value.GetProperty("data").GetProperty("status").GetString() == "ready");
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
            "batch-1",
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
        new(
            "device-waiter",
            1,
            "waiter-1",
            "Waiter One",
            "waiter",
            "tenant-1");

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
                new StaffSnapshot(99, "kitchen-99", "Kitchen User", "kitchen@example.test", "kitchen", true),
            ],
            [
                new MenuCategorySnapshot(
                    "category-1",
                    "Main",
                    1,
                    [
                        new MenuItemSnapshot(
                            "item-grill",
                            "category-1",
                            "GRILL-1",
                            "Grilled Chicken",
                            null,
                            350m,
                            "AFN",
                            1,
                            []),
                        new MenuItemSnapshot(
                            "item-general",
                            "category-1",
                            "GEN-1",
                            "Salad",
                            null,
                            120m,
                            "AFN",
                            2,
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
                        "station-grill",
                        "branch-1",
                        "GRILL",
                        "Grill Station",
                        1,
                        true),
                ],
                [
                    new MenuItemKitchenRouteSnapshot(
                        "route-1",
                        "item-grill",
                        "branch-1",
                        "station-grill"),
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
