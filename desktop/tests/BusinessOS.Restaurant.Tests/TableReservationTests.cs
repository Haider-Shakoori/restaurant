using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

/// <summary>
/// Regression guard: stale occupancy (occupied table with no active order) is
/// released, tables with live orders are never touched, and reservations
/// round-trip with their note.
/// </summary>
public sealed class TableReservationTests
{
    [Fact]
    public async Task Stale_occupancy_is_released_and_reservations_round_trip()
    {
        var root = Directory.CreateTempSubdirectory("bos-reserve-").FullName;
        try
        {
            var factory = new LocalDatabaseFactory(root);
            await factory.EnsureCreatedAsync();
            await using (var seed = factory.Create())
            {
                seed.Branches.Add(new LocalBranch
                {
                    Id = "branch-1", Code = "MAIN", Name = "Main", IsActive = true,
                });
                seed.DiningAreas.Add(new LocalDiningArea
                {
                    Id = "area-1", BranchId = "branch-1", Name = "Hall", SortOrder = 1, IsActive = true,
                });
                seed.DiningTables.AddRange(
                    new LocalDiningTable
                    {
                        Id = "t-1", DiningAreaId = "area-1", Code = "T1", Name = "Table 1",
                        Capacity = 4, Status = "occupied", IsActive = true,
                    },
                    new LocalDiningTable
                    {
                        Id = "t-2", DiningAreaId = "area-1", Code = "T2", Name = "Table 2",
                        Capacity = 4, Status = "occupied", IsActive = true,
                    },
                    new LocalDiningTable
                    {
                        Id = "t-3", DiningAreaId = "area-1", Code = "T3", Name = "Table 3",
                        Capacity = 2, Status = "available", IsActive = true,
                    });
                // t-2 stays occupied because a live order references it.
                seed.Orders.Add(new LocalOrder
                {
                    Id = "order-1", ClientOrderId = "ORD-1", DiningTableId = "t-2", BranchId = "branch-1",
                    ServiceType = "dine_in", WaiterId = 1, WaiterPublicId = "u1", WaiterName = "Tester",
                    Status = "draft", GuestCount = 2,
                    CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow,
                });
                await seed.SaveChangesAsync();
            }

            var cashier = new LocalCashierService(factory);
            var actor = new LocalTerminalPrincipal("device-1", 1, "u1", "Tester", "cashier", "tenant-1");

            var released = await cashier.ReconcileOccupancyAsync(actor, CancellationToken.None);
            Assert.Equal(1, released);
            await using (var check = factory.Create())
            {
                Assert.Equal("available", (await check.DiningTables.SingleAsync(x => x.Id == "t-1")).Status);
                Assert.Equal("occupied", (await check.DiningTables.SingleAsync(x => x.Id == "t-2")).Status);
            }

            await cashier.ReserveTableAsync("t-3", "19:30, Janssen, 4 guests", actor, CancellationToken.None);
            await using (var check = factory.Create())
            {
                var reserved = await check.DiningTables.SingleAsync(x => x.Id == "t-3");
                Assert.Equal("reserved", reserved.Status);
                Assert.Equal("19:30, Janssen, 4 guests", reserved.ReservationNote);
            }

            await cashier.CancelReservationAsync("t-3", actor, CancellationToken.None);
            await using (var check = factory.Create())
            {
                var free = await check.DiningTables.SingleAsync(x => x.Id == "t-3");
                Assert.Equal("available", free.Status);
                Assert.Null(free.ReservationNote);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
