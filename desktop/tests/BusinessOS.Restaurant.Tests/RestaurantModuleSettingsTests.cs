using System.Text.Json;
using BusinessOS.Restaurant.Application.OperationalData;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class RestaurantModuleSettingsTests
{
    [Fact]
    public async Task Tenant_bootstrap_module_flags_are_cached_for_offline_desktop_views()
    {
        var root = Path.Combine(Path.GetTempPath(), "RestaurantModules", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var factory = new LocalDatabaseFactory(root);
            var service = new LocalRestaurantSettingsService(factory);
            Assert.True((await service.GetModulesAsync()).InventoryEnabled);

            using var json = JsonDocument.Parse("""
                {
                  "recipes_enabled": true,
                  "inventory_enabled": false,
                  "purchasing_enabled": false,
                  "automatic_recipe_consumption_enabled": false
                }
                """);
            var flags = new Dictionary<string, JsonElement>();
            foreach (var property in json.RootElement.EnumerateObject())
                flags[property.Name] = property.Value.Clone();

            var snapshot = new OperationalSnapshot(
                1, DateTimeOffset.UtcNow, 1, "tenant-modules",
                [], [], [], [],
                RestaurantSettings: flags);

            await new OperationalSnapshotStore(factory).ApplyAsync(snapshot);
            var cached = await service.GetModulesAsync();

            Assert.True(cached.RecipesEnabled);
            Assert.False(cached.InventoryEnabled);
            Assert.False(cached.PurchasingEnabled);
            Assert.False(cached.AutomaticRecipeConsumptionEnabled);

            // No network or server call is needed to read the last known flags.
            Assert.False((await new LocalRestaurantSettingsService(factory).GetModulesAsync()).InventoryEnabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Explicit_web_module_sync_replaces_cached_values_without_losing_unrelated_kot_settings()
    {
        var root = Path.Combine(Path.GetTempPath(), "RestaurantModules", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new LocalRestaurantSettingsService(new LocalDatabaseFactory(root));
            await service.ApplyCloudModulesAsync(new RestaurantModuleFlags(false, true, false, false));
            var modules = await service.GetModulesAsync();
            var kitchen = await service.GetAsync();

            Assert.False(modules.RecipesEnabled);
            Assert.True(modules.InventoryEnabled);
            Assert.False(modules.PurchasingEnabled);
            Assert.True(kitchen.KitchenQueueEnabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
