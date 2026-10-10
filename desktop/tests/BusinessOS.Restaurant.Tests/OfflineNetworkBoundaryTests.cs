using System.Net;
using System.Text.Json;
using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.Persistence;
using BusinessOS.Restaurant.Sync;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class OfflineNetworkBoundaryTests
{
    [Fact]
    public async Task Signed_standalone_mode_cannot_send_cloud_requests_even_when_direct_client_is_called()
    {
        var activation = Activation(DesktopOperatingMode.StandaloneOffline);
        var user = Session();
        var handler = new CountingFailingHandler();
        using var http = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CloudOperationalDataClient(http).FetchBootstrapAsync(activation, user));

        var client = new CloudReconciliationClient(http);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.PushAsync(activation, user, [], CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.PullAsync(activation, user, 0, 100, CancellationToken.None));

        Assert.Equal(0, handler.Sends);
    }

    [Fact]
    public async Task Signed_standalone_mode_refuses_cloud_reconciliation_without_changing_financial_outbox()
    {
        var root = Path.Combine(Path.GetTempPath(), "RestaurantOfflineBoundary",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var dbFactory = new LocalDatabaseFactory(root);
            await dbFactory.EnsureCreatedAsync();
            await using (var db = dbFactory.Create())
            {
                db.CloudOutbox.Add(new LocalCloudOutboxMutation
                {
                    Id = "sale-1", Operation = "payment.create",
                    EntityType = "payment", LocalEntityId = "payment-1",
                    ActorPublicId = "cashier-1", ActorUserId = 1,
                    PayloadJson = "{\"amount\":250}", Status = "pending",
                    Attempts = 0, OccurredAtUtc = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            var handler = new CountingFailingHandler();
            using var http = new HttpClient(handler);
            var reconciler = new CloudReconciliationService(
                dbFactory, new CloudReconciliationClient(http));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                reconciler.RunOnceAsync(Activation(DesktopOperatingMode.StandaloneOffline), Session()));
            await using var read = dbFactory.Create();
            var item = await read.CloudOutbox.SingleAsync();
            Assert.Equal("pending", item.Status);
            Assert.Equal(0, item.Attempts);
            Assert.Equal(0, handler.Sends);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ActivationState Activation(string mode)
    {
        using var json = JsonDocument.Parse("{}");
        var now = DateTimeOffset.UtcNow;
        var payload = json.RootElement.Clone();
        var snapshot = new LeaseSnapshot(
            1, "lease", "key", "tenant-1", "business", "subscription", 1,
            "device", "device-uid", "plan", "Restaurant",
            now, now.AddDays(100), now.AddDays(100), payload, DesktopMode: mode);
        return new ActivationState("https://tenant.example.test", "device", "device-uid",
            "secret", "key", "key", new SignedLease(payload, "signature", "Ed25519", "key"),
            snapshot, now);
    }

    private static AuthSession Session() => new(
        "https://tenant.example.test", "test-token",
        new AuthUser(1, "cashier-1", "Cashier", "cashier@example.test", "cashier"),
        "tenant-1", DateTimeOffset.UtcNow);

    private sealed class CountingFailingHandler : HttpMessageHandler
    {
        public int Sends { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sends++;
            return Task.FromException<HttpResponseMessage>(
                new HttpRequestException("The internet is disconnected.", null, HttpStatusCode.ServiceUnavailable));
        }
    }
}
