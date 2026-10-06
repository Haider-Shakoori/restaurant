using System.Text.Json;
using BusinessOS.Restaurant.Application.OperationalData;
using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class LocalOrderFlowTests
{
    [Fact]
    public async Task Android_mutations_create_idempotent_local_order_and_submit_it()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "BusinessOS.Restaurant.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var factory = new LocalDatabaseFactory(root);
            var snapshotStore = new OperationalSnapshotStore(factory);
            await snapshotStore.ApplyAsync(CreateOperationalSnapshot());

            await using (var db = factory.Create())
            {
                db.StaffUsers.Add(new LocalStaffUser
                {
                    Id = 1,
                    PublicId = "waiter-public-1",
                    Name = "Waiter One",
                    Email = "waiter@example.test",
                    Role = "waiter",
                    IsActive = true,
                });
                await db.SaveChangesAsync();
            }

            var service = new LocalOrderService(factory, snapshotStore);
            var auth = CreateAuthContext();

            var open = Mutation(
                "mutation-open-1",
                "order.open",
                """
                {
                  "client_order_id": "client-order-1",
                  "dining_table_id": "table-1",
                  "guest_count": 2
                }
                """);

            var firstOpen = await service.PushAsync(
                auth,
                new LocalSyncPushRequest("batch-1", [open]));
            AssertStatus(firstOpen, "accepted");

            var retryOpen = await service.PushAsync(
                auth,
                new LocalSyncPushRequest("batch-1-retry", [open]));
            AssertStatus(retryOpen, "accepted");

            var addItem = Mutation(
                "mutation-line-1",
                "order.item.add",
                """
                {
                  "client_order_id": "client-order-1",
                  "client_line_id": "client-line-1",
                  "menu_item_id": "item-1",
                  "quantity": 2
                }
                """);

            var addResult = await service.PushAsync(
                auth,
                new LocalSyncPushRequest("batch-2", [addItem]));
            AssertStatus(addResult, "accepted");

            var submit = Mutation(
                "mutation-submit-1",
                "order.submit",
                """
                {
                  "client_order_id": "client-order-1"
                }
                """);

            var submitResult = await service.PushAsync(
                auth,
                new LocalSyncPushRequest("batch-3", [submit]));
            AssertStatus(submitResult, "accepted");

            await using var verify = factory.Create();
            var order = await verify.Orders.SingleAsync();
            var line = await verify.OrderItems.SingleAsync();
            var table = await verify.DiningTables.SingleAsync();

            Assert.Equal("submitted", order.Status);
            Assert.Equal(500m, order.Subtotal);
            Assert.Equal(500m, order.Total);
            Assert.False(order.CloudSynced);
            Assert.Equal(2, line.Quantity);
            Assert.Equal(500m, line.LineTotal);
            Assert.Equal("occupied", table.Status);
            Assert.Equal(3, await verify.Mutations.CountAsync());
            Assert.True(await verify.Changes.CountAsync() >= 4);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Reusing_mutation_id_with_different_payload_is_rejected()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "BusinessOS.Restaurant.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var factory = new LocalDatabaseFactory(root);
            var snapshotStore = new OperationalSnapshotStore(factory);
            await snapshotStore.ApplyAsync(CreateOperationalSnapshot());

            await using (var db = factory.Create())
            {
                db.StaffUsers.Add(new LocalStaffUser
                {
                    Id = 1,
                    PublicId = "waiter-public-1",
                    Name = "Waiter One",
                    Email = "waiter@example.test",
                    Role = "waiter",
                    IsActive = true,
                });
                await db.SaveChangesAsync();
            }

            var service = new LocalOrderService(factory, snapshotStore);
            var auth = CreateAuthContext();

            var first = Mutation(
                "same-mutation",
                "order.open",
                """
                {
                  "client_order_id": "client-order-a",
                  "dining_table_id": "table-1"
                }
                """);

            var second = Mutation(
                "same-mutation",
                "order.open",
                """
                {
                  "client_order_id": "client-order-b",
                  "dining_table_id": "table-1"
                }
                """);

            await service.PushAsync(
                auth,
                new LocalSyncPushRequest("batch-a", [first]));

            var response = await service.PushAsync(
                auth,
                new LocalSyncPushRequest("batch-b", [second]));

            var result = FirstResult(response);
            Assert.Equal("rejected", result.GetProperty("status").GetString());
            Assert.Equal("mutation_id_reused", result.GetProperty("code").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static OperationalSnapshot CreateOperationalSnapshot() =>
        new(
            1,
            DateTimeOffset.UtcNow,
            0,
            "tenant-1",
            [new BranchSnapshot("branch-1", "MAIN", "Main Branch", true)],
            [],
            [
                new MenuCategorySnapshot(
                    "category-1",
                    "Main",
                    1,
                    [
                        new MenuItemSnapshot(
                            "item-1",
                            "category-1",
                            "FOOD-001",
                            "Kabuli Pulao",
                            null,
                            250m,
                            "AFN",
                            1,
                            []),
                    ]),
            ],
            [
                new DiningTableSnapshot(
                    "table-1",
                    "T-01",
                    "Table 1",
                    4,
                    "available",
                    true,
                    new DiningAreaSummary("area-1", "Main Hall"),
                    new BranchSummary("branch-1", "Main Branch")),
            ]);

    private static LocalAuthContext CreateAuthContext()
    {
        using var payloadDocument = JsonDocument.Parse("{}");
        var payload = payloadDocument.RootElement.Clone();
        var now = DateTimeOffset.UtcNow;

        var activation = new ActivationState(
            "https://restaurant.example.test",
            "host-device-1",
            "host-uid-1",
            "host-secret",
            "public-key",
            "key-1",
            new SignedLease(payload, "signature", "Ed25519", "key-1"),
            new LeaseSnapshot(
                1,
                "lease-1",
                "key-1",
                "tenant-1",
                "business-1",
                "subscription-1",
                1,
                "host-device-1",
                "host-uid-1",
                "standard",
                "Standard",
                now.AddHours(-1),
                now.AddDays(7),
                now.AddDays(30),
                payload),
            now);

        return new LocalAuthContext(
            new LocalDevice
            {
                Id = "android-device-1",
                DeviceUid = "android-uid-1",
                DeviceName = "Waiter Phone",
                StaffUserId = 1,
                SecretHash = "00",
                IsActive = true,
                PairedAtUtc = now,
                LastSeenAtUtc = now,
            },
            new LocalStaffUser
            {
                Id = 1,
                PublicId = "waiter-public-1",
                Name = "Waiter One",
                Email = "waiter@example.test",
                Role = "waiter",
                IsActive = true,
            },
            activation);
    }

    private static LocalSyncMutation Mutation(
        string id,
        string operation,
        string json)
    {
        using var document = JsonDocument.Parse(json);
        return new LocalSyncMutation(
            id,
            operation,
            document.RootElement.Clone(),
            DateTimeOffset.UtcNow);
    }

    private static void AssertStatus(
        Dictionary<string, object?> response,
        string expected)
    {
        var result = FirstResult(response);
        Assert.Equal(expected, result.GetProperty("status").GetString());
    }

    private static JsonElement FirstResult(Dictionary<string, object?> response)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response));
        return document.RootElement
            .GetProperty("results")[0]
            .Clone();
    }
}
