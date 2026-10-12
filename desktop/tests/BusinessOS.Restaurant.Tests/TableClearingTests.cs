using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

/// <summary>
/// Regression guard for manual table clearing: the payment choice is honored
/// (a held table stays occupied until cleared), clearing is cashier-friendly
/// but never allowed over an active order, and settling a bill completes any
/// kitchen ticket still dangling on the live board.
/// </summary>
public sealed class TableClearingTests
{
    [Fact]
    public async Task Held_table_stays_occupied_until_a_cashier_clears_it()
    {
        var root = Directory.CreateTempSubdirectory("bos-clearing-").FullName;
        try
        {
            var factory = new LocalDatabaseFactory(root);
            await factory.EnsureCreatedAsync();
            await SeedDiningRoomAsync(factory);

            var cashier = new LocalCashierService(factory);
            var actor = new LocalTerminalPrincipal("device-1", 1, "u1", "Tester", "cashier", "tenant-1");

            await cashier.HoldTableAsync("t-1", "Paid · awaiting clearing", actor, CancellationToken.None);
            await using (var check = factory.Create())
            {
                var held = await check.DiningTables.SingleAsync(x => x.Id == "t-1");
                Assert.Equal("occupied", held.Status);
                Assert.Equal("Paid · awaiting clearing", held.ReservationNote);
            }

            await cashier.ClearTableAsync("t-1", actor, CancellationToken.None, "Cleared from the dining room");
            await using (var check = factory.Create())
            {
                var free = await check.DiningTables.SingleAsync(x => x.Id == "t-1");
                Assert.Equal("available", free.Status);
                Assert.Null(free.ReservationNote);
            }

            // Roles below cashier cannot hold or clear tables.
            var waiter = actor with { UserRole = "waiter" };
            await Assert.ThrowsAsync<LocalSyncConflictException>(
                () => cashier.HoldTableAsync("t-1", null, waiter, CancellationToken.None));
            await Assert.ThrowsAsync<LocalSyncConflictException>(
                () => cashier.ClearTableAsync("t-1", waiter, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Clearing_is_rejected_while_an_active_order_holds_the_table()
    {
        var root = Directory.CreateTempSubdirectory("bos-clearing-").FullName;
        try
        {
            var factory = new LocalDatabaseFactory(root);
            await factory.EnsureCreatedAsync();
            await SeedDiningRoomAsync(factory);
            await using (var seed = factory.Create())
            {
                seed.Orders.Add(NewOrder("order-active", "t-2", "draft"));
                await seed.SaveChangesAsync();
            }

            var cashier = new LocalCashierService(factory);
            var actor = new LocalTerminalPrincipal("device-1", 1, "u1", "Tester", "cashier", "tenant-1");

            await Assert.ThrowsAsync<LocalSyncConflictException>(
                () => cashier.ClearTableAsync("t-2", actor, CancellationToken.None));
            await using (var check = factory.Create())
            {
                Assert.Equal("occupied", (await check.DiningTables.SingleAsync(x => x.Id == "t-2")).Status);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Settling_a_bill_completes_stranded_kitchen_tickets()
    {
        var root = Directory.CreateTempSubdirectory("bos-clearing-").FullName;
        try
        {
            var factory = new LocalDatabaseFactory(root);
            await factory.EnsureCreatedAsync();
            await SeedDiningRoomAsync(factory);
            var now = DateTimeOffset.UtcNow;
            await using (var seed = factory.Create())
            {
                seed.Orders.Add(NewOrder("order-1", "t-2", "served"));
                seed.Bills.Add(new LocalBill
                {
                    Id = "bill-1", OrderId = "order-1", BranchId = "branch-1", CreatedByUserId = 1,
                    BillNumber = "BILL-0001", Status = "open",
                    Subtotal = 600m, Total = 600m, PaidAmount = 0m, BalanceDue = 600m, IssuedAt = now,
                });
                seed.CashierSessions.Add(new LocalCashierSession
                {
                    Id = "session-1", BranchId = "branch-1", CashierUserId = 1, CashierName = "Tester",
                    Status = "open", OpenedAt = now,
                });
                seed.KitchenStations.Add(new LocalKitchenStation
                {
                    Id = "station-1", BranchId = "branch-1", Code = "GENERAL", Name = "General Kitchen",
                    SortOrder = 1, IsActive = true,
                });
                seed.KitchenTickets.Add(new LocalKitchenTicket
                {
                    Id = "ticket-1", OrderId = "order-1", KitchenStationId = "station-1",
                    TicketNumber = "KOT-0001-MAIN", Status = "ready",
                    QueuedAt = now, CreatedAtUtc = now, UpdatedAtUtc = now,
                });
                seed.KitchenTicketItems.Add(new LocalKitchenTicketItem
                {
                    Id = "ticket-item-1", KitchenTicketId = "ticket-1", OrderItemId = "order-item-1",
                    ItemName = "Qabuli", Quantity = 1, Status = "ready",
                });
                await seed.SaveChangesAsync();
            }

            var cashier = new LocalCashierService(factory);
            var actor = new LocalTerminalPrincipal("device-1", 1, "u1", "Tester", "cashier", "tenant-1");
            await cashier.AddPaymentAsync(
                "bill-1",
                new LocalPaymentRequest("session-1", 600m, "cash"),
                actor,
                CancellationToken.None);

            await using (var check = factory.Create())
            {
                Assert.Equal("closed", (await check.Orders.SingleAsync(x => x.Id == "order-1")).Status);
                Assert.Equal("available", (await check.DiningTables.SingleAsync(x => x.Id == "t-2")).Status);
                // A paid order must never strand a KOT card on the live board.
                Assert.Equal("completed", (await check.KitchenTickets.SingleAsync(x => x.Id == "ticket-1")).Status);
                Assert.Equal("completed",
                    (await check.KitchenTicketItems.SingleAsync(x => x.Id == "ticket-item-1")).Status);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static LocalOrder NewOrder(string id, string tableId, string status) => new()
    {
        Id = id, ClientOrderId = "CLIENT-" + id, DiningTableId = tableId, BranchId = "branch-1",
        ServiceType = "dine_in", WaiterId = 1, WaiterPublicId = "u1", WaiterName = "Tester",
        Status = status, GuestCount = 2, Total = 600m,
        CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow,
    };

    private static async Task SeedDiningRoomAsync(LocalDatabaseFactory factory)
    {
        await using var seed = factory.Create();
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
                Capacity = 4, Status = "available", IsActive = true,
            },
            new LocalDiningTable
            {
                Id = "t-2", DiningAreaId = "area-1", Code = "T2", Name = "Table 2",
                Capacity = 4, Status = "occupied", IsActive = true,
            });
        await seed.SaveChangesAsync();
    }
}
