using Xunit;

namespace BusinessOS.Restaurant.Tests;

/// <summary>
/// Source guards for the redesigned dashboard: the subscription remaining-time
/// section reads the local signed lease (offline-first, tablets included), and
/// the hero band carries the live LAN status pills that used to be spread
/// across the layout.
/// </summary>
public sealed class DashboardSubscriptionTests
{
    [Fact]
    public void Dashboard_renders_subscription_remaining_time_from_the_local_lease()
    {
        var pages = Source("BusinessOS.Restaurant.Desktop", "RestaurantOperationalPages.cs");
        Assert.Contains("DashboardSubscriptionSection", pages);
        Assert.Contains("BuildSubscriptionPanel(", pages);
        Assert.Contains("SubscriptionEndsAt", pages);
        Assert.Contains("days left", pages);
        Assert.Contains("WindowsActivationStore", pages);
        // The section must not call the licensing HTTP coordinator on the
        // dashboard path: offline restaurants and tablets still render it.
        var dashboard = pages.Split("private static async Task<FrameworkElement> DashboardAsync")[1]
            .Split("private static async Task<FrameworkElement> TablesAsync")[0];
        Assert.DoesNotContain("RestaurantLicenseCoordinator", dashboard);
    }

    [Fact]
    public void Dashboard_hero_shows_live_lan_and_subscription_pills()
    {
        var pages = Source("BusinessOS.Restaurant.Desktop", "RestaurantOperationalPages.cs");
        Assert.Contains("DashboardHero(", pages);
        Assert.Contains("HeroPill(", pages);
        Assert.Contains("Tablets & LAN", pages);
        // Subscription time is also visible at a glance in the hero band.
        Assert.Contains("days left", pages);
        Assert.Contains("BuildOperationsOverview(", pages);
    }

    private static string Source(params string[] relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;

        return File.ReadAllText(Path.Combine(
            dir?.FullName ?? throw new DirectoryNotFoundException(),
            "desktop", "src", Path.Combine(relativePath)));
    }
}
