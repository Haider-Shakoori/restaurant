using BusinessOS.Restaurant.Application.OperationalData;
using BusinessOS.Restaurant.Persistence;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class OperationalDataTests
{
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
