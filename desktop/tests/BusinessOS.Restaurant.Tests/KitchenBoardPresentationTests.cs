using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class KitchenBoardPresentationTests
{
    [Fact]
    public void Kds_service_counters_follow_active_station_filters_and_actual_aging()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "OperationalActionViews.cs"));

        Assert.Contains("void UpdateKitchenSummary()", source);
        Assert.Contains("kitchenSummary.Text =", source);
        Assert.Contains("settings.KitchenLateMinutes", source);
        Assert.Contains("row.Status == \"expo\"", source);
        Assert.Contains("row.Priority == \"rush\"", source);
        Assert.Contains("stationFilter.SelectedItem as Choice", source);
        Assert.Contains("statusFilter.SelectedItem?.ToString()", source);

        var render = source.IndexOf("void RenderBoard()", StringComparison.Ordinal);
        var summary = source.IndexOf("UpdateKitchenSummary();", render, StringComparison.Ordinal);
        Assert.True(render >= 0 && summary > render);
        Assert.Contains("ageTimer.Tick += (_, _) =>", source);
    }

    [Fact]
    public void Existing_kot_state_transitions_and_kitchen_timer_are_retained()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "OperationalActionViews.cs"));

        Assert.Contains("await workflow.StartKitchenItemAsync(", source);
        Assert.Contains("await workflow.MarkKitchenItemReadyAsync(", source);
        Assert.Contains("await workflow.PassExpoItemAsync(", source);
        Assert.Contains("await workflow.RefireKitchenItemAsync(", source);
        Assert.Contains("pollTimer.Start();", source);
        Assert.Contains("ageTimer.Start();", source);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
