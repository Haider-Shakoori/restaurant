using System.Text.Json;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

/// <summary>
/// Regression guard: staff see short daily order numbers (#0001, #0002)
/// instead of the long client order identifiers.
/// </summary>
public sealed class OrderDisplayNumberTests
{
    [Fact]
    public async Task Orders_get_short_daily_numbers_for_staff()
    {
        var root = Directory.CreateTempSubdirectory("bos-order-no-").FullName;
        try
        {
            var factory = new LocalDatabaseFactory(root);
            await factory.EnsureCreatedAsync();
            await using (var seed = factory.Create())
            {
                seed.Branches.Add(new LocalBranch
                {
                    Id = "branch-1", Code = "MAIN", Name = "Main Branch", IsActive = true,
                });
                await seed.SaveChangesAsync();
            }

            var sync = new LocalSyncService(factory, new OperationalSnapshotStore(factory));
            var principal = new LocalTerminalPrincipal("device-1", 1, "user-1", "Tester", "cashier", "tenant-1");

            static LocalSyncPushRequest Request(string clientOrderId) => new(
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

            await sync.PushAsync(principal, Request("SHORT-1"), CancellationToken.None);
            await sync.PushAsync(principal, Request("SHORT-2"), CancellationToken.None);

            await using var verify = factory.Create();
            var orders = await verify.Orders.AsNoTracking()
                .Where(x => x.ClientOrderId == "SHORT-1" || x.ClientOrderId == "SHORT-2")
                .OrderBy(x => x.DisplayNumber)
                .ToArrayAsync();

            Assert.Equal(2, orders.Length);
            Assert.Equal(1, orders[0].DisplayNumber);
            Assert.Equal(2, orders[1].DisplayNumber);
            Assert.Equal("takeaway", orders[0].ServiceType);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Pull_snapshots_expose_the_short_display_number_for_waiter_devices()
    {
        var root = Directory.CreateTempSubdirectory("bos-order-no-pull-").FullName;
        try
        {
            var factory = new LocalDatabaseFactory(root);
            await factory.EnsureCreatedAsync();
            await using (var seed = factory.Create())
            {
                seed.Branches.Add(new LocalBranch
                {
                    Id = "branch-1", Code = "MAIN", Name = "Main Branch", IsActive = true,
                });
                await seed.SaveChangesAsync();
            }

            var sync = new LocalSyncService(factory, new OperationalSnapshotStore(factory));
            var principal = new LocalTerminalPrincipal("device-1", 1, "user-1", "Tester", "cashier", "tenant-1");

            await sync.PushAsync(
                principal,
                new LocalSyncPushRequest(
                    Guid.CreateVersion7().ToString("N"),
                    [
                        new LocalSyncMutationRequest(
                            Guid.CreateVersion7().ToString("N"),
                            "order.open",
                            DateTimeOffset.UtcNow,
                            JsonSerializer.SerializeToElement(new
                            {
                                client_order_id = "PULL-1",
                                branch_id = "branch-1",
                                service_type = "takeaway",
                                guest_count = 1,
                            })),
                    ]),
                CancellationToken.None);

            var pull = await sync.PullAsync(principal, 0, 100, CancellationToken.None);
            var payload = JsonSerializer.SerializeToElement(pull);
            var snapshot = payload.GetProperty("changes").EnumerateArray()
                .Where(change => change.GetProperty("entity_type").GetString() == "order")
                .Select(change => change.GetProperty("data"))
                .Single(data => data.GetProperty("client_order_id").GetString() == "PULL-1");

            // The waiter app must render the same short #n staff number as Windows.
            Assert.Equal(1, snapshot.GetProperty("display_number").GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
