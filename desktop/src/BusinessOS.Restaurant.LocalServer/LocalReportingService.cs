using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.LocalServer;

public sealed record LocalReportSummary(
    string BranchId, DateOnly From, DateOnly To, int BillCount, decimal GrossSales,
    decimal Discounts, decimal NetSales, decimal Payments, decimal CostOfGoods,
    decimal GrossProfit, decimal InventoryValue, decimal CashVariance);

public sealed record LocalPaymentBreakdown(string Method, int Count, decimal Amount);
public sealed record LocalTopItem(string ItemName, int Quantity, decimal Sales);
public sealed record LocalClosingReport(DateOnly BusinessDate, int Version, decimal NetSales, decimal Payments, decimal CashVariance);

public sealed class LocalReportingService(LocalDatabaseFactory databaseFactory)
{
    public async Task<LocalReportSummary> SummaryAsync(string branchId, DateOnly fromDate, DateOnly toDate, CancellationToken token = default)
    {
        await using var db = databaseFactory.CreateDbContext();
        var start = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var bills = await db.Bills.AsNoTracking()
            .Where(x => x.BranchId == branchId && x.IssuedAt >= start && x.IssuedAt < end && x.Status != "void")
            .ToListAsync(token);
        var billIds = bills.Select(x => x.Id).ToArray();
        var payments = await db.Payments.AsNoTracking()
            .Where(x => billIds.Contains(x.BillId) && x.Status != "void")
            .SumAsync(x => (decimal?)x.Amount, token) ?? 0m;
        var cogs = await db.StockMovements.AsNoTracking()
            .Where(x => x.BranchId == branchId && x.OccurredAt >= start && x.OccurredAt < end &&
                        x.MovementType == "consumption" && x.UnitCost != null)
            .SumAsync(x => (decimal?)(-x.QuantityDelta * x.UnitCost!.Value), token) ?? 0m;
        var inventoryValue = await db.InventoryValuations.AsNoTracking()
            .Where(x => x.BranchId == branchId).SumAsync(x => (decimal?)x.Value, token) ?? 0m;
        var closingIds = await db.DailyClosings.AsNoTracking()
            .Where(x => x.BranchId == branchId && x.BusinessDate >= fromDate && x.BusinessDate <= toDate)
            .Select(x => x.Id).ToArrayAsync(token);
        var variance = await db.DailyClosingSnapshots.AsNoTracking()
            .Where(x => closingIds.Contains(x.DailyClosingId))
            .GroupBy(x => x.DailyClosingId).Select(g => g.OrderByDescending(x => x.Version).First().CashVariance)
            .SumAsync(token);

        var gross = bills.Sum(x => x.Subtotal);
        var discounts = bills.Sum(x => x.DiscountAmount);
        var net = bills.Sum(x => x.Total);
        return new(branchId, fromDate, toDate, bills.Count, gross, discounts, net, payments, cogs, net - cogs, inventoryValue, variance);
    }

    public async Task<IReadOnlyList<LocalPaymentBreakdown>> PaymentsAsync(string branchId, DateOnly fromDate, DateOnly toDate, CancellationToken token = default)
    {
        await using var db = databaseFactory.CreateDbContext();
        var start = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return await (from payment in db.Payments.AsNoTracking()
                      join bill in db.Bills.AsNoTracking() on payment.BillId equals bill.Id
                      where bill.BranchId == branchId && payment.ReceivedAt >= start && payment.ReceivedAt < end && payment.Status != "void"
                      group payment by payment.Method into g
                      orderby g.Sum(x => x.Amount) descending
                      select new LocalPaymentBreakdown(g.Key, g.Count(), g.Sum(x => x.Amount))).ToListAsync(token);
    }

    public async Task<IReadOnlyList<LocalTopItem>> TopItemsAsync(string branchId, DateOnly fromDate, DateOnly toDate, int take = 10, CancellationToken token = default)
    {
        await using var db = databaseFactory.CreateDbContext();
        var start = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return await (from line in db.BillLines.AsNoTracking()
                      join bill in db.Bills.AsNoTracking() on line.BillId equals bill.Id
                      where bill.BranchId == branchId && bill.IssuedAt >= start && bill.IssuedAt < end && bill.Status != "void"
                      group line by line.ItemName into g
                      orderby g.Sum(x => x.LineTotal) descending
                      select new LocalTopItem(g.Key, g.Sum(x => x.Quantity), g.Sum(x => x.LineTotal))).Take(Math.Clamp(take, 1, 100)).ToListAsync(token);
    }

    public async Task<IReadOnlyList<LocalClosingReport>> ClosingsAsync(string branchId, DateOnly fromDate, DateOnly toDate, CancellationToken token = default)
    {
        await using var db = databaseFactory.CreateDbContext();
        var rows = await (from closing in db.DailyClosings.AsNoTracking()
                          join snapshot in db.DailyClosingSnapshots.AsNoTracking() on closing.Id equals snapshot.DailyClosingId
                          where closing.BranchId == branchId && closing.BusinessDate >= fromDate && closing.BusinessDate <= toDate
                          select new { closing.BusinessDate, snapshot.Version, snapshot.NetSales, snapshot.PaymentsTotal, snapshot.CashVariance }).ToListAsync(token);
        return rows.GroupBy(x => x.BusinessDate).Select(g => g.OrderByDescending(x => x.Version).First())
            .OrderByDescending(x => x.BusinessDate)
            .Select(x => new LocalClosingReport(x.BusinessDate, x.Version, x.NetSales, x.PaymentsTotal, x.CashVariance)).ToArray();
    }
}
