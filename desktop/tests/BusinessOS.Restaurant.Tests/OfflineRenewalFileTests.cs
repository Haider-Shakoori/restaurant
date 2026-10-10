using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BusinessOS.Restaurant.Licensing;
using NSec.Cryptography;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class OfflineRenewalFileTests
{
    [Fact]
    public async Task Signed_renewal_imports_without_internet_and_rejects_tampering_or_wrong_device()
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), "RestaurantOfflineRenewals",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var algorithm = SignatureAlgorithm.Ed25519;
            using var key = Key.Create(algorithm, new KeyCreationParameters
            {
                ExportPolicy = KeyExportPolicies.AllowPlaintextExport,
            });
            var publicKey = Encode(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
            var oldLease = CreateLease(key, "terminal-1", 10);
            var oldSnapshot = new SignedLeaseVerifier().Verify(oldLease, publicKey);
            var store = new WindowsActivationStore(root);
            var prefs = new ConnectionSettingsStore(root);
            await prefs.SaveAsync(new ConnectionSettings("https://tenant.example.test", SyncEnabled: true));
            await store.SaveAsync(new ActivationState(
                "https://tenant.example.test", "device-1", "terminal-1",
                "existing-secret", publicKey, "key-1",
                oldLease, oldSnapshot, DateTimeOffset.UtcNow));

            var coordinator = new RestaurantLicenseCoordinator(store, prefs,
                new InstallationIdentityProvider(root));
            var renewalPath = Path.Combine(root, "renewal.json");
            var fresh = CreateLease(key, "terminal-1", 45);
            await File.WriteAllTextAsync(renewalPath, JsonSerializer.Serialize(fresh));
            var renewed = await coordinator.ImportOfflineRenewalAsync(renewalPath);

            Assert.True(renewed.Snapshot.OfflineValidUntil > oldSnapshot.OfflineValidUntil);
            Assert.Equal("existing-secret", renewed.DeviceSecret);
            Assert.Equal("terminal-1", renewed.DeviceUid);
            Assert.False((await prefs.LoadAsync())!.SyncEnabled);

            var wrongDevice = CreateLease(key, "other-computer", 80);
            await File.WriteAllTextAsync(renewalPath, JsonSerializer.Serialize(wrongDevice));
            await Assert.ThrowsAsync<CryptographicException>(() =>
                coordinator.ImportOfflineRenewalAsync(renewalPath));

            await File.WriteAllTextAsync(renewalPath, JsonSerializer.Serialize(fresh)
                .Replace("standalone_offline", "cloud_sync", StringComparison.Ordinal));
            await Assert.ThrowsAsync<CryptographicException>(() =>
                coordinator.ImportOfflineRenewalAsync(renewalPath));

            // Signed old files cannot roll a device's renewal date backwards.
            await File.WriteAllTextAsync(renewalPath, JsonSerializer.Serialize(oldLease));
            await Assert.ThrowsAsync<CryptographicException>(() =>
                coordinator.ImportOfflineRenewalAsync(renewalPath));
            Assert.Equal(renewed.Snapshot.LeaseId, (await store.LoadAsync())!.Snapshot.LeaseId);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static SignedLease CreateLease(Key key, string deviceUid, int validityDays)
    {
        var now = DateTimeOffset.UtcNow;
        // Renewal term and issue time must be after the original.
        var issued = now.AddMinutes(validityDays == 10 ? -50 : -10);
        var data = new
        {
            schema_version = 1, lease_id = Guid.NewGuid().ToString("N"), key_id = "key-1",
            tenant_id = "tenant-1", business_id = "business-1",
            subscription_id = "subscription-1", license_version = 1,
            device_id = "device-1", device_uid = deviceUid,
            plan = new { code = "plan-1", name = "Restaurant" },
            features = new { }, desktop_mode = "standalone_offline",
            issued_at = issued.ToString("O"),
            offline_valid_until = now.AddDays(validityDays).ToString("O"),
            subscription_ends_at = now.AddDays(validityDays).ToString("O"),
        };
        var payload = JsonSerializer.SerializeToElement(data);
        var canon = (byte[])typeof(SignedLeaseVerifier).GetMethod("Canonicalize",
            BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [payload])!;
        var signature = SignatureAlgorithm.Ed25519.Sign(key, canon);
        return new SignedLease(payload, Encode(signature), "Ed25519", "key-1");
    }

    private static string Encode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
