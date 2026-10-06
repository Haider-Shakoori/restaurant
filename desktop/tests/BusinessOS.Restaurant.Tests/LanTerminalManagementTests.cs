using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class LanTerminalManagementTests
{
    [Theory]
    [InlineData(10, true, LocalTerminalStatus.Online)]
    [InlineData(60, true, LocalTerminalStatus.Stale)]
    [InlineData(240, true, LocalTerminalStatus.Offline)]
    [InlineData(10, false, LocalTerminalStatus.Disabled)]
    public void Terminal_status_uses_heartbeat_age_and_enabled_state(
        int secondsOld,
        bool enabled,
        string expected)
    {
        var now = new DateTimeOffset(2026, 10, 6, 18, 0, 0, TimeSpan.Zero);

        var status = LocalTerminalStatus.Resolve(
            enabled,
            now.AddSeconds(-secondsOld),
            now);

        Assert.Equal(expected, status);
    }

    [Fact]
    public void Network_mode_stays_local_when_cloud_has_been_unavailable()
    {
        var now = new DateTimeOffset(2026, 10, 6, 18, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            LocalNetworkMode.Healthy,
            LocalNetworkMode.Resolve(now.AddSeconds(-30), null, 0, now));

        Assert.Equal(
            LocalNetworkMode.Degraded,
            LocalNetworkMode.Resolve(now.AddMinutes(-5), "timeout", 4, now));

        Assert.Equal(
            LocalNetworkMode.IsolatedLocal,
            LocalNetworkMode.Resolve(now.AddMinutes(-15), "offline", 12, now));
    }

    [Fact]
    public async Task Manager_can_disable_and_reenable_a_paired_terminal()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var factory = new LocalDatabaseFactory(root);
            await factory.EnsureCreatedAsync();

            await using (var db = factory.Create())
            {
                db.PairedTerminals.Add(new LocalPairedTerminal
                {
                    DeviceId = "waiter-device-1",
                    TenantId = "tenant-1",
                    DeviceSecretHash = "AA",
                    AccessTokenHash = "BB",
                    UserId = 10,
                    UserPublicId = "waiter-10",
                    UserName = "Waiter Ten",
                    UserRole = "waiter",
                    ValidatedAtUtc = DateTimeOffset.UtcNow,
                    LastSeenAtUtc = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            var service = new LocalTerminalManagementService(
                factory,
                new ConnectionSettingsStore(root),
                new WindowsActivationStore(root));

            Assert.True(await service.SetEnabledAsync(
                "waiter-device-1",
                enabled: false,
                actorUserId: 1));

            var disabled = Assert.Single(await service.GetTerminalsAsync());
            Assert.False(disabled.IsEnabled);
            Assert.Equal(LocalTerminalStatus.Disabled, disabled.Status);

            Assert.True(await service.SetEnabledAsync(
                "waiter-device-1",
                enabled: true,
                actorUserId: 1));

            var enabled = Assert.Single(await service.GetTerminalsAsync());
            Assert.True(enabled.IsEnabled);
            Assert.NotEqual(LocalTerminalStatus.Disabled, enabled.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Unpair_removes_terminal_credentials_and_runtime_state()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var factory = new LocalDatabaseFactory(root);
            await factory.EnsureCreatedAsync();

            await using (var db = factory.Create())
            {
                db.PairedTerminals.Add(new LocalPairedTerminal
                {
                    DeviceId = "kitchen-device-1",
                    TenantId = "tenant-1",
                    DeviceSecretHash = "AA",
                    AccessTokenHash = "BB",
                    UserId = 20,
                    UserPublicId = "kitchen-20",
                    UserName = "Kitchen One",
                    UserRole = "kitchen",
                    ValidatedAtUtc = DateTimeOffset.UtcNow,
                    LastSeenAtUtc = DateTimeOffset.UtcNow,
                });
                db.TerminalRuntimes.Add(new LocalTerminalRuntime
                {
                    DeviceId = "kitchen-device-1",
                    DisplayName = "Kitchen Screen",
                    ClientType = "kitchen",
                    IsEnabled = true,
                    FirstSeenAtUtc = DateTimeOffset.UtcNow,
                    LastHeartbeatAtUtc = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            var service = new LocalTerminalManagementService(
                factory,
                new ConnectionSettingsStore(root),
                new WindowsActivationStore(root));

            Assert.True(await service.UnpairAsync("kitchen-device-1"));
            Assert.Empty(await service.GetTerminalsAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "BusinessOS.Restaurant.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
