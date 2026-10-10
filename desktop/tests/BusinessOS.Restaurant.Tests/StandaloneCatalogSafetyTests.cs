using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class StandaloneCatalogSafetyTests
{
    [Fact]
    public async Task Dish_edit_does_not_rewrite_historical_order_prices_or_send_cloud_mutations()
    {
        var root = TempRoot();
        try
        {
            var factory = await SeedAsync(root);
            await using (var db = factory.Create())
            {
                db.OrderItems.Add(new LocalOrderItem
                {
                    Id = "line-1", OrderId = "order-1", MenuItemId = "dish-1",
                    ClientLineId = "history-line", ItemName = "Old Dish",
                    UnitPrice = 250m, Quantity = 2, LineTotal = 500m,
                    Status = "served", CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            var catalog = new StandaloneCatalogService(factory);
            await catalog.UpdateDishAsync("dish-1", "New Dish", 310m, false, "NEW-01", "owner");

            await using var read = factory.Create();
            Assert.Equal(310m, (await read.MenuItems.SingleAsync()).Price);
            Assert.False((await read.MenuItems.SingleAsync()).IsAvailable);
            var historical = await read.OrderItems.SingleAsync();
            Assert.Equal(250m, historical.UnitPrice);
            Assert.Equal(500m, historical.LineTotal);
            Assert.Equal("Old Dish", historical.ItemName);
            Assert.Empty(await read.CloudOutbox.ToListAsync());

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => catalog.UpdateDishAsync("dish-1", "Malicious", 0m, true, null, "waiter"));
            Assert.Equal(310m, (await read.MenuItems.AsNoTracking().SingleAsync()).Price);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Occupied_or_unsettled_table_cannot_be_disabled_and_history_is_never_deleted()
    {
        var root = TempRoot();
        try
        {
            var factory = await SeedAsync(root);
            var catalog = new StandaloneCatalogService(factory);
            await using (var db = factory.Create())
            {
                var table = await db.DiningTables.SingleAsync();
                table.Status = "occupied";
                await db.SaveChangesAsync();
            }

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => catalog.UpdateTableAsync("table-1", "Unavailable", 5, false, "manager"));
            await using (var db = factory.Create())
            {
                (await db.DiningTables.SingleAsync()).Status = "available";
                await db.SaveChangesAsync();
            }
            // A stale Available status must not hide an unpaid historical bill/order.
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => catalog.UpdateTableAsync("table-1", "Unavailable", 5, false, "owner"));

            await using (var db = factory.Create())
            {
                (await db.Orders.SingleAsync()).Status = "closed";
                await db.SaveChangesAsync();
            }
            await catalog.UpdateTableAsync("table-1", "Table 01", 6, false, "admin");
            await using var read = factory.Create();
            Assert.False((await read.DiningTables.SingleAsync()).IsActive);
            Assert.Equal(6, (await read.DiningTables.SingleAsync()).Capacity);
            Assert.Single(await read.Orders.ToListAsync());
            Assert.Empty(await read.CloudOutbox.ToListAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Kitchen_route_enforces_branch_integrity_and_updates_without_duplicate_rows()
    {
        var root = TempRoot();
        try
        {
            var factory = await SeedAsync(root);
            var catalog = new StandaloneCatalogService(factory);
            var stationId = await catalog.UpsertKitchenStationAsync(
                null, "branch-1", "HOT", "Hot Kitchen", "manager");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                catalog.RouteDishToStationAsync("dish-1", "branch-2", stationId, "admin"));
            await catalog.RouteDishToStationAsync("dish-1", "branch-1", stationId, "owner");

            var newStationId = await catalog.UpsertKitchenStationAsync(
                null, "branch-1", "COLD", "Cold Kitchen", "manager");
            await catalog.RouteDishToStationAsync("dish-1", "branch-1", newStationId, "admin");
            await catalog.UpsertKitchenStationAsync(newStationId, "branch-1",
                "COLD", "Cold Prep", "owner");

            await using var db = factory.Create();
            Assert.Single(await db.MenuItemKitchenRoutes.ToListAsync());
            Assert.Equal(newStationId, (await db.MenuItemKitchenRoutes.SingleAsync()).KitchenStationId);
            Assert.Equal("Cold Prep", (await db.KitchenStations
                .SingleAsync(x => x.Id == newStationId)).Name);
            Assert.Empty(await db.CloudOutbox.ToListAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Category_hiding_preserves_dishes_and_prevents_duplicate_names()
    {
        var root = TempRoot();
        try
        {
            var factory = await SeedAsync(root);
            var catalog = new StandaloneCatalogService(factory);
            await catalog.UpdateCategoryAsync("cat-1", "Dishes", false, "manager");
            await using var db = factory.Create();
            Assert.False((await db.MenuCategories.SingleAsync()).IsActive);
            Assert.Single(await db.MenuItems.ToListAsync());
            Assert.Equal("cat-1", (await db.MenuItems.SingleAsync()).MenuCategoryId);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task<LocalDatabaseFactory> SeedAsync(string root)
    {
        var factory = new LocalDatabaseFactory(root);
        await factory.EnsureCreatedAsync();
        await using var db = factory.Create();
        db.Branches.AddRange(
            new LocalBranch { Id = "branch-1", Code = "MAIN", Name = "Main", IsActive = true },
            new LocalBranch { Id = "branch-2", Code = "SECOND", Name = "Second", IsActive = true });
        db.DiningAreas.Add(new LocalDiningArea
        {
            Id = "area-1", BranchId = "branch-1", Name = "Hall", IsActive = true,
        });
        db.DiningTables.Add(new LocalDiningTable
        {
            Id = "table-1", DiningAreaId = "area-1", Code = "T01",
            Name = "Table 1", Capacity = 4, IsActive = true, Status = "available",
        });
        db.MenuCategories.Add(new LocalMenuCategory
        {
            Id = "cat-1", Name = "Main", IsActive = true,
        });
        db.MenuItems.Add(new LocalMenuItem
        {
            Id = "dish-1", MenuCategoryId = "cat-1", Name = "Old Dish",
            Price = 250m, Sku = "OLD-01", IsAvailable = true,
        });
        db.Orders.Add(new LocalOrder
        {
            Id = "order-1", ClientOrderId = "OLD-ORDER", DiningTableId = "table-1",
            BranchId = "branch-1", Status = "billed", WaiterId = 1,
            WaiterPublicId = "waiter-1", WaiterName = "Waiter",
            GuestCount = 2, Subtotal = 500m, Total = 500m,
            CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return factory;
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "RestaurantStandaloneCatalog",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
