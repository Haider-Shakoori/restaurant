using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.LocalServer;

public sealed record LocalReportSummary(
    string BranchId, DateOnly From, DateOnly To, int BillCount, decimal GrossSales,
    decimal Discounts, decimal NetSales, decimal Payments, decimal Outstanding,
    decimal CostOfGoods, decimal GrossProfit, decimal GrossMarginPercent,
    decimal InventoryValue, decimal CashVariance);

public sealed record LocalPaymentBreakdown(string Method, int Count, decimal Amount);
public sealed record LocalTopItem(string ItemName, int Quantity, decimal Sales);
public sealed record LocalClosingReport(DateOnly BusinessDate, string Status, int Version, decimal GrossSales, decimal Discounts, decimal NetSales, decimal Payments, decimal CashVariance);
public sealed record LocalSalesDay(DateOnly BusinessDate, int BillCount, decimal GrossSales, decimal Discounts, decimal NetSales, decimal Payments, decimal CostOfGoods, decimal GrossProfit);
public sealed record LocalAccountingLine(string Code, string Account, string Category, decimal Debit, decimal Credit);
public sealed record LocalInventoryReportLine(string InventoryItemId, string Sku, string ItemName, string Unit, decimal Quantity, decimal AverageUnitCost, decimal Value, decimal ReorderLevel, bool IsLowStock);

public sealed class LocalReportingService(LocalDatabaseFactory databaseFactory)
{
    public async Task<LocalReportSummary> SummaryAsync(string branchId, DateOnly fromDate, DateOnly toDate, CancellationToken token = default)
    {
        ValidatePeriod(fromDate, toDate);
        await using var db = databaseFactory.Create();
        var (start, end) = Period(fromDate, toDate);
        var branchBills = await db.Bills.AsNoTracking().Where(x => x.BranchId == branchId && x.Status != "void").ToListAsync(token);
        var bills = branchBills.Where(x => x.IssuedAt >= start && x.IssuedAt < end).ToArray();
        var billIds = bills.Select(x => x.Id).ToArray();
        var paymentRows = billIds.Length == 0 ? [] : await db.Payments.AsNoTracking().Where(x => billIds.Contains(x.BillId) && x.Status != "void").ToListAsync(token);
        var payments = paymentRows.Where(x => x.ReceivedAt >= start && x.ReceivedAt < end).Sum(x => x.Amount);
        var stockRows = await db.StockMovements.AsNoTracking().Where(x => x.BranchId == branchId && x.MovementType == "consumption" && x.UnitCost != null).ToListAsync(token);
        var cogs = stockRows.Where(x => x.OccurredAt >= start && x.OccurredAt < end).Sum(x => -x.QuantityDelta * x.UnitCost!.Value);
        var inventoryValue = await db.InventoryValuations.AsNoTracking().Where(x => x.BranchId == branchId).SumAsync(x => (decimal?)x.Value, token) ?? 0m;
        var branchClosings = await db.DailyClosings.AsNoTracking().Where(x => x.BranchId == branchId).ToListAsync(token);
        var closingIds = branchClosings.Where(x => x.BusinessDate >= fromDate && x.BusinessDate <= toDate).Select(x => x.Id).ToArray();
        var snapshots = closingIds.Length == 0 ? [] : await db.DailyClosingSnapshots.AsNoTracking().Where(x => closingIds.Contains(x.DailyClosingId)).ToListAsync(token);
        var variance = snapshots.GroupBy(x => x.DailyClosingId).Select(g => g.OrderByDescending(x => x.Version).First().CashVariance).Sum();
        var gross = bills.Sum(x => x.Subtotal);
        var discounts = bills.Sum(x => x.DiscountAmount);
        var net = bills.Sum(x => x.Total);
        var profit = net - cogs;
        return new(branchId, fromDate, toDate, bills.Length, gross, discounts, net, payments, Math.Max(0m, net - payments), cogs, profit, net == 0m ? 0m : decimal.Round(profit / net * 100m, 2), inventoryValue, variance);
    }

