using BusinessOS.Restaurant.Application.OperationalData;
using BusinessOS.Restaurant.Persistence;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class OperationalDataTests
{
    [Fact]
    public async Task Authoritative_table_snapshot_preserves_offline_occupancy_and_archived_history()
    {
        var root = Path.Combine(Path.GetTempPath(), "BusinessOS.Restaurant.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var factory = new LocalDatabaseFactory(root);
            var store = new OperationalSnapshotStore(factory);
            var table = new DiningTableSnapshot("table-1", "T-01", "Window Table", 5,
                "available", true, new DiningAreaSummary("area-1", "Main Hall"),
                new BranchSummary("branch-1", "Main Branch"));
            var snapshot = new OperationalSnapshot(1, DateTimeOffset.UtcNow, 1, "tenant-a",
                [new BranchSnapshot("branch-1", "MAIN", "Main Branch", true)],
                [], [], [table],
                Areas:
                [
                    new DiningAreaReferenceSnapshot("area-1", "branch-1", "Main Hall", 1, true),
                    new DiningAreaReferenceSnapshot("area-empty", "branch-1", "Roof Terrace", 2, true),
                ]);

            await store.ApplyAsync(snapshot);
            var catalog = await store.LoadCatalogAsync();
            Assert.Single(catalog.Tables);
            Assert.Equal("Window Table", catalog.Tables[0].Name);
            Assert.Equal(2, catalog.Areas.Count); // empty floors must still sync

            await using (var db = factory.Create())
            {
                db.Orders.Add(new LocalOrder
                {
                    Id = "order-1", ClientOrderId = "local-order-1", DiningTableId = "table-1",
                    BranchId = "branch-1", WaiterPublicId = "waiter-1", WaiterName = "Waiter",
                    Status = "draft", ServiceType = "dine_in", CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            await store.ApplyAsync(snapshot with
            {
                Cursor = 2,
                Tables = [table with { Name = "Window Table Updated", Status = "available" }],
            });
            catalog = await store.LoadCatalogAsync();
            Assert.Equal("Window Table Updated", catalog.Tables[0].Name);
            Assert.Equal("occupied", catalog.Tables[0].Status); // cloud cannot reopen an offline occupied table

            // Cloud archive hides the table from current ordering but retains
            // its SQLite row and original order for historical reconciliation.
            await store.ApplyAsync(snapshot with { Cursor = 3, Tables = [], Areas = [] });
            Assert.Empty((await store.LoadCatalogAsync()).Tables);
            await using (var db = factory.Create())
            {
                Assert.NotNull(await db.DiningTables.FindAsync("table-1"));
                Assert.NotNull(await db.Orders.FindAsync("order-1"));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Operational_snapshot_round_trips_into_local_sqlite_catalog()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "BusinessOS.Restaurant.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var factory = new LocalDatabaseFactory(root);
            var store = new OperationalSnapshotStore(factory);

            var snapshot = new OperationalSnapshot(
                1,
                DateTimeOffset.UtcNow,
                42,
                "tenant-1",
                [
                    new BranchSnapshot("branch-1", "MAIN", "Main Branch", true),
                ],
                [
                    new StaffSnapshot(1, "staff-1", "Waiter One", "waiter@example.test", "waiter", true),
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
                                "FOOD-001",
                                "Kabuli Pulao",
                                null,
                                250m,
                                "AFN",
                                1,
                                [
                                    new ModifierGroupSnapshot(
                                        "group-1",
                                        "Size",
                                        1,
                                        1,
                                        1,
                                        [
                                            new ModifierOptionSnapshot(
                                                "option-1",
                                                "Large",
                                                50m,
                                                1),
                                        ]),
                                ]),
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
                ]);

            await store.ApplyAsync(snapshot);
            var catalog = await store.LoadCatalogAsync();

            Assert.Equal("tenant-1", catalog.State?.TenantId);
            Assert.Equal(42, catalog.State?.Cursor);
            Assert.Single(catalog.Branches);
            Assert.Single(catalog.Tables);
            Assert.Single(catalog.Categories);
            Assert.Single(catalog.Items);
            Assert.Single(catalog.ModifierGroups);
            Assert.Single(catalog.ModifierOptions);
            Assert.Single(catalog.MenuItemModifierGroups);
            Assert.Equal(250m, catalog.Items[0].Price);
            Assert.Equal(50m, catalog.ModifierOptions[0].PriceDelta);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
