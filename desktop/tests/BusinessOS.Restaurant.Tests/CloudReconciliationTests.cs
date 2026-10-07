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

    [Fact]
    public async Task Cloud_waiter_order_imports_lines_kot_and_print_job_idempotently()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var factory = new LocalDatabaseFactory(root);
            await factory.EnsureCreatedAsync();

            await using (var db = factory.Create())
            {
                db.Branches.Add(new LocalBranch
                {
                    Id = "branch-1",
                    Code = "MAIN",
                    Name = "Main Branch",
                    IsActive = true,
                });
                db.DiningAreas.Add(new LocalDiningArea
                {
                    Id = "area-1",
                    BranchId = "branch-1",
                    Name = "Main Hall",
                    SortOrder = 1,
                    IsActive = true,
                });
                db.DiningTables.Add(new LocalDiningTable
                {
                    Id = "table-1",
                    DiningAreaId = "area-1",
                    Code = "T-01",
                    Name = "Table 1",
                    Capacity = 4,
                    Status = "available",
                    IsActive = true,
                });
                db.MenuCategories.Add(new LocalMenuCategory
                {
                    Id = "category-1",
                    Name = "Mains",
                    SortOrder = 1,
                    IsActive = true,
                });
                db.MenuItems.Add(new LocalMenuItem
                {
                    Id = "menu-1",
                    MenuCategoryId = "category-1",
                    Sku = "FOOD-1",
                    Name = "Kabuli Pulao",
                    Price = 250m,
                    Currency = "AFN",
                    SortOrder = 1,
                    IsAvailable = true,
                });
                db.StaffUsers.Add(new LocalStaffUser
                {
                    Id = 7,
                    PublicId = "waiter-public-7",
                    Name = "Waiter Seven",
                    Email = "waiter7@example.test",
                    Role = "waiter",
                    IsActive = true,
                });
                db.KitchenStations.Add(new LocalKitchenStation
                {
                    Id = "station-1",
                    BranchId = "branch-1",
                    Code = "HOT",
                    Name = "Hot Kitchen",
                    SortOrder = 1,
                    IsActive = true,
                });
                db.KitchenPrinterBindings.Add(new LocalKitchenPrinterBinding
                {
                    KitchenStationId = "station-1",
                    PrinterName = "Kitchen Printer",
                    Copies = 1,
                    IsEnabled = true,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            const string orderPayload = """
            {
              "id":"cloud-order-1",
              "client_order_id":"MOBILE-CLOUD-ORDER-1",
              "dining_table_id":"table-1",
              "waiter_id":7,
              "status":"submitted",
              "guest_count":2,
              "notes":"No onions",
              "subtotal":"500.00",
              "total":"500.00",
              "opened_at":"2026-10-07T05:00:00Z",
              "submitted_at":"2026-10-07T05:01:00Z",
              "items":[
                {
                  "id":"cloud-line-1",
                  "client_line_id":"mobile-line-1",
                  "menu_item_id":"menu-1",
                  "item_name":"Kabuli Pulao",
                  "unit_price":"250.00",
                  "quantity":2,
                  "line_total":"500.00",
                  "notes":"No onions",
                  "status":"queued"
                }
              ],
              "kitchen_tickets":[
                {
                  "id":"cloud-ticket-1",
                  "order_id":"cloud-order-1",
                  "kitchen_station_id":"station-1",
                  "submitted_by_user_id":7,
                  "ticket_number":"KOT-CLOUD-001",
                  "status":"queued",
                  "queued_at":"2026-10-07T05:01:00Z",
                  "station":{"id":"station-1","branch_id":"branch-1","code":"HOT","name":"Hot Kitchen","sort_order":1,"is_active":true},
                  "items":[
                    {
                      "id":"cloud-ticket-line-1",
                      "order_item_id":"cloud-line-1",
                      "item_name":"Kabuli Pulao",
                      "quantity":2,
                      "notes":"No onions",
                      "status":"queued"
                    }
                  ]
                }
              ]
            }
            """;

            var responseJson =
                """{"data":{"server_time":"2026-10-07T05:02:00Z","cursor":21,"has_more":false,"changes":[{"sequence":20,"entity_type":"order","entity_id":"cloud-order-1","operation":"upsert","payload":""" +
                orderPayload +
                ""","occurred_at":"2026-10-07T05:01:00Z","local_links":[]},{"sequence":21,"entity_type":"order","entity_id":"cloud-order-1","operation":"upsert","payload":""" +
                orderPayload +
                ""","occurred_at":"2026-10-07T05:01:30Z","local_links":[]}]}}""";

            using var http = new HttpClient(new QueueHandler(Json(HttpStatusCode.OK, responseJson)));
            var service = new CloudReconciliationService(
                factory,
                new CloudReconciliationClient(http));

            var result = await service.RunOnceAsync(Activation(), Session());

            Assert.Equal(2, result.Pulled);
            Assert.Equal(21, result.Cursor);

            await using var verify = factory.Create();
            Assert.Equal(1, await verify.Orders.CountAsync());
            Assert.Equal(1, await verify.OrderItems.CountAsync());
            Assert.Equal(1, await verify.KitchenTickets.CountAsync());
            Assert.Equal(1, await verify.KitchenTicketItems.CountAsync());
            Assert.Equal(1, await verify.PrintJobs.CountAsync());

            var order = await verify.Orders.SingleAsync();
            var ticket = await verify.KitchenTickets.SingleAsync();
            var print = await verify.PrintJobs.SingleAsync();
            var table = await verify.DiningTables.SingleAsync();

            Assert.Equal("MOBILE-CLOUD-ORDER-1", order.ClientOrderId);
            Assert.Equal("submitted", order.Status);
            Assert.Equal("occupied", table.Status);
            Assert.Equal("KOT-CLOUD-001", ticket.TicketNumber);
            Assert.Equal("pending", print.Status);
            Assert.Contains("SOURCE: CLOUD FALLBACK", print.PayloadText, StringComparison.Ordinal);
            Assert.Contains("2 x Kabuli Pulao", print.PayloadText, StringComparison.Ordinal);
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
