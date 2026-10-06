using System.Text.Json;
using System.Text.Json.Serialization;

namespace BusinessOS.Restaurant.Licensing;

public sealed record LicenseActivationRequest(
    [property: JsonPropertyName("license_key")] string LicenseKey,
    [property: JsonPropertyName("device_uid")] string DeviceUid,
    [property: JsonPropertyName("device_name")] string? DeviceName,
    [property: JsonPropertyName("app_version")] string? AppVersion,
    [property: JsonPropertyName("platform")] string Platform = "windows");

public sealed record LeaseRefreshRequest(
    [property: JsonPropertyName("app_version")] string? AppVersion);

public sealed record ActivatedDevice(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("uid")] string Uid,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("activated_at")] DateTimeOffset? ActivatedAt);

public sealed record SignedLease(
    [property: JsonPropertyName("payload")] JsonElement Payload,
    [property: JsonPropertyName("signature")] string Signature,
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("key_id")] string KeyId);

public sealed record LicenseActivationResponse(
    [property: JsonPropertyName("device")] ActivatedDevice Device,
    [property: JsonPropertyName("device_secret")] string DeviceSecret,
    [property: JsonPropertyName("lease")] SignedLease Lease);

public sealed record LeaseRefreshResponse(
    [property: JsonPropertyName("lease")] SignedLease Lease);

public sealed record LicensePublicKeyResponse(
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("key_id")] string KeyId,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("public_key")] string PublicKey);

public sealed record LeaseSnapshot(
    int SchemaVersion,
    string LeaseId,
    string KeyId,
    string TenantId,
    string BusinessId,
    string SubscriptionId,
    int LicenseVersion,
    string DeviceId,
    string DeviceUid,
    string PlanCode,
    string PlanName,
    DateTimeOffset IssuedAt,
    DateTimeOffset OfflineValidUntil,
    DateTimeOffset SubscriptionEndsAt,
    JsonElement Features);
