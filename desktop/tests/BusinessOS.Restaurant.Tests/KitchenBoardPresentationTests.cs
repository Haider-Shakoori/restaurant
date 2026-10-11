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
    public void Kitchen_has_only_ready_actions_and_retains_timer_and_mobile_alerts()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "OperationalActionViews.cs"));

        Assert.DoesNotContain("workflow.StartKitchenItemAsync(", source);
        Assert.DoesNotContain("Button(\"START\")", source);
        Assert.Contains("workflow.MarkKitchenItemReadyDirectAsync(", source);
        Assert.Contains("KotTopReadyButton", source);
        Assert.Contains("KotTopActionToolbar", source);
        Assert.Contains("KotIndependentContentScroll", source);
        Assert.Contains("workflow.MarkKitchenItemReadyDirectAsync(", source);
        Assert.DoesNotContain("Button(\"RE-FIRE\")", source);
        Assert.DoesNotContain("Button(\"RECALL\")", source);
        Assert.DoesNotContain("Button(\"MARK WASTE\")", source);
        Assert.Contains("DesktopUiWork.RunBoundedAsync(primaryAction, TimeSpan.FromSeconds(25))", source);
        Assert.Contains("kitchenWritesInProgress > 0", source);
        Assert.Contains("DesktopUiWork.RunAsync(async () =>", source);
        Assert.Contains("pollTimer.Start();", source);
        Assert.Contains("ageTimer.Start();", source);
    }

    [Fact]
    public void Ready_only_kot_is_the_only_visible_kitchen_mode_with_legacy_api_compatibility()
    {
        var root = RepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "RestaurantOperationalPages.cs"));
        var service = File.ReadAllText(Path.Combine(root, "desktop", "src",
            "BusinessOS.Restaurant.LocalServer", "LocalRestaurantSettingsService.cs"));
        var kitchen = File.ReadAllText(Path.Combine(root, "desktop", "src",
            "BusinessOS.Restaurant.LocalServer", "LocalKitchenService.cs"));

        Assert.Contains("SettingsReadyOnlyKot", view);
        Assert.Contains("One-touch KOT: READY alerts the waiter (always enabled)", view);
        Assert.Contains("false, // Ready-only KOT has no START action.", view);
        Assert.Contains("false, // Expo is not part of the restaurant's workflow.", view);
        Assert.DoesNotContain("var preparing = new CheckBox", view);
        Assert.DoesNotContain("var expo = new CheckBox", view);
        Assert.Contains("public bool ReadyOnlyKotEnabled", service);
        Assert.Contains("PreparingStageEnabled: true", service);
        Assert.Contains("round.PreparingEnabled", kitchen);
        Assert.Contains("transferToExpo", kitchen);
        Assert.Contains("next_step = transferToExpo ? \"expo\" : \"waiter_pickup\"", kitchen);
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
        Assert.Contains("workflow.MarkKitchenItemReadyDirectAsync(row.ItemId, token)", source);
        Assert.Contains("workflow.MarkKitchenItemReadyDirectAsync(", source);
        Assert.Contains("workflow.MarkKitchenItemReadyDirectAsync(", source);
        Assert.Contains("workflow.MarkKitchenItemReadyDirectAsync(", source);
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