    public async Task<IReadOnlyList<LocalPaymentBreakdown>> PaymentsAsync(string branchId, DateOnly fromDate, DateOnly toDate, CancellationToken token = default)
    {
        ValidatePeriod(fromDate, toDate);
        await using var db = databaseFactory.Create();
        var (start, end) = Period(fromDate, toDate);
        var billIds = await db.Bills.AsNoTracking().Where(x => x.BranchId == branchId && x.Status != "void").Select(x => x.Id).ToArrayAsync(token);
        if (billIds.Length == 0) return [];
        var rows = await db.Payments.AsNoTracking().Where(x => billIds.Contains(x.BillId) && x.Status != "void").ToListAsync(token);
        return rows.Where(x => x.ReceivedAt >= start && x.ReceivedAt < end).GroupBy(x => x.Method)
            .Select(g => new LocalPaymentBreakdown(g.Key, g.Count(), g.Sum(x => x.Amount))).OrderByDescending(x => x.Amount).ToArray();
    }

    public async Task<IReadOnlyList<LocalTopItem>> TopItemsAsync(string branchId, DateOnly fromDate, DateOnly toDate, int take = 10, CancellationToken token = default)
    {
        ValidatePeriod(fromDate, toDate);
        await using var db = databaseFactory.Create();
        var (start, end) = Period(fromDate, toDate);
        var branchBills = await db.Bills.AsNoTracking().Where(x => x.BranchId == branchId && x.Status != "void").ToListAsync(token);
        var billIds = branchBills.Where(x => x.IssuedAt >= start && x.IssuedAt < end).Select(x => x.Id).ToArray();
        if (billIds.Length == 0) return [];
        var lines = await db.BillLines.AsNoTracking().Where(x => billIds.Contains(x.BillId)).ToListAsync(token);
        return lines.GroupBy(x => x.ItemName).Select(g => new LocalTopItem(g.Key, g.Sum(x => x.Quantity), g.Sum(x => x.LineTotal)))
            .OrderByDescending(x => x.Sales).Take(Math.Clamp(take, 1, 100)).ToArray();
    }

    public async Task<IReadOnlyList<LocalClosingReport>> ClosingsAsync(string branchId, DateOnly fromDate, DateOnly toDate, CancellationToken token = default)
    {
        ValidatePeriod(fromDate, toDate);
        await using var db = databaseFactory.Create();
        var branchClosings = await db.DailyClosings.AsNoTracking().Where(x => x.BranchId == branchId).ToListAsync(token);
        var closings = branchClosings.Where(x => x.BusinessDate >= fromDate && x.BusinessDate <= toDate).ToArray();
        if (closings.Length == 0) return [];
        var ids = closings.Select(x => x.Id).ToArray();
        var snapshots = await db.DailyClosingSnapshots.AsNoTracking().Where(x => ids.Contains(x.DailyClosingId)).ToListAsync(token);
        var lookup = closings.ToDictionary(x => x.Id);
        return snapshots.GroupBy(x => x.DailyClosingId).Select(g => g.OrderByDescending(x => x.Version).First())
            .Select(x => new LocalClosingReport(lookup[x.DailyClosingId].BusinessDate, lookup[x.DailyClosingId].Status, x.Version, x.GrossSales, x.Discounts, x.NetSales, x.PaymentsTotal, x.CashVariance))
            .OrderByDescending(x => x.BusinessDate).ToArray();
    }

