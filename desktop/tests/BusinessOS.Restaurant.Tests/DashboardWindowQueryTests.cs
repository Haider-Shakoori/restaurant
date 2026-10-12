using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

/// <summary>
/// Regression guard for the dashboard/inventory business-day window. The
/// SQLite provider cannot translate DateTimeOffset ordering comparisons in
/// LINQ, so the bounded reads live in the shared SQL helpers and must execute
/// against a real database while still reaching historical rows for reports.
/// </summary>
public sealed class DashboardWindowQueryTests
{
    [Fact]
    public async Task Window_helpers_filter_bills_payments_and_movements_in_the_database()
    {
        var root = Directory.CreateTempSubdirectory("bos-window-").FullName;
        try
        {
            var factory = new LocalDatabaseFactory(root);
            await factory.EnsureCreatedAsync();
            await using var db = factory.Create();
            var now = DateTimeOffset.UtcNow;

            db.Bills.AddRange(
                Bill("bill-recent", now.AddHours(-2), 100m),
                Bill("bill-old", now.AddDays(-30), 5000m));
            db.Payments.AddRange(
                Payment("pay-recent", now.AddHours(-1), "posted", 100m),
                Payment("pay-pending", now.AddHours(-1), "pending", 700m),
                Payment("pay-old", now.AddDays(-30), "posted", 3000m));
            db.StockMovements.AddRange(
                Movement("mv-consumption-recent", "consumption", now.AddHours(-3)),
                Movement("mv-adjustment-recent", "adjustment", now.AddHours(-3)),
                Movement("mv-consumption-old", "consumption", now.AddDays(-400)));
            await db.SaveChangesAsync();

            var since = now.ToUniversalTime().AddHours(-48);
            var bills = await OperationalWindowQueries.BillsSince(db, since)
                .AsNoTracking()
                .Select(x => new { x.Id, x.IssuedAt, x.Total })
                .ToListAsync();
            var payments = await OperationalWindowQueries.PostedPaymentsSince(db, since)
                .AsNoTracking()
                .Select(x => new { x.Id, x.ReceivedAt, x.Amount })
                .ToListAsync();
            var movements = await OperationalWindowQueries.ConsumptionMovementsSince(db, since)
                .AsNoTracking()
                .Select(x => new { x.Id, x.SourceType, x.QuantityDelta, x.OccurredAt })
                .ToListAsync();

            Assert.Equal(["bill-recent"], bills.Select(x => x.Id));
            Assert.Equal(100m, bills.Single().Total);
            Assert.Equal(["pay-recent"], payments.Select(x => x.Id));
            Assert.Equal(["mv-consumption-recent"], movements.Select(x => x.Id));
            Assert.Equal("manual_stock_count", movements.Single().SourceType);

            // Historical report windows still reach older rows.
            var historical = await OperationalWindowQueries.BillsSince(db, now.AddDays(-60).ToUniversalTime())
                .AsNoTracking()
                .ToListAsync();
            Assert.Equal(2, historical.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DateTimeOffset_ordering_must_run_after_materialization()
    {
        var root = Directory.CreateTempSubdirectory("bos-order-").FullName;
        try
        {
            var factory = new LocalDatabaseFactory(root);
            await factory.EnsureCreatedAsync();
            await using var db = factory.Create();
            var now = DateTimeOffset.UtcNow;
            db.Bills.AddRange(
                Bill("bill-a", now.AddHours(-3), 10m),
                Bill("bill-b", now.AddHours(-1), 20m));
            await db.SaveChangesAsync();

            // SQLite cannot order by DateTimeOffset server-side, so the table
            // order modal materializes first and sorts in memory: newest wins.
            var rows = await db.Bills.AsNoTracking()
                .Select(x => new { x.Id, x.IssuedAt })
                .ToListAsync();
            var newest = rows.OrderByDescending(x => x.IssuedAt).Select(x => x.Id).FirstOrDefault();

            Assert.Equal("bill-b", newest);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static LocalBill Bill(string id, DateTimeOffset issuedAt, decimal total) => new()
    {
        Id = id,
        OrderId = "order-" + id,
        BranchId = "branch-1",
        CreatedByUserId = 1,
        BillNumber = "B-" + id,
        Status = "open",
        Total = total,
        IssuedAt = issuedAt,
    };

    private static LocalTenantPayment Payment(string id, DateTimeOffset receivedAt, string status, decimal amount) => new()
    {
        Id = id,
        BillId = "bill-" + id,
        CashierSessionId = "session-1",
        ReceivedByUserId = 1,
        Method = "cash",
        Amount = amount,
        Status = status,
        ReceivedAt = receivedAt,
    };

    private static LocalStockMovement Movement(string id, string movementType, DateTimeOffset occurredAt) => new()
    {
        Id = id,
        BranchId = "branch-1",
        InventoryItemId = "item-1",
        ActorUserId = 1,
        MovementType = movementType,
        QuantityDelta = -1m,
        SourceType = "manual_stock_count",
        SourceId = id,
        IdempotencyKey = "key-" + id,
        OccurredAt = occurredAt,
    };
}
