using System.Diagnostics;
using System.Text.Json;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

/// <summary>
/// The waiter app long-polls the LAN pull endpoint so a READY dish reaches the
/// phone in about a second. These guards keep the wait bounded and keep one
/// waiter from being woken by another waiter's orders.
/// </summary>
public sealed class LocalSyncLongPollTests
{
    [Fact]
    public async Task Long_poll_returns_immediately_when_a_change_already_exists()
    {
        var root = Directory.CreateTempSubdirectory("bos-longpoll-ready-").FullName;
        try
        {
            var factory = await SeedAsync(root);
            var sync = new LocalSyncService(factory, new OperationalSnapshotStore(factory));
            var principal = new LocalTerminalPrincipal("device-1", 1, "user-1", "Tester", "cashier", "tenant-1");

            await sync.PushAsync(principal, Request("LONG-POLL-1"), CancellationToken.None);

            var stopwatch = Stopwatch.StartNew();
            var pull = await sync.PullAsync(principal, 0, 100, TimeSpan.FromSeconds(5), CancellationToken.None);
            stopwatch.Stop();

            var payload = JsonSerializer.SerializeToElement(pull);
            Assert.NotEmpty(payload.GetProperty("changes").EnumerateArray());
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(3),
                $"Long poll waited for the timeout despite pending changes ({stopwatch.Elapsed}).");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Long_poll_honours_the_wait_window_when_nothing_changed()
    {
        var root = Directory.CreateTempSubdirectory("bos-longpoll-empty-").FullName;
        try
        {
            var factory = await SeedAsync(root);
            var sync = new LocalSyncService(factory, new OperationalSnapshotStore(factory));
            var principal = new LocalTerminalPrincipal("device-1", 1, "user-1", "Tester", "cashier", "tenant-1");

            var stopwatch = Stopwatch.StartNew();
            var pull = await sync.PullAsync(
                principal, 0, 100, TimeSpan.FromMilliseconds(900), CancellationToken.None);
            stopwatch.Stop();

            var payload = JsonSerializer.SerializeToElement(pull);
            Assert.Empty(payload.GetProperty("changes").EnumerateArray());
            Assert.True(
                stopwatch.Elapsed >= TimeSpan.FromMilliseconds(700),
                $"Long poll returned before its wait window ({stopwatch.Elapsed}).");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Waiter_probe_ignores_orders_owned_by_another_waiter()
    {
        var root = Directory.CreateTempSubdirectory("bos-longpoll-scope-").FullName;
        try
        {
            var factory = await SeedAsync(root);
            var sync = new LocalSyncService(factory, new OperationalSnapshotStore(factory));

            var opener = new LocalTerminalPrincipal("device-a", 1, "user-a", "Owner", "cashier", "tenant-1");
            await sync.PushAsync(opener, Request("SCOPE-1"), CancellationToken.None);

            var sameWaiter = new LocalTerminalPrincipal("device-a", 1, "user-a", "Owner", "waiter", "tenant-1");
            var otherWaiter = new LocalTerminalPrincipal("device-b", 2, "user-b", "Other", "waiter", "tenant-1");

            Assert.True(await sync.HasVisibleChangesAsync(sameWaiter, 0, CancellationToken.None));
            Assert.False(await sync.HasVisibleChangesAsync(otherWaiter, 0, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<LocalDatabaseFactory> SeedAsync(string root)
    {
        var factory = new LocalDatabaseFactory(root);
        await factory.EnsureCreatedAsync();
        await using var seed = factory.Create();
        seed.Branches.Add(new LocalBranch
        {
            Id = "branch-1", Code = "MAIN", Name = "Main Branch", IsActive = true,
        });
        await seed.SaveChangesAsync();
        return factory;
    }

    private static LocalSyncPushRequest Request(string clientOrderId) => new(
        Guid.CreateVersion7().ToString("N"),
        [
            new LocalSyncMutationRequest(
                Guid.CreateVersion7().ToString("N"),
                "order.open",
                DateTimeOffset.UtcNow,
                JsonSerializer.SerializeToElement(new
                {
                    client_order_id = clientOrderId,
                    branch_id = "branch-1",
                    service_type = "takeaway",
                    guest_count = 1,
                })),
        ]);
}
