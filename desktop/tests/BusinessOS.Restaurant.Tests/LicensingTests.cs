using System.Reflection;
using System.Text;
using System.Text.Json;
using BusinessOS.Restaurant.Licensing;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class LicensingTests
{
    [Fact]
    public void Activation_request_uses_windows_platform_and_server_contract_names()
    {
        var request = new LicenseActivationRequest(
            "RST-TEST-TEST-TEST-TEST",
            "installation-1",
            "Cashier Terminal",
            "1.0.0");

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("RST-TEST-TEST-TEST-TEST", root.GetProperty("license_key").GetString());
        Assert.Equal("installation-1", root.GetProperty("device_uid").GetString());
        Assert.Equal("windows", root.GetProperty("platform").GetString());
    }

    [Fact]
    public void Lease_canonicalization_matches_server_for_unicode_and_html_sensitive_text()
    {
        using var payload = JsonDocument.Parse(
            """
            {
              "z": "سلام/کابل & <table>",
              "a": {
                "value": "پښتو",
                "quote": "\"hello\""
              }
            }
            """);

        var method = typeof(SignedLeaseVerifier).GetMethod(
            "Canonicalize",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var canonical = (byte[])method!.Invoke(
            null,
            new object[] { payload.RootElement })!;

        Assert.Equal(
            "{\"a\":{\"quote\":\"\\\"hello\\\"\",\"value\":\"پښتو\"},\"z\":\"سلام/کابل & <table>\"}",
            Encoding.UTF8.GetString(canonical));
    }

    [Fact]
    public async Task Installation_identity_is_stable_for_the_same_installation()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var provider = new InstallationIdentityProvider(root);
            var first = await provider.GetOrCreateAsync();
            var second = await provider.GetOrCreateAsync();

            Assert.Equal(first, second);
            Assert.True(Guid.TryParse(first, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Connection_settings_round_trip_sync_preference()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var store = new ConnectionSettingsStore(root);
            var expected = new ConnectionSettings("https://restaurant.example.test", SyncEnabled: false);

            await store.SaveAsync(expected);
            var actual = await store.LoadAsync();

            Assert.Equal(expected, actual);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Offline_access_expires_at_signed_lease_boundary()
    {
        using var payloadDocument = JsonDocument.Parse("{}");
        var lease = new SignedLease(
            payloadDocument.RootElement.Clone(),
            "signature",
            "Ed25519",
            "key-1");

        var cutoff = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
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
            cutoff.AddDays(-1),
            cutoff,
            cutoff.AddDays(10),
            payloadDocument.RootElement.Clone());

        var state = new ActivationState(
            "https://restaurant.example.test",
            "device-1",
            "uid-1",
            "secret",
            "public-key",
            "key-1",
            lease,
            snapshot,
            cutoff.AddHours(-1));

        Assert.True(LicenseManager.CanRunOffline(state, cutoff));
        Assert.False(LicenseManager.CanRunOffline(state, cutoff.AddTicks(1)));
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "BusinessOS.Restaurant.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
