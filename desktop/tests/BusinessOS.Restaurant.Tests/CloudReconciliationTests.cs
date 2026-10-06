using System.Net;
using System.Text;
using System.Text.Json;
using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.Persistence;
using BusinessOS.Restaurant.Sync;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class CloudReconciliationTests
{
    [Fact]
    public async Task Accepted_outbox_mutation_is_marked_synced_and_linked()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var factory = new LocalDatabaseFactory(root);
            await factory.EnsureCreatedAsync();

            await using (var db = factory.Create())
            {
                db.CloudOutbox.Add(new LocalCloudOutboxMutation
                {
                    Id = "mutation-1",
                    Operation = "supplier.create",
                    EntityType = "supplier",
                    LocalEntityId = "local-supplier-1",
                    ActorUserId = 1,
                    ActorPublicId = "actor-1",
                    PayloadJson = """{"code":"SUP-1","name":"Supplier One"}""",
                    Status = "pending",
                    Attempts = 0,
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            using var http = new HttpClient(new QueueHandler(
                Json(HttpStatusCode.OK, """
                {"data":{"server_time":"2026-10-06T18:00:00Z","results":[{"mutation_id":"mutation-1","status":"accepted","operation":"supplier.create","entity_type":"supplier","entity_id":"cloud-supplier-1","code":null,"message":null}],"pull_cursor":7}}
                """),
                Json(HttpStatusCode.OK, """
                {"data":{"server_time":"2026-10-06T18:00:01Z","cursor":7,"has_more":false,"changes":[]}}
                """)));

            var service = new CloudReconciliationService(
                factory,
                new CloudReconciliationClient(http));

            var result = await service.RunOnceAsync(
                Activation(),
                Session());

            Assert.Equal(1, result.Pushed);
            Assert.Equal(1, result.Accepted);
            Assert.Equal(0, result.Conflicts);

            await using var verify = factory.Create();
            var mutation = await verify.CloudOutbox.SingleAsync();
            var link = await verify.CloudEntityLinks.SingleAsync();

            Assert.Equal("synced", mutation.Status);
            Assert.Equal("cloud-supplier-1", mutation.CloudEntityId);
            Assert.Equal("supplier", link.EntityType);
            Assert.Equal("local-supplier-1", link.LocalEntityId);
            Assert.Equal("cloud-supplier-1", link.CloudEntityId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cloud_conflict_is_persisted_without_discarding_local_payload()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var factory = new LocalDatabaseFactory(root);
            await factory.EnsureCreatedAsync();

            await using (var db = factory.Create())
            {
                db.CloudOutbox.Add(new LocalCloudOutboxMutation
                {
                    Id = "mutation-conflict",
                    Operation = "order.serve",
                    EntityType = "order",
                    LocalEntityId = "local-order-1",
                    ActorUserId = 1,
                    ActorPublicId = "actor-1",
                    PayloadJson = """{"client_order_id":"ORDER-1"}""",
                    Status = "pending",
                    Attempts = 0,
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            using var http = new HttpClient(new QueueHandler(
                Json(HttpStatusCode.OK, """
                {"data":{"server_time":"2026-10-06T18:00:00Z","results":[{"mutation_id":"mutation-conflict","status":"conflict","operation":"order.serve","entity_type":"order","entity_id":null,"code":"state_conflict","message":"Cloud order state differs."}],"pull_cursor":0}}
                """),
                Json(HttpStatusCode.OK, """
                {"data":{"server_time":"2026-10-06T18:00:01Z","cursor":0,"has_more":false,"changes":[]}}
                """)));

            var service = new CloudReconciliationService(
                factory,
                new CloudReconciliationClient(http));

            var result = await service.RunOnceAsync(
                Activation(),
                Session());

            Assert.Equal(1, result.Conflicts);

            await using var verify = factory.Create();
            var mutation = await verify.CloudOutbox.SingleAsync();
            var conflict = await verify.CloudConflicts.SingleAsync();

            Assert.Equal("conflict", mutation.Status);
            Assert.Equal("state_conflict", conflict.Code);
            Assert.Contains("ORDER-1", conflict.LocalPayloadJson, StringComparison.Ordinal);
            Assert.Equal("open", conflict.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }


    [Fact]
    public async Task Cloud_pull_imports_supplier_and_advances_cursor()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var factory = new LocalDatabaseFactory(root);
            await factory.EnsureCreatedAsync();

            using var http = new HttpClient(new QueueHandler(
                Json(HttpStatusCode.OK, """
                {"data":{"server_time":"2026-10-06T18:00:01Z","cursor":12,"has_more":false,"changes":[{"sequence":12,"entity_type":"supplier","entity_id":"cloud-supplier-2","operation":"upsert","payload":{"id":"cloud-supplier-2","code":"SUP-2","name":"Cloud Supplier","phone":null,"email":null,"address":null,"is_active":true},"occurred_at":"2026-10-06T18:00:00Z","local_links":[]}]}}
                """)));

            var service = new CloudReconciliationService(
                factory,
                new CloudReconciliationClient(http));

            var result = await service.RunOnceAsync(
                Activation(),
                Session());

            Assert.Equal(1, result.Pulled);
            Assert.Equal(12, result.Cursor);

            await using var verify = factory.Create();
            var supplier = await verify.Suppliers.SingleAsync();
            var link = await verify.CloudEntityLinks.SingleAsync();
            var state = await verify.CloudSyncStates.SingleAsync();

            Assert.Equal("SUP-2", supplier.Code);
            Assert.Equal("Cloud Supplier", supplier.Name);
            Assert.Equal(supplier.Id, link.LocalEntityId);
            Assert.Equal("cloud-supplier-2", link.CloudEntityId);
            Assert.Equal(12, state.PullCursor);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ActivationState Activation()
    {
        using var document = JsonDocument.Parse("{}");
        var lease = new SignedLease(
            document.RootElement.Clone(),
            "signature",
            "Ed25519",
            "key-1");

        var snapshot = new LeaseSnapshot(
            1,
            "lease-1",
            "key-1",
            "tenant-1",
            "business-1",
            "subscription-1",
            1,
            "device-1",
            "uid-1",
            "standard",
            "Standard",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(7),
            DateTimeOffset.UtcNow.AddDays(14),
            document.RootElement.Clone());

        return new ActivationState(
            "https://restaurant.example.test",
            "device-1",
            "uid-1",
            "secret",
            "public-key",
            "key-1",
            lease,
            snapshot,
            DateTimeOffset.UtcNow);
    }

    private static AuthSession Session() =>
        new(
            "https://restaurant.example.test",
            "access-token",
            new AuthUser(1, "actor-1", "Manager", "manager@example.test", "manager"),
            "tenant-1",
            DateTimeOffset.UtcNow);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "BusinessOS.Restaurant.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
