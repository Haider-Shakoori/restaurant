namespace BusinessOS.Restaurant.Licensing;

public sealed record ActivationState(
    string TenantBaseUrl,
    string DeviceId,
    string DeviceUid,
    string DeviceSecret,
    string PublicKey,
    string PublicKeyId,
    SignedLease Lease,
    LeaseSnapshot Snapshot,
    DateTimeOffset LastVerifiedAt);
