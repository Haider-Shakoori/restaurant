using System.Text.Json;
using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class DesktopOfflineModeTests
{
    [Fact]
    public void Standalone_signed_license_blocks_all_cloud_sync_even_if_local_sync_flag_is_true()
    {
        var now = DateTimeOffset.UtcNow;
        using var payload = JsonDocument.Parse("{}");
        var snapshot = new LeaseSnapshot(
            1, "lease", "test-key", "tenant", "business", "subscription", 1,
            "device", "device-uid", "plan", "Restaurant",
            now, now.AddDays(60), now.AddDays(60),
            payload.RootElement.Clone(), DesktopMode: DesktopOperatingMode.StandaloneOffline);
        var signed = new SignedLease(payload.RootElement.Clone(), "signature", "Ed25519", "test-key");
        var activation = new ActivationState("https://example.test", "device", "device-uid",
            "secret", "key", "test-key", signed, snapshot, now);

        Assert.True(DesktopOperatingMode.IsStandalone(activation));
        Assert.False(DesktopOperatingMode.CloudAllowed(activation,
            new ConnectionSettings("https://example.test", SyncEnabled: true)));
        Assert.False(DesktopOperatingMode.CloudAllowed(activation,
            new ConnectionSettings("https://example.test", SyncEnabled: false)));
        Assert.False(DesktopOperatingMode.CloudAllowed(null,
            new ConnectionSettings("https://example.test")));

        var cloud = activation with
        {
            Snapshot = snapshot with { DesktopMode = DesktopOperatingMode.CloudSync },
        };
        Assert.True(DesktopOperatingMode.CloudAllowed(cloud,
            new ConnectionSettings("https://example.test", SyncEnabled: true)));
        Assert.False(DesktopOperatingMode.CloudAllowed(cloud,
            new ConnectionSettings("https://example.test", SyncEnabled: false)));
    }

    [Fact]
    public async Task Standalone_module_updates_are_persisted_only_in_local_sqlite()
    {
        var root = Path.Combine(Path.GetTempPath(), "RestaurantStandalone", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new LocalRestaurantSettingsService(new LocalDatabaseFactory(root));
            await service.ApplyStandaloneModulesAsync(
                new RestaurantModuleFlags(true, true, false, false));

            var updated = await service.GetModulesAsync();
            Assert.True(updated.InventoryEnabled);
            Assert.False(updated.PurchasingEnabled);
            Assert.False(updated.AutomaticRecipeConsumptionEnabled);

            var factory = new LocalDatabaseFactory(root);
            await using var db = factory.Create();
            var setting = await db.RestaurantSettings.FindAsync("purchasing_enabled");
            Assert.NotNull(setting);
            Assert.Equal("local", setting.Source);

            await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyStandaloneModulesAsync(
                new RestaurantModuleFlags(true, false, true, false)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
