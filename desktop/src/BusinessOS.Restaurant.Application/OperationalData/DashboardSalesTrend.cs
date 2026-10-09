namespace BusinessOS.Restaurant.Application.OperationalData;

/// <summary>
/// Builds truthful local-business-day hourly billed totals for the offline desktop dashboard.
/// Use the Windows restaurant time zone instead of UTC midnight; historical receipts are
/// converted with the offset that applied when they were issued (including DST if applicable).
/// This is read-only reporting and does not alter bill, payment or inventory accounting.
/// </summary>
public static class DashboardSalesTrend
{
    public static DashboardDailySales Aggregate(
        DateTimeOffset asOf,
        IEnumerable<(DateTimeOffset IssuedAt, decimal Total)> bills,
        TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(bills);
        timeZone ??= TimeZoneInfo.Local;

        var localNow = TimeZoneInfo.ConvertTime(asOf, timeZone);
        var businessDay = DateOnly.FromDateTime(localNow.DateTime);
        var hourlyTotals = new decimal[24];

        foreach (var (issuedAt, total) in bills)
        {
            var localIssued = TimeZoneInfo.ConvertTime(issuedAt, timeZone);
            if (DateOnly.FromDateTime(localIssued.DateTime) == businessDay &&
                localIssued <= localNow)
            {
                hourlyTotals[localIssued.Hour] += total;
            }
        }

        return new DashboardDailySales(businessDay, hourlyTotals.Sum(), hourlyTotals);
    }
}

public sealed record DashboardDailySales(
    DateOnly BusinessDay,
    decimal Total,
    IReadOnlyList<decimal> HourlyTotals);
