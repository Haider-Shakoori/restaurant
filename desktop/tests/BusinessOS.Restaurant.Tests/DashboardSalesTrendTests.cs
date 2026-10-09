using BusinessOS.Restaurant.Application.OperationalData;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class DashboardSalesTrendTests
{
    private static readonly TimeZoneInfo Kabul = TimeZoneInfo.CreateCustomTimeZone(
        "Restaurant Kabul UTC+04:30", TimeSpan.FromMinutes(270), "Kabul", "Kabul");

    [Fact]
    public void Aggregates_bills_by_restaurant_local_day_and_hour_instead_of_utc_day()
    {
        var now = new DateTimeOffset(2026, 10, 8, 2, 0, 0, TimeSpan.Zero); // 06:30 Kabul
        var sales = DashboardSalesTrend.Aggregate(now, new[]
        {
            (new DateTimeOffset(2026, 10, 7, 19, 0, 0, TimeSpan.Zero), 100m), // prior day 23:30
            (new DateTimeOffset(2026, 10, 7, 20, 15, 0, TimeSpan.Zero), 250m), // 00:45 today
            (new DateTimeOffset(2026, 10, 8, 0, 30, 0, TimeSpan.Zero), 50m), // 05:00 today
            (new DateTimeOffset(2026, 10, 8, 3, 0, 0, TimeSpan.Zero), 999m), // future
        }, Kabul);

        Assert.Equal(new DateOnly(2026, 10, 8), sales.BusinessDay);
        Assert.Equal(300m, sales.Total);
        Assert.Equal(24, sales.HourlyTotals.Count);
        Assert.Equal(250m, sales.HourlyTotals[0]);
        Assert.Equal(50m, sales.HourlyTotals[5]);
        Assert.Equal(0m, sales.HourlyTotals[7]);
    }

    [Fact]
    public void Empty_day_produces_zero_total_and_no_fabricated_trend()
    {
        var sales = DashboardSalesTrend.Aggregate(
            new DateTimeOffset(2026, 10, 8, 8, 0, 0, TimeSpan.Zero),
            Array.Empty<(DateTimeOffset IssuedAt, decimal Total)>(), Kabul);

        Assert.Equal(0m, sales.Total);
        Assert.All(sales.HourlyTotals, hourly => Assert.Equal(0m, hourly));
    }

    [Fact]
    public void Converts_different_receipt_offsets_into_same_business_hour()
    {
        var asOf = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var sales = DashboardSalesTrend.Aggregate(asOf, new[]
        {
            (new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.FromHours(2)), 20m),
            (new DateTimeOffset(2026, 10, 8, 8, 0, 0, TimeSpan.Zero), 35m),
        }, Kabul);

        Assert.Equal(55m, sales.HourlyTotals[12]); // 08:00 UTC is 12:30 in Kabul
    }
}
