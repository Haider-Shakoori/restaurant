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
public sealed record LocalKitchenStationPerformance(
    string StationId,
    string Station,
    int Tickets,
    int Items,
    int RushItems,
    int LateItems,
    int ActiveItems,
    double AverageQueueMinutes,
    double AveragePreparationMinutes,
    double AverageTotalMinutes,
    double UtilizationPercent);
public sealed record LocalKitchenPerformanceReport(
    string BranchId,
    DateOnly From,
    DateOnly To,
    int KotRounds,
    int Tickets,
    int Items,
    int RushItems,
    int LateItems,
    double AverageQueueMinutes,
    double AveragePreparationMinutes,
    double AverageTotalMinutes,
    IReadOnlyList<LocalKitchenStationPerformance> Stations);

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

    public async Task<LocalKitchenPerformanceReport> KitchenPerformanceAsync(
        string branchId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken token = default)
    {
        await databaseFactory.EnsureCreatedAsync(token);
        await using var db = databaseFactory.Create();
        var (start, end) = Period(fromDate, toDate);
        var settings = await LocalRestaurantSettingsService.GetAsync(db, token);

        var rounds = await db.KotRounds
            .AsNoTracking()
            .Where(x => x.BranchId == branchId)
            .ToListAsync(token);
        var periodRounds = rounds
            .Where(x => x.SentAt >= start && x.SentAt < end)
            .ToArray();
        var roundIds = periodRounds.Select(x => x.Id).ToArray();

        if (roundIds.Length == 0)
        {
            return new LocalKitchenPerformanceReport(
                branchId,
                fromDate,
                toDate,
                0,
                0,
                0,
                0,
                0,
                0d,
                0d,
                0d,
                []);
        }

        var tickets = await db.KitchenTickets
            .AsNoTracking()
            .Where(x => x.KotRoundId != null && roundIds.Contains(x.KotRoundId))
            .ToArrayAsync(token);
        var ticketIds = tickets.Select(x => x.Id).ToArray();
        var items = ticketIds.Length == 0
            ? []
            : await db.KitchenTicketItems
                .AsNoTracking()
                .Where(x => ticketIds.Contains(x.KitchenTicketId))
                .ToArrayAsync(token);

        var stationIds = tickets.Select(x => x.KitchenStationId).Distinct().ToArray();
        var stations = stationIds.Length == 0
            ? new Dictionary<string, LocalKitchenStation>(StringComparer.Ordinal)
            : await db.KitchenStations
                .AsNoTracking()
                .Where(x => stationIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, StringComparer.Ordinal, token);

        var ticketById = tickets.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;

        double QueueMinutes(LocalKitchenTicketItem item)
        {
            var ticket = ticketById[item.KitchenTicketId];
            return item.StartedAt.HasValue
                ? Math.Max(0d, (item.StartedAt.Value - ticket.QueuedAt).TotalMinutes)
                : 0d;
        }

        double PreparationMinutes(LocalKitchenTicketItem item) =>
            item.StartedAt.HasValue && item.ReadyAt.HasValue
                ? Math.Max(0d, (item.ReadyAt.Value - item.StartedAt.Value).TotalMinutes)
                : 0d;

        double TotalMinutes(LocalKitchenTicketItem item)
        {
            var ticket = ticketById[item.KitchenTicketId];
            var stop = item.ReadyAt ?? (item.Status is "voided" or "cancelled" ? item.VoidedAt : null) ?? now;
            return Math.Max(0d, (stop - ticket.QueuedAt).TotalMinutes);
        }

        bool IsActive(LocalKitchenTicketItem item) =>
            item.Status is "queued" or "active" or "preparing" or "expo";

        bool IsLate(LocalKitchenTicketItem item) =>
            IsActive(item) && TotalMinutes(item) >= settings.KitchenLateMinutes ||
            item.ReadyAt.HasValue && TotalMinutes(item) >= settings.KitchenLateMinutes;

        static double Average(IEnumerable<double> values)
        {
            var array = values.Where(value => value >= 0d).ToArray();
            return array.Length == 0 ? 0d : Math.Round(array.Average(), 2);
        }

        var stationRows = new List<LocalKitchenStationPerformance>();
        foreach (var stationGroup in tickets.GroupBy(x => x.KitchenStationId, StringComparer.Ordinal))
        {
            var groupedTicketIds = stationGroup.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            var stationItems = items
                .Where(x => groupedTicketIds.Contains(x.KitchenTicketId))
                .ToArray();

            stations.TryGetValue(stationGroup.Key, out var station);
            stationRows.Add(new LocalKitchenStationPerformance(
                stationGroup.Key,
                station?.Name ?? stationGroup.Key,
                stationGroup.Count(),
                stationItems.Length,
                stationItems.Count(x => x.Priority == "rush"),
                stationItems.Count(IsLate),
                stationItems.Count(IsActive),
                Average(stationItems.Where(x => x.StartedAt.HasValue).Select(QueueMinutes)),
                Average(stationItems.Where(x => x.StartedAt.HasValue && x.ReadyAt.HasValue).Select(PreparationMinutes)),
                Average(stationItems.Where(x => x.ReadyAt.HasValue).Select(TotalMinutes)),
                items.Length == 0
                    ? 0d
                    : Math.Round(stationItems.Length * 100d / items.Length, 2)));
        }

        var orderedStations = stationRows
            .OrderByDescending(x => x.AverageTotalMinutes)
            .ThenByDescending(x => x.ActiveItems)
            .ThenBy(x => x.Station)
            .ToArray();

        return new LocalKitchenPerformanceReport(
            branchId,
            fromDate,
            toDate,
            periodRounds.Length,
            tickets.Length,
            items.Length,
            items.Count(x => x.Priority == "rush"),
            items.Count(IsLate),
            Average(items.Where(x => x.StartedAt.HasValue).Select(QueueMinutes)),
            Average(items.Where(x => x.StartedAt.HasValue && x.ReadyAt.HasValue).Select(PreparationMinutes)),
            Average(items.Where(x => x.ReadyAt.HasValue).Select(TotalMinutes)),
            orderedStations);
    }

    private static (DateTimeOffset Start, DateTimeOffset End) Period(DateOnly fromDate, DateOnly toDate) =>
        (
            new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
        );
}
