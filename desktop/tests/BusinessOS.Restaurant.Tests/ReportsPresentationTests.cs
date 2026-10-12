using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class ReportsPresentationTests
{
    [Fact]
    public void Reports_page_reads_the_local_reporting_service_instead_of_the_cloud_http_model()
    {
        var pages = File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "RestaurantOperationalPages.cs"));

        // Offline-first: the page composes the same LocalReportingService that
        // the local HTTP API and ReportingTests already exercise, so it renders
        // with no internet and no LAN round-trip.
        Assert.Contains("\"reports\" => await ReportsAsync(),", pages);
        Assert.Contains("new LocalReportingService(factory)", pages);
        Assert.Contains("reports.SummaryAsync(", pages);
        Assert.Contains("reports.PaymentsAsync(", pages);
        Assert.Contains("reports.TopItemsAsync(", pages);
        Assert.Contains("reports.KitchenPerformanceAsync(", pages);
        Assert.Contains("reports.ClosingsAsync(", pages);
    }

    [Fact]
    public void Reports_page_is_a_filterable_kpi_and_breakdown_surface_without_verbose_prose()
    {
        var pages = File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "RestaurantOperationalPages.cs"));

        // Branch + period filters, quick presets and the run action drive the page.
        Assert.Contains("ReportBranchChoice", pages);
        Assert.Contains("Run report", pages);
        Assert.Contains("Last 7 days", pages);
        Assert.Contains("This month", pages);

        // KPIs and breakdown panels reuse the dashboard design language.
        Assert.Contains("BuildReportsContent", pages);
        Assert.Contains("BuildPaymentsPanel", pages);
        Assert.Contains("BuildTopItemsPanel", pages);
        Assert.Contains("BuildKitchenPanel", pages);
        Assert.Contains("BuildClosingsPanel", pages);

        // The long explanatory paragraphs were removed from the page.
        Assert.DoesNotContain("Financial and kitchen performance data stay local-first", pages);
        Assert.DoesNotContain("Stations are ordered by total preparation time", pages);
    }

    [Fact]
    public void Diagnostics_view_model_no_longer_exposes_the_removed_cloud_reports_model()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "LanDiagnosticsViewModel.cs"));
        Assert.DoesNotContain("ReportsViewModel", source);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
