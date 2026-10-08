using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

/// <summary>
/// Reproduces the shared settings dependency used by Kitchen/KOT and Settings
/// without requiring a Windows shell, a printer, or a LAN terminal.
/// </summary>
public sealed class WorkspaceSettingsLoadTests
{
    [Fact]
    public async Task Empty_local_database_loads_workflow_defaults_for_both_workspaces()
    {
        var root = CreateRoot();
        try
        {
            var factory = new LocalDatabaseFactory(root);
            var service = new LocalRestaurantSettingsService(factory);
            var first = await service.GetAsync();
            var second = await service.GetAsync();

            Assert.Equal(RestaurantWorkflowSettings.Defaults, first);
            Assert.Equal(first, second);
            Assert.True(first.KitchenQueueEnabled);
            Assert.True(first.PreparingStageEnabled);
            Assert.Equal("block", first.NegativeStockPolicy);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Settings_read_survives_reopening_existing_local_database()
    {
        var root = CreateRoot();
        try
        {
            var factory = new LocalDatabaseFactory(root);
            await factory.EnsureCreatedAsync();
            var first = await new LocalRestaurantSettingsService(factory).GetAsync();
            var reopenedFactory = new LocalDatabaseFactory(root);
            var second = await new LocalRestaurantSettingsService(reopenedFactory).GetAsync();
            Assert.Equal(first, second);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "BusinessOS.Restaurant.Tests",
            "workspace-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
