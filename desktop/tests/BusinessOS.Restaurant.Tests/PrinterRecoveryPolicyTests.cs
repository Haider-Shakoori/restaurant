using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class PrinterRecoveryPolicyTests
{
    [Theory]
    [InlineData("KotPrintQueueProcessor.cs")]
    [InlineData("ReceiptPrintQueueProcessor.cs")]
    public void Spooler_does_not_automatically_replay_unknown_in_progress_print(string filename)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "src",
            "BusinessOS.Restaurant.Printing", filename));
        Assert.Contains("value.Status == \"pending\"", source);
        Assert.Contains("value.Status == \"failed\"", source);
        Assert.DoesNotContain("value.Status == \"printing\"", source);
        Assert.Contains("_retryNotBefore", source);
        Assert.Contains("value.Status == \"pending\" ||", source);
        Assert.Contains("job.Attempts < 10", source);
        Assert.Contains("1 << Math.Min(Math.Max(job.Attempts, 1), 8)", source);
    }

    [Fact]
    public void Manual_retry_is_manager_gated_confirmed_and_audited_without_new_kot()
    {
        var service = File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "DesktopRestaurantWorkflowService.cs"));
        Assert.Contains("RequeuePrintJobAsync(", service);
        Assert.Contains("Only an Owner or Manager can retry print jobs", service);
        Assert.Contains("previousStatus is not (\"failed\" or \"printing\")", service);
        Assert.Contains("EventType = \"print.manual_retry\"", service);
        Assert.Contains("await db.SaveChangesAsync(token)", service);

        var pages = File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "RestaurantOperationalPages.cs"));
        Assert.Contains("await PrinterQueueRecoveryPanelAsync(workflow)", pages);
        Assert.Contains("MessageBoxButton.YesNo", pages);
        Assert.Contains("await workflow.RequeuePrintJobAsync(selected.Id, selected.Receipt)", pages);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
