using BusinessOS.Restaurant.Application.OperationalData;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class DashboardStockAlertsTests
{
    [Fact]
    public void Alerts_are_branch_specific_and_zero_or_negative_balances_are_first()
    {
        var alerts = DashboardStockAlerts.Find(new[]
        {
            new DashboardStockBalance("Branch A", "Rice", "kg", 12m, 10m),
            new DashboardStockBalance("Branch A", "Flour", "kg", 4m, 10m),
            new DashboardStockBalance("Branch B", "Flour", "kg", 0m, 10m),
            new DashboardStockBalance("Branch A", "Oil", "l", -1m, 5m),
        });

        Assert.Equal(3, alerts.Count);
        Assert.Equal(("Branch A", "Oil"), (alerts[0].BranchName, alerts[0].ItemName));
        Assert.Equal(("Branch B", "Flour"), (alerts[1].BranchName, alerts[1].ItemName));
        Assert.Equal(("Branch A", "Flour"), (alerts[2].BranchName, alerts[2].ItemName));
    }

    [Fact]
    public void Exact_reorder_boundary_is_an_alert_but_unconfigured_threshold_is_not()
    {
        var alerts = DashboardStockAlerts.Find(new[]
        {
            new DashboardStockBalance("Branch A", "Salt", "kg", 10m, 10m),
            new DashboardStockBalance("Branch A", "Sugar", "kg", 0m, 0m),
            new DashboardStockBalance("Branch A", "Paper", "pcs", 30m, 10m),
        });

        var single = Assert.Single(alerts);
        Assert.Equal("Salt", single.ItemName);
        Assert.Equal(10m, single.Quantity);
    }

    [Fact]
    public void Empty_inventory_and_missing_balance_do_not_generate_fake_alerts()
    {
        Assert.Empty(DashboardStockAlerts.Find(Array.Empty<DashboardStockBalance>()));
        Assert.Throws<ArgumentNullException>(() => DashboardStockAlerts.Find(null!));
    }
}
