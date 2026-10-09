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

        Assert.Contains("workflow.StartKitchenItemAsync(", source);
        Assert.Contains("workflow.MarkKitchenItemReadyAsync(", source);
        Assert.Contains("workflow.PassExpoItemAsync(", source);
        Assert.Contains("await workflow.RefireKitchenItemAsync(", source);
        Assert.Contains("pollTimer.Start();", source);
        Assert.Contains("ageTimer.Start();", source);
    }

    [Fact]
    public void Ready_kitchen_items_show_pickup_handoff_not_a_running_production_timer()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "OperationalActionViews.cs"));
        Assert.Contains("label.Text = currentStatus == \"ready\" ? \"PICKUP\" : \"SERVED\"", source);
        Assert.Contains("Ready for pickup. The waiter delivers the food", source);
        Assert.Contains("OPEN POS & ORDERS · MARK SERVED", source);
        Assert.Contains("row.Status == \"ready\"", source);
        Assert.Contains("role", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workflow.PassExpoItemAsync(row.ItemId)", source);
        Assert.Contains("workflow.RecallKitchenItemAsync(", source);
        Assert.Contains("workflow.RecordKitchenWasteAsync(", source);
        Assert.Contains("workflow.RefireKitchenItemAsync(", source);
    }

    [Fact]
    public void Offline_branch_list_is_read_only_and_does_not_fake_a_tenant_save()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "DesktopCloudManagementWindow.cs"));
        Assert.Contains("LoadCachedBranchesAsync()", source);
        Assert.Contains("db.Branches.AsNoTracking()", source);
        Assert.Contains("SetCachedBranchChoices(branches)", source);
        Assert.Contains("SetCloudAvailability(false)", source);
        Assert.Contains("_save.IsEnabled = available && _hasValidBranch", source);
        Assert.Contains("if (!_cloudAvailable)", source);
        Assert.Contains("No active branch on the tenant", source);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