    public async Task<IReadOnlyList<LocalSalesDay>> SalesTrendAsync(string branchId, DateOnly fromDate, DateOnly toDate, CancellationToken token = default)
    {
        ValidatePeriod(fromDate, toDate);
        await using var db = databaseFactory.Create();
        var (start, end) = Period(fromDate, toDate);
        var bills = (await db.Bills.AsNoTracking().Where(x => x.BranchId == branchId && x.Status != "void").ToListAsync(token))
            .Where(x => x.IssuedAt >= start && x.IssuedAt < end).ToArray();
        var billIds = bills.Select(x => x.Id).ToArray();
        var payments = billIds.Length == 0 ? [] : await db.Payments.AsNoTracking().Where(x => billIds.Contains(x.BillId) && x.Status != "void").ToListAsync(token);
        var movements = await db.StockMovements.AsNoTracking().Where(x => x.BranchId == branchId && x.MovementType == "consumption" && x.UnitCost != null).ToListAsync(token);
        return Enumerable.Range(0, toDate.DayNumber - fromDate.DayNumber + 1).Select(offset =>
        {
            var day = fromDate.AddDays(offset); var (ds, de) = Period(day, day);
            var dayBills = bills.Where(x => x.IssuedAt >= ds && x.IssuedAt < de).ToArray();
            var ids = dayBills.Select(x => x.Id).ToHashSet();
            var paid = payments.Where(x => ids.Contains(x.BillId) && x.ReceivedAt >= ds && x.ReceivedAt < de).Sum(x => x.Amount);
            var cogs = movements.Where(x => x.OccurredAt >= ds && x.OccurredAt < de).Sum(x => -x.QuantityDelta * x.UnitCost!.Value);
            var net = dayBills.Sum(x => x.Total);
            return new LocalSalesDay(day, dayBills.Length, dayBills.Sum(x => x.Subtotal), dayBills.Sum(x => x.DiscountAmount), net, paid, cogs, net - cogs);
        }).ToArray();
    }

    public async Task<IReadOnlyList<LocalAccountingLine>> AccountingSummaryAsync(string branchId, DateOnly fromDate, DateOnly toDate, CancellationToken token = default)
    {
        var s = await SummaryAsync(branchId, fromDate, toDate, token);
        return [
            new("1000", "Cash / payment clearing", "Asset", s.Payments, 0m),
            new("1100", "Customer receivables", "Asset", s.Outstanding, 0m),
            new("4000", "Restaurant sales", "Revenue", 0m, s.GrossSales),
            new("4050", "Sales discounts", "Contra revenue", s.Discounts, 0m),
            new("5000", "Cost of goods sold", "Expense", s.CostOfGoods, 0m),
            new("1200", "Inventory on hand", "Asset snapshot", s.InventoryValue, 0m),
        ];
    }

    public async Task<IReadOnlyList<LocalInventoryReportLine>> InventoryAsync(string branchId, CancellationToken token = default)
    {
        await using var db = databaseFactory.Create();
        var items = await db.InventoryItems.AsNoTracking().ToListAsync(token);
        var valuations = await db.InventoryValuations.AsNoTracking().Where(x => x.BranchId == branchId).ToListAsync(token);
        var byItem = valuations.ToDictionary(x => x.InventoryItemId);
        return items.Where(x => x.IsActive).Select(item =>
        {
            byItem.TryGetValue(item.Id, out var value);
            var quantity = value?.Quantity ?? 0m;
            return new LocalInventoryReportLine(item.Id, item.Sku, item.Name, item.BaseUnit, quantity, value?.AverageUnitCost ?? 0m, value?.Value ?? 0m, item.ReorderLevel, quantity <= item.ReorderLevel);
        }).OrderByDescending(x => x.IsLowStock).ThenBy(x => x.ItemName).ToArray();
    }

    public static void ValidatePeriod(DateOnly fromDate, DateOnly toDate)
    {
        if (toDate < fromDate) throw new ArgumentException("The report end date must be on or after the start date.");
        if (toDate.DayNumber - fromDate.DayNumber > 366) throw new ArgumentException("Local reports are limited to 367 days per request.");
    }

    private static (DateTimeOffset Start, DateTimeOffset End) Period(DateOnly fromDate, DateOnly toDate) =>
        (new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), new DateTimeOffset(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
}
