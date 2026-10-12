using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.Persistence;

/// <summary>
/// Bounded business-day reads for reporting screens.
///
/// The SQLite provider cannot translate DateTimeOffset ordering comparisons in
/// LINQ (it fails query compilation), so the window is applied in SQL with
/// provider-bound parameters. The application writes these columns from
/// <see cref="DateTimeOffset.UtcNow"/>, so stored TEXT values share one offset
/// and compare chronologically; callers still re-check the local business day
/// before counting money.
/// </summary>
public static class OperationalWindowQueries
{
    public static IQueryable<LocalBill> BillsSince(RestaurantDbContext db, DateTimeOffset sinceUtc) =>
        db.Bills.FromSqlInterpolated($"SELECT * FROM bills WHERE IssuedAt >= {sinceUtc}");

    public static IQueryable<LocalTenantPayment> PostedPaymentsSince(RestaurantDbContext db, DateTimeOffset sinceUtc) =>
        db.Payments.FromSqlInterpolated(
            $"SELECT * FROM tenant_payments WHERE Status = 'posted' AND ReceivedAt >= {sinceUtc}");

    public static IQueryable<LocalStockMovement> ConsumptionMovementsSince(RestaurantDbContext db, DateTimeOffset sinceUtc) =>
        db.StockMovements.FromSqlInterpolated(
            $"SELECT * FROM stock_movements WHERE MovementType = 'consumption' AND OccurredAt >= {sinceUtc}");
}
