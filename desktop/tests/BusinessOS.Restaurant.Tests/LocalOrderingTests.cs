using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessOS.Restaurant.Application.OperationalData;
using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class LocalOrderingTests
{
    [Fact]
    public async Task Android_mutations_open_add_and_submit_against_local_database()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalogStore = new OperationalSnapshotStore(factory);
            await catalogStore.ApplyAsync(Snapshot());

            var sync = new LocalSyncService(factory, catalogStore);
            var waiter = new LocalTerminalPrincipal(
                "device-1",
                1,
                "waiter-1",
                "Waiter One",
                "waiter",
                "tenant-1");

            var open = await PushOneAsync(
                sync,
                waiter,
                "M-OPEN-1",
                "order.open",
                new
                {
                    client_order_id = "ORDER-1",
                    dining_table_id = "table-1",
                    guest_count = 2,
                });

            Assert.Equal("accepted", open.GetProperty("status").GetString());

            var add = await PushOneAsync(
                sync,
                waiter,
                "M-ADD-1",
                "order.item.add",
                new
                {
                    client_order_id = "ORDER-1",
                    client_line_id = "LINE-1",
                    menu_item_id = "item-1",
                    quantity = 2,
                });

            Assert.Equal("accepted", add.GetProperty("status").GetString());

            var submit = await PushOneAsync(
                sync,
                waiter,
                "M-SUBMIT-1",
                "order.submit",
                new
                {
                    client_order_id = "ORDER-1",
                });

            Assert.Equal("accepted", submit.GetProperty("status").GetString());

            await using var db = factory.Create();
            var order = await db.Orders.SingleAsync();

            Assert.Equal("submitted", order.Status);
            Assert.Equal(500m, order.Subtotal);
            Assert.Equal(500m, order.Total);
            Assert.Equal("occupied", (await db.DiningTables.SingleAsync()).Status);
            Assert.Single(await db.OrderItems.ToListAsync());

            var pull = JsonSerializer.SerializeToElement(
                await sync.PullAsync(waiter, 0, 100, CancellationToken.None));

            Assert.True(pull.GetProperty("cursor").GetInt64() > 0);
            Assert.NotEmpty(pull.GetProperty("changes").EnumerateArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Bootstrap_carries_menu_image_url_to_LAN_clients()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalogStore = new OperationalSnapshotStore(factory);
            await catalogStore.ApplyAsync(Snapshot());
            var sync = new LocalSyncService(factory, catalogStore);
            var waiter = new LocalTerminalPrincipal(
                "device-1",
                1,
                "waiter-1",
                "Waiter One",
                "waiter",
                "tenant-1");

            var bootstrap = JsonSerializer.SerializeToElement(
                await sync.BootstrapAsync(waiter, CancellationToken.None));

            Assert.Equal(
                "https://restaurant.example.test/media/menu-items/item-1",
                bootstrap.GetProperty("menu")[0]
                    .GetProperty("items")[0]
                    .GetProperty("image_url")
                    .GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Mutation_replay_is_safe_and_reuse_with_different_content_is_rejected()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalogStore = new OperationalSnapshotStore(factory);
            await catalogStore.ApplyAsync(Snapshot());
            var sync = new LocalSyncService(factory, catalogStore);
            var waiter = new LocalTerminalPrincipal(
                "device-1",
                1,
                "waiter-1",
                "Waiter One",
                "waiter",
                "tenant-1");

            var first = await PushOneAsync(
                sync,
                waiter,
                "M-SAME",
                "order.open",
                new
                {
                    client_order_id = "ORDER-1",
                    dining_table_id = "table-1",
                    guest_count = 2,
                });

            var replay = await PushOneAsync(
                sync,
                waiter,
                "M-SAME",
                "order.open",
                new
                {
                    client_order_id = "ORDER-1",
                    dining_table_id = "table-1",
                    guest_count = 2,
                });

            var reused = await PushOneAsync(
                sync,
                waiter,
                "M-SAME",
                "order.open",
                new
                {
                    client_order_id = "ORDER-1",
                    dining_table_id = "table-1",
                    guest_count = 3,
                });

            Assert.Equal("accepted", first.GetProperty("status").GetString());
            Assert.Equal("accepted", replay.GetProperty("status").GetString());
            Assert.Equal("rejected", reused.GetProperty("status").GetString());
            Assert.Equal("mutation_id_reused", reused.GetProperty("code").GetString());

            await using var db = factory.Create();
            Assert.Single(await db.Orders.ToListAsync());
            Assert.Single(await db.Mutations.ToListAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Second_waiter_gets_table_busy_conflict()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var factory = new LocalDatabaseFactory(root);
            var catalogStore = new OperationalSnapshotStore(factory);
            await catalogStore.ApplyAsync(Snapshot());
            var sync = new LocalSyncService(factory, catalogStore);

            var waiterOne = new LocalTerminalPrincipal(
                "device-1",
                1,
                "waiter-1",
                "Waiter One",
                "waiter",
                "tenant-1");
            var waiterTwo = new LocalTerminalPrincipal(
                "device-2",
                2,
                "waiter-2",
                "Waiter Two",
                "waiter",
                "tenant-1");

            await PushOneAsync(
                sync,
                waiterOne,
                "M-OPEN-1",
                "order.open",
                new
                {
                    client_order_id = "ORDER-1",
                    dining_table_id = "table-1",
                });

            var conflict = await PushOneAsync(
                sync,
                waiterTwo,
                "M-OPEN-2",
                "order.open",
                new
                {
                    client_order_id = "ORDER-2",
                    dining_table_id = "table-1",
                });

            Assert.Equal("conflict", conflict.GetProperty("status").GetString());
            Assert.Equal("table_busy", conflict.GetProperty("code").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cached_terminal_credentials_authenticate_without_cloud()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var factory = new LocalDatabaseFactory(root);
            var store = new OperationalSnapshotStore(factory);
            await store.ApplyAsync(Snapshot());

            await using (var db = factory.Create())
            {
                db.PairedTerminals.Add(new LocalPairedTerminal
                {
                    DeviceId = "device-1",
                    TenantId = "tenant-1",
                    DeviceSecretHash = Hash("device-secret"),
                    AccessTokenHash = Hash("access-token"),
                    UserId = 1,
                    UserPublicId = "waiter-1",
                    UserName = "Waiter One",
                    UserRole = "waiter",
                    ValidatedAtUtc = DateTimeOffset.UtcNow,
                    LastSeenAtUtc = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            var context = new DefaultHttpContext();
            context.Request.Headers.Authorization = "Bearer access-token";
            context.Request.Headers["X-Device-Id"] = "device-1";
            context.Request.Headers["X-Device-Secret"] = "device-secret";
            context.Request.Headers["X-App-Version"] = "1.0.0";

            var activationStore = new WindowsActivationStore(root);
            await activationStore.SaveAsync(ValidActivation("tenant-1"));
            var settingsStore = new ConnectionSettingsStore(root);
            var terminalManagement = new LocalTerminalManagementService(
                factory,
                settingsStore,
                activationStore);

            var authenticator = new LocalTerminalAuthenticator(
                factory,
                activationStore,
                settingsStore,
                store,
                terminalManagement,
                new HttpClient());

            var principal = await authenticator.AuthenticateAsync(
                context.Request,
                new LocalServerOptions("tenant-1"),
                allowCloudPairing: false,
                CancellationToken.None);

            Assert.NotNull(principal);
            Assert.Equal(1, principal.UserId);
            Assert.Equal("waiter", principal.UserRole);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<JsonElement> PushOneAsync(
        LocalSyncService sync,
        LocalTerminalPrincipal principal,
        string mutationId,
        string operation,
        object payload)
    {
        var request = new LocalSyncPushRequest(
            "batch-1",
            [
                new LocalSyncMutationRequest(
                    mutationId,
                    operation,
                    DateTimeOffset.UtcNow,
                    JsonSerializer.SerializeToElement(payload)),
            ]);

        var response = JsonSerializer.SerializeToElement(
            await sync.PushAsync(principal, request, CancellationToken.None));

        return response.GetProperty("results")[0];
    }

    private static ActivationState ValidActivation(string tenantId)
    {
        var payload = JsonSerializer.SerializeToElement(new { });
        var lease = new SignedLease(payload, "signature", "Ed25519", "key-1");
        var now = DateTimeOffset.UtcNow;
        var snapshot = new LeaseSnapshot(
            1,
            "lease-1",
            "key-1",
            tenantId,
            "business-1",
            "subscription-1",
            1,
            "desktop-device",
            "desktop-uid",
            "standard",
            "Standard",
            now.AddHours(-1),
            now.AddDays(2),
            now.AddDays(30),
            payload);

        return new ActivationState(
            "https://restaurant.example.test",
            "desktop-device",
            "desktop-uid",
            "desktop-secret",
            "public-key",
            "key-1",
            lease,
            snapshot,
            now);
    }

    private static OperationalSnapshot Snapshot() =>
        new(
            1,
            DateTimeOffset.UtcNow,
            0,
            "tenant-1",
            [
                new BranchSnapshot("branch-1", "MAIN", "Main Branch", true),
            ],
            [
                new StaffSnapshot(1, "waiter-1", "Waiter One", "one@example.test", "waiter", true),
                new StaffSnapshot(2, "waiter-2", "Waiter Two", "two@example.test", "waiter", true),
            ],
            [
                new MenuCategorySnapshot(
                    "category-1",
                    "Main",
                    1,
                    [
                        new MenuItemSnapshot(
                            "item-1",
                            "category-1",
                            "FOOD-1",
                            "Kabuli Pulao",
                            null,
                            250m,
                            "AFN",
                            1,
                            [],
                            "https://restaurant.example.test/media/menu-items/item-1"),
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

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

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
