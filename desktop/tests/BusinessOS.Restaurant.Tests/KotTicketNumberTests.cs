using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

/// <summary>
/// Regression guard: identical human KOT numbers repeat every business day,
/// so the unique internal ticket number must include the business date —
/// otherwise the second day's dispatch fails with a UNIQUE constraint error.
/// </summary>
public sealed class KotTicketNumberTests
{
    [Fact]
    public async Task Kot_ticket_numbers_stay_unique_across_business_days()
    {
        var root = Directory.CreateTempSubdirectory("bos-kot-").FullName;
        try
        {
            var factory = new LocalDatabaseFactory(root);
            await factory.EnsureCreatedAsync();
            await using var db = factory.Create();
            var now = DateTimeOffset.UtcNow;

            db.Branches.Add(new LocalBranch
            {
                Id = "branch-1", Code = "MAIN", Name = "Main Branch", IsActive = true,
            });
            var order = new LocalOrder
            {
                Id = "order-1",
                ClientOrderId = "KOT-ORDER-1",
                DiningTableId = string.Empty,
                BranchId = "branch-1",
                ServiceType = "dine_in",
                WaiterId = 1,
                WaiterPublicId = "user-1",
                WaiterName = "Tester",
                Status = "submitted",
                GuestCount = 1,
                OpenedAt = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            db.Orders.Add(order);
            var firstItem = new LocalOrderItem
            {
                Id = "item-1", OrderId = order.Id, MenuItemId = "menu-1", ClientLineId = "L1",
                ItemName = "Kabuli Pulao", UnitPrice = 400m, Quantity = 1, LineTotal = 400m,
                Status = "pending", CreatedAtUtc = now, UpdatedAtUtc = now,
            };
            var secondItem = new LocalOrderItem
            {
                Id = "item-2", OrderId = order.Id, MenuItemId = "menu-1", ClientLineId = "L2",
                ItemName = "Kabuli Pulao", UnitPrice = 400m, Quantity = 1, LineTotal = 400m,
                Status = "pending", CreatedAtUtc = now, UpdatedAtUtc = now,
            };
            db.OrderItems.AddRange(firstItem, secondItem);
            await db.SaveChangesAsync();

            var kitchen = new LocalKitchenService(factory);
            var principal = new LocalTerminalPrincipal(
                "device-1", 1, "user-1", "Tester", "owner", "tenant-1");

            var dayOne = new LocalKotRound
            {
                Id = "round-1", OrderId = order.Id, BranchId = "branch-1",
                RoundNumber = 1, DisplayNumber = 1, BusinessDate = new DateOnly(2026, 10, 11),
                KotNumber = "KOT-0001", MutationId = "m1",
                QueueEnabled = true, PreparingEnabled = true, SentAt = now,
            };
            db.KotRounds.Add(dayOne);
            await db.SaveChangesAsync();
            await kitchen.DispatchRoundAsync(db, order, dayOne, [firstItem], principal, CancellationToken.None);

            var dayTwo = new LocalKotRound
            {
                Id = "round-2", OrderId = order.Id, BranchId = "branch-1",
                RoundNumber = 2, DisplayNumber = 1, BusinessDate = new DateOnly(2026, 10, 12),
                KotNumber = "KOT-0001", MutationId = "m2",
                QueueEnabled = true, PreparingEnabled = true, SentAt = now,
            };
            db.KotRounds.Add(dayTwo);
            await db.SaveChangesAsync();
            await kitchen.DispatchRoundAsync(db, order, dayTwo, [secondItem], principal, CancellationToken.None);

            var tickets = await db.KitchenTickets.AsNoTracking().ToArrayAsync();
            Assert.Equal(2, tickets.Length);
            Assert.Equal(2, tickets.Select(x => x.TicketNumber).Distinct().Count());
            Assert.All(tickets, ticket => Assert.Equal("KOT-0001", ticket.KotNumber));
            Assert.Contains(tickets, ticket => ticket.TicketNumber.EndsWith("20261011", StringComparison.Ordinal));
            Assert.Contains(tickets, ticket => ticket.TicketNumber.EndsWith("20261012", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
