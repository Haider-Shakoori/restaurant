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
    public async Task<LocalReportSummary> SummaryAsync(
        string branchId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken token = default)
    {
        await using var db = databaseFactory.Create();
        var (start, end) = Period(fromDate, toDate);

        var branchBills = await db.Bills.AsNoTracking()
            .Where(x => x.BranchId == branchId && x.Status != "void")
            .ToListAsync(token);
        var bills = branchBills
            .Where(x => x.IssuedAt >= start && x.IssuedAt < end)
            .ToArray();
        var billIds = bills.Select(x => x.Id).ToArray();

        var paymentRows = billIds.Length == 0
            ? []
            : await db.Payments.AsNoTracking()
                .Where(x => billIds.Contains(x.BillId) && x.Status != "void")
                .ToListAsync(token);
        var payments = paymentRows
            .Where(x => x.ReceivedAt >= start && x.ReceivedAt < end)
            .Sum(x => x.Amount);

        var stockRows = await db.StockMovements.AsNoTracking()
            .Where(x => x.BranchId == branchId && x.MovementType == "consumption" && x.UnitCost != null)
            .ToListAsync(token);
        var cogs = stockRows
            .Where(x => x.OccurredAt >= start && x.OccurredAt < end)
            .Sum(x => -x.QuantityDelta * x.UnitCost!.Value);

        var inventoryValue = await db.InventoryValuations.AsNoTracking()
            .Where(x => x.BranchId == branchId)
            .SumAsync(x => (decimal?)x.Value, token) ?? 0m;

        var branchClosings = await db.DailyClosings.AsNoTracking()
            .Where(x => x.BranchId == branchId)
            .ToListAsync(token);
        var closingIds = branchClosings
            .Where(x => x.BusinessDate >= fromDate && x.BusinessDate <= toDate)
            .Select(x => x.Id)
            .ToArray();
        var snapshots = closingIds.Length == 0
            ? []
            : await db.DailyClosingSnapshots.AsNoTracking()
                .Where(x => closingIds.Contains(x.DailyClosingId))
                .ToListAsync(token);
        var variance = snapshots
            .GroupBy(x => x.DailyClosingId)
            .Select(g => g.OrderByDescending(x => x.Version).First().CashVariance)
            .Sum();

        var gross = bills.Sum(x => x.Subtotal);
        var discounts = bills.Sum(x => x.DiscountAmount);
        var net = bills.Sum(x => x.Total);

        return new(
            branchId,
            fromDate,
            toDate,
            bills.Length,
            gross,
            discounts,
            net,
            payments,
            cogs,
            net - cogs,
            inventoryValue,
            variance);
    }

    public async Task<IReadOnlyList<LocalPaymentBreakdown>> PaymentsAsync(
        string branchId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken token = default)
    {
        await using var db = databaseFactory.Create();
        var (start, end) = Period(fromDate, toDate);

        var billIds = await db.Bills.AsNoTracking()
            .Where(x => x.BranchId == branchId && x.Status != "void")
            .Select(x => x.Id)
            .ToArrayAsync(token);
        if (billIds.Length == 0)
        {
            return [];
        }

        var rows = await db.Payments.AsNoTracking()
            .Where(x => billIds.Contains(x.BillId) && x.Status != "void")
            .ToListAsync(token);

        return rows
            .Where(x => x.ReceivedAt >= start && x.ReceivedAt < end)
            .GroupBy(x => x.Method)
            .Select(g => new LocalPaymentBreakdown(g.Key, g.Count(), g.Sum(x => x.Amount)))
            .OrderByDescending(x => x.Amount)
            .ToArray();
    }

    public async Task<IReadOnlyList<LocalTopItem>> TopItemsAsync(
        string branchId,
        DateOnly fromDate,
        DateOnly toDate,
        int take = 10,
        CancellationToken token = default)
    {
        await using var db = databaseFactory.Create();
        var (start, end) = Period(fromDate, toDate);

        var branchBills = await db.Bills.AsNoTracking()
            .Where(x => x.BranchId == branchId && x.Status != "void")
            .ToListAsync(token);
        var billIds = branchBills
            .Where(x => x.IssuedAt >= start && x.IssuedAt < end)
            .Select(x => x.Id)
            .ToArray();
        if (billIds.Length == 0)
        {
            return [];
        }

        var lines = await db.BillLines.AsNoTracking()
            .Where(x => billIds.Contains(x.BillId))
            .ToListAsync(token);

        return lines
            .GroupBy(x => x.ItemName)
            .Select(g => new LocalTopItem(g.Key, g.Sum(x => x.Quantity), g.Sum(x => x.LineTotal)))
            .OrderByDescending(x => x.Sales)
            .Take(Math.Clamp(take, 1, 100))
            .ToArray();
    }

    public async Task<IReadOnlyList<LocalClosingReport>> ClosingsAsync(
        string branchId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken token = default)
    {
        await using var db = databaseFactory.Create();

        var branchClosings = await db.DailyClosings.AsNoTracking()
            .Where(x => x.BranchId == branchId)
            .ToListAsync(token);
        var closings = branchClosings
            .Where(x => x.BusinessDate >= fromDate && x.BusinessDate <= toDate)
            .ToArray();
        if (closings.Length == 0)
        {
            return [];
        }

        var closingIds = closings.Select(x => x.Id).ToArray();
        var snapshots = await db.DailyClosingSnapshots.AsNoTracking()
            .Where(x => closingIds.Contains(x.DailyClosingId))
            .ToListAsync(token);
        var closingDates = closings.ToDictionary(x => x.Id, x => x.BusinessDate);

        return snapshots
            .GroupBy(x => x.DailyClosingId)
            .Select(g => g.OrderByDescending(x => x.Version).First())
            .Select(x => new LocalClosingReport(
                closingDates[x.DailyClosingId],
                x.Version,
                x.NetSales,
                x.PaymentsTotal,
                x.CashVariance))
            .OrderByDescending(x => x.BusinessDate)
            .ToArray();
    }

    private static (DateTimeOffset Start, DateTimeOffset End) Period(DateOnly fromDate, DateOnly toDate) =>
        (
            new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
        );
}
