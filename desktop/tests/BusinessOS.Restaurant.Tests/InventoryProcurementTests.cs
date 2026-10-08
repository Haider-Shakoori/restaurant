using System.Text.Json;
using BusinessOS.Restaurant.Application.OperationalData;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class InventoryProcurementTests
{
    [Fact]
    public async Task Desktop_one_step_purchase_posts_stock_and_completes_order()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var factory = new LocalDatabaseFactory(root);
            await new OperationalSnapshotStore(factory).ApplyAsync(Snapshot());
            var inventory = new LocalInventoryService(factory);
            var actor = InventoryUser();

            var item = JsonSerializer.SerializeToElement(await inventory.CreateItemAsync(
                "FLOUR", "Flour", "kg", "kg", 1m, 0m, actor, CancellationToken.None));
            var supplier = JsonSerializer.SerializeToElement(await inventory.CreateSupplierAsync(
                "SUP-FLOUR", "Flour Supplier", null, null, null, actor, CancellationToken.None));
            var purchase = JsonSerializer.SerializeToElement(await inventory.CreatePurchaseOrderAsync(
                "branch-1", supplier.GetProperty("id").GetString()!,
                [new LocalPurchaseOrderLineRequest(item.GetProperty("id").GetString()!, 3m, 75m)],
                "Desktop one-step purchase", actor, CancellationToken.None));
            var poId = purchase.GetProperty("id").GetString()!;
            var lineId = purchase.GetProperty("lines")[0].GetProperty("id").GetString()!;

            await inventory.ReceivePurchaseOrderAsync(
                poId, [new LocalReceivePurchaseOrderLineRequest(lineId, 3m)],
                "desktop-receipt-" + poId, "Received through desktop purchase form",
                actor, CancellationToken.None);

            await using var db = factory.Create();
            Assert.Equal("received", (await db.PurchaseOrders.SingleAsync()).Status);
            Assert.Equal(3m, (await db.InventoryBalances.SingleAsync()).Quantity);
            Assert.Equal(225m, (await db.InventoryValuations.SingleAsync()).Value);
            Assert.Single(await db.GoodsReceipts.ToListAsync());
            Assert.Single(await db.StockMovements.ToListAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Purchase_receipts_are_idempotent_and_update_weighted_average_stock()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalog = new OperationalSnapshotStore(factory);
            await catalog.ApplyAsync(Snapshot());

            var inventory = new LocalInventoryService(factory);
            var user = InventoryUser();

            var itemJson = JsonSerializer.SerializeToElement(
                await inventory.CreateItemAsync(
                    "RICE",
                    "Basmati Rice",
                    "g",
                    "kg",
                    1000m,
                    1600m,
                    user,
                    CancellationToken.None));
            var itemId = itemJson.GetProperty("id").GetString()!;

            var supplierJson = JsonSerializer.SerializeToElement(
                await inventory.CreateSupplierAsync(
                    "SUP-01",
                    "Main Foods",
                    null,
                    null,
                    null,
                    user,
                    CancellationToken.None));
            var supplierId = supplierJson.GetProperty("id").GetString()!;

            var poJson = JsonSerializer.SerializeToElement(
                await inventory.CreatePurchaseOrderAsync(
                    "branch-1",
                    supplierId,
                    [
                        new LocalPurchaseOrderLineRequest(itemId, 2m, 200m),
                    ],
                    "Weekly rice purchase",
                    user,
                    CancellationToken.None));
            var poId = poJson.GetProperty("id").GetString()!;
            var poLineId = poJson.GetProperty("lines")[0].GetProperty("id").GetString()!;

            Assert.Equal("400.00", poJson.GetProperty("estimated_total").GetString());

            var firstReceipt = JsonSerializer.SerializeToElement(
                await inventory.ReceivePurchaseOrderAsync(
                    poId,
                    [new LocalReceivePurchaseOrderLineRequest(poLineId, 1m)],
                    "GRN-CLIENT-1",
                    null,
                    user,
                    CancellationToken.None));

            Assert.Equal("posted", firstReceipt.GetProperty("status").GetString());

            var replay = JsonSerializer.SerializeToElement(
                await inventory.ReceivePurchaseOrderAsync(
                    poId,
                    [new LocalReceivePurchaseOrderLineRequest(poLineId, 1m)],
                    "GRN-CLIENT-1",
                    null,
                    user,
                    CancellationToken.None));

            Assert.Equal(
                firstReceipt.GetProperty("id").GetString(),
                replay.GetProperty("id").GetString());

            await using (var db = factory.Create())
            {
                Assert.Equal(1000m, (await db.InventoryBalances.SingleAsync()).Quantity);
                Assert.Equal(1000m, (await db.InventoryValuations.SingleAsync()).Quantity);
                Assert.Equal(200m, (await db.InventoryValuations.SingleAsync()).Value);
                Assert.Equal(0.2m, (await db.InventoryValuations.SingleAsync()).AverageUnitCost);
                Assert.Single(await db.StockMovements.ToArrayAsync());
                Assert.Single(await db.GoodsReceipts.ToArrayAsync());
                Assert.Equal("partially_received", (await db.PurchaseOrders.SingleAsync()).Status);
            }

            await inventory.ReceivePurchaseOrderAsync(
                poId,
                [new LocalReceivePurchaseOrderLineRequest(poLineId, 1m)],
                "GRN-CLIENT-2",
                null,
                user,
                CancellationToken.None);

            await using (var db = factory.Create())
            {
                Assert.Equal(2000m, (await db.InventoryBalances.SingleAsync()).Quantity);
                Assert.Equal(2000m, (await db.InventoryValuations.SingleAsync()).Quantity);
                Assert.Equal(400m, (await db.InventoryValuations.SingleAsync()).Value);
                Assert.Equal(0.2m, (await db.InventoryValuations.SingleAsync()).AverageUnitCost);
                Assert.Equal(2, await db.StockMovements.CountAsync());
                Assert.Equal("received", (await db.PurchaseOrders.SingleAsync()).Status);
            }

            var items = JsonSerializer.SerializeToElement(
                await inventory.ItemsAsync(
                    "branch-1",
                    lowStockOnly: false,
                    user,
                    CancellationToken.None));

            Assert.False(items[0].GetProperty("low_stock").GetBoolean());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Receipt_rejects_overdelivery_without_mutating_stock_or_order()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var factory = new LocalDatabaseFactory(root);
            await new OperationalSnapshotStore(factory).ApplyAsync(Snapshot());
            var inventory = new LocalInventoryService(factory);
            var actor = InventoryUser();

            var item = JsonSerializer.SerializeToElement(await inventory.CreateItemAsync(
                "OVER-RICE", "Rice for receipt validation", "g", "kg", 1000m, 0m, actor, CancellationToken.None));
            var supplier = JsonSerializer.SerializeToElement(await inventory.CreateSupplierAsync(
                "OVER-SUP", "Receipt Validation Supplier", null, null, null, actor, CancellationToken.None));
            var order = JsonSerializer.SerializeToElement(await inventory.CreatePurchaseOrderAsync(
                "branch-1", supplier.GetProperty("id").GetString()!,
                [new LocalPurchaseOrderLineRequest(item.GetProperty("id").GetString()!, 2m, 200m)],
                null, actor, CancellationToken.None));
            var orderId = order.GetProperty("id").GetString()!;
            var lineId = order.GetProperty("lines")[0].GetProperty("id").GetString()!;

            var error = await Assert.ThrowsAsync<LocalSyncConflictException>(() =>
                inventory.ReceivePurchaseOrderAsync(orderId,
                    [new LocalReceivePurchaseOrderLineRequest(lineId, 3m)],
                    "OVER-RECEIPT", null, actor, CancellationToken.None));
            Assert.Equal("invalid_payload", error.Code);

            await using var db = factory.Create();
            Assert.Empty(await db.GoodsReceipts.ToArrayAsync());
            Assert.Empty(await db.StockMovements.ToArrayAsync());
            Assert.Empty(await db.InventoryBalances.ToArrayAsync());
            Assert.Equal("ordered", (await db.PurchaseOrders.SingleAsync()).Status);
            Assert.Equal(0m, (await db.PurchaseOrderLines.SingleAsync()).ReceivedBaseQuantity);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Receipt_idempotency_key_cannot_be_reused_across_purchase_orders()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var factory = new LocalDatabaseFactory(root);
            await new OperationalSnapshotStore(factory).ApplyAsync(Snapshot());
            var inventory = new LocalInventoryService(factory);
            var actor = InventoryUser();
            var item = JsonSerializer.SerializeToElement(await inventory.CreateItemAsync(
                "KEY-RICE", "Rice for replay validation", "g", "kg", 1000m, 0m, actor, CancellationToken.None));
            var supplier = JsonSerializer.SerializeToElement(await inventory.CreateSupplierAsync(
                "KEY-SUP", "Replay Validation Supplier", null, null, null, actor, CancellationToken.None));
            var itemId = item.GetProperty("id").GetString()!;
            var supplierId = supplier.GetProperty("id").GetString()!;

            async Task<(string OrderId, string LineId)> CreateOrderAsync()
            {
                var po = JsonSerializer.SerializeToElement(await inventory.CreatePurchaseOrderAsync(
                    "branch-1", supplierId, [new LocalPurchaseOrderLineRequest(itemId, 1m, 100m)],
                    null, actor, CancellationToken.None));
                return (po.GetProperty("id").GetString()!, po.GetProperty("lines")[0].GetProperty("id").GetString()!);
            }

            var first = await CreateOrderAsync();
            var second = await CreateOrderAsync();
            await inventory.ReceivePurchaseOrderAsync(first.OrderId,
                [new LocalReceivePurchaseOrderLineRequest(first.LineId, 1m)],
                "SHARED-GRN-ID", null, actor, CancellationToken.None);

            var conflict = await Assert.ThrowsAsync<LocalSyncConflictException>(() =>
                inventory.ReceivePurchaseOrderAsync(second.OrderId,
                    [new LocalReceivePurchaseOrderLineRequest(second.LineId, 1m)],
                    "SHARED-GRN-ID", null, actor, CancellationToken.None));
            Assert.Equal("receipt_conflict", conflict.Code);

            await using var db = factory.Create();
            Assert.Single(await db.GoodsReceipts.ToArrayAsync());
            Assert.Single(await db.StockMovements.ToArrayAsync());
            Assert.Equal(1000m, (await db.InventoryBalances.SingleAsync()).Quantity);
            Assert.Equal("ordered", (await db.PurchaseOrders.SingleAsync(value => value.Id == second.OrderId)).Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Serving_order_consumes_active_recipe_once_and_low_stock_updates()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalog = new OperationalSnapshotStore(factory);
            await catalog.ApplyAsync(Snapshot());

            var inventory = new LocalInventoryService(factory);
            var user = InventoryUser();

            var itemJson = JsonSerializer.SerializeToElement(
                await inventory.CreateItemAsync(
                    "RICE",
                    "Basmati Rice",
                    "g",
                    "kg",
                    1000m,
                    1600m,
                    user,
                    CancellationToken.None));
            var itemId = itemJson.GetProperty("id").GetString()!;

            await inventory.AdjustAsync(
                "branch-1",
                itemId,
                2000m,
                "OPENING-STOCK",
                "Opening stock",
                user,
                CancellationToken.None);

            await using (var db = factory.Create())
            {
                var valuation = await db.InventoryValuations.SingleAsync();
                valuation.Value = 400m;
                valuation.AverageUnitCost = 0.2m;
                await db.SaveChangesAsync();
            }

            await inventory.CreateRecipeVersionAsync(
                "branch-1",
                "item-1",
                "Kabuli Pulao Standard",
                [new LocalRecipeComponentRequest(itemId, 250m)],
                user,
                CancellationToken.None);

            var kitchen = new LocalKitchenService(factory);
            var sync = new LocalSyncService(factory, catalog, kitchen);
            var cashier = new LocalCashierService(factory, inventory);

            await PushOneAsync(sync, Waiter(), "OPEN", "order.open", new
            {
                client_order_id = "ORDER-1",
                dining_table_id = "table-1",
                guest_count = 2,
            });

            await PushOneAsync(sync, Waiter(), "ADD", "order.item.add", new
            {
                client_order_id = "ORDER-1",
                client_line_id = "LINE-1",
                menu_item_id = "item-1",
                quantity = 2,
            });

            await PushOneAsync(sync, Waiter(), "SUBMIT", "order.submit", new
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

            await cashier.ServeOrderAsync(orderId, Cashier(), CancellationToken.None);
            await cashier.ServeOrderAsync(orderId, Cashier(), CancellationToken.None);

            await using (var db = factory.Create())
            {
                var balance = await db.InventoryBalances.SingleAsync();
                var valuation = await db.InventoryValuations.SingleAsync();

                Assert.Equal(1500m, balance.Quantity);
                Assert.Equal(1500m, valuation.Quantity);
                Assert.Equal(300m, valuation.Value);
                Assert.Equal(0.2m, valuation.AverageUnitCost);
                Assert.Single(await db.InventoryConsumptions.ToArrayAsync());
                Assert.Single(await db.InventoryConsumptionLines.ToArrayAsync());

                var consumptionMovements = await db.StockMovements
                    .Where(value => value.MovementType == "consumption")
                    .ToArrayAsync();

                Assert.Single(consumptionMovements);
                Assert.Equal(-500m, consumptionMovements[0].QuantityDelta);
            }

            var lowStock = JsonSerializer.SerializeToElement(
                await inventory.ItemsAsync(
                    "branch-1",
                    lowStockOnly: true,
                    user,
                    CancellationToken.None));

            Assert.Single(lowStock.EnumerateArray());
            Assert.True(lowStock[0].GetProperty("low_stock").GetBoolean());

            var replayAdjustmentOne = JsonSerializer.SerializeToElement(
                await inventory.AdjustAsync(
                    "branch-1",
                    itemId,
                    100m,
                    "ADJ-1",
                    "Count correction",
                    user,
                    CancellationToken.None));

            var replayAdjustmentTwo = JsonSerializer.SerializeToElement(
                await inventory.AdjustAsync(
                    "branch-1",
                    itemId,
                    100m,
                    "ADJ-1",
                    "Count correction",
                    user,
                    CancellationToken.None));

            Assert.Equal(
                replayAdjustmentOne.GetProperty("id").GetString(),
                replayAdjustmentTwo.GetProperty("id").GetString());

            await using (var db = factory.Create())
            {
                Assert.Equal(1600m, (await db.InventoryBalances.SingleAsync()).Quantity);
                Assert.Equal(1, await db.StockMovements.CountAsync(value => value.MovementType == "adjustment" && value.IdempotencyKey == "adjustment:branch-1:ADJ-1"));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Serving_multi_ingredient_recipe_consumes_each_component_once()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var factory = new LocalDatabaseFactory(root);
            await new OperationalSnapshotStore(factory).ApplyAsync(Snapshot());
            var inventory = new LocalInventoryService(factory);
            var actor = InventoryUser();

            async Task<string> AddIngredientAsync(string sku, string name, decimal opening)
            {
                var item = JsonSerializer.SerializeToElement(await inventory.CreateItemAsync(
                    sku, name, "g", "kg", 1000m, 0m, actor, CancellationToken.None));
                var id = item.GetProperty("id").GetString()!;
                await inventory.AdjustAsync("branch-1", id, opening, "OPEN-" + sku,
                    "Opening inventory", actor, CancellationToken.None);
                return id;
            }

            var riceId = await AddIngredientAsync("MULTI-RICE", "Multi Rice", 2000m);
            var spiceId = await AddIngredientAsync("MULTI-SPICE", "Multi Spice", 1000m);
            await inventory.CreateRecipeVersionAsync("branch-1", "item-1", "Two ingredient recipe",
                [new LocalRecipeComponentRequest(riceId, 250m),
                 new LocalRecipeComponentRequest(spiceId, 20m)],
                actor, CancellationToken.None);

            var kitchen = new LocalKitchenService(factory);
            var sync = new LocalSyncService(factory, new OperationalSnapshotStore(factory), kitchen);
            var cashier = new LocalCashierService(factory, inventory);
            await PushOneAsync(sync, Waiter(), "MULTI-OPEN", "order.open", new
            {
                client_order_id = "MULTI-ORDER",
                dining_table_id = "table-1",
                guest_count = 2,
            });
            await PushOneAsync(sync, Waiter(), "MULTI-ADD", "order.item.add", new
            {
                client_order_id = "MULTI-ORDER",
                client_line_id = "MULTI-LINE",
                menu_item_id = "item-1",
                quantity = 2,
            });
            await PushOneAsync(sync, Waiter(), "MULTI-SUBMIT", "order.submit", new
            {
                client_order_id = "MULTI-ORDER",
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

            await cashier.ServeOrderAsync(orderId, Cashier(), CancellationToken.None);
            await cashier.ServeOrderAsync(orderId, Cashier(), CancellationToken.None);

            await using var verify = factory.Create();
            var balances = await verify.InventoryBalances.ToDictionaryAsync(value => value.InventoryItemId);
            Assert.Equal(1500m, balances[riceId].Quantity);
            Assert.Equal(960m, balances[spiceId].Quantity);
            Assert.Single(await verify.InventoryConsumptions.ToArrayAsync());
            Assert.Equal(2, await verify.InventoryConsumptionLines.CountAsync());
            var movements = await verify.StockMovements
                .Where(value => value.MovementType == "consumption")
                .ToDictionaryAsync(value => value.InventoryItemId);
            Assert.Equal(2, movements.Count);
            Assert.Equal(-500m, movements[riceId].QuantityDelta);
            Assert.Equal(-40m, movements[spiceId].QuantityDelta);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Recipe_versions_deactivate_previous_version()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalog = new OperationalSnapshotStore(factory);
            await catalog.ApplyAsync(Snapshot());
            var inventory = new LocalInventoryService(factory);
            var user = InventoryUser();

            var itemJson = JsonSerializer.SerializeToElement(
                await inventory.CreateItemAsync(
                    "RICE",
                    "Basmati Rice",
                    "g",
                    "kg",
                    1000m,
                    0m,
                    user,
                    CancellationToken.None));
            var itemId = itemJson.GetProperty("id").GetString()!;

            var first = JsonSerializer.SerializeToElement(
                await inventory.CreateRecipeVersionAsync(
                    "branch-1",
                    "item-1",
                    null,
                    [new LocalRecipeComponentRequest(itemId, 200m)],
                    user,
                    CancellationToken.None));

            var second = JsonSerializer.SerializeToElement(
                await inventory.CreateRecipeVersionAsync(
                    "branch-1",
                    "item-1",
                    null,
                    [new LocalRecipeComponentRequest(itemId, 250m)],
                    user,
                    CancellationToken.None));

            Assert.Equal(1, first.GetProperty("version").GetInt32());
            Assert.Equal(2, second.GetProperty("version").GetInt32());

            await using var db = factory.Create();
            var recipes = await db.Recipes.OrderBy(value => value.Version).ToArrayAsync();

            Assert.False(recipes[0].IsActive);
            Assert.True(recipes[1].IsActive);
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
            "batch-10",
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

    private static LocalTerminalPrincipal InventoryUser() =>
        new("inventory-device", 5, "inventory-1", "Inventory User", "inventory", "tenant-1");

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
                new StaffSnapshot(5, "inventory-1", "Inventory User", "inventory@example.test", "inventory", true),
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
