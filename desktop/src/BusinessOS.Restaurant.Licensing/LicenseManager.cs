namespace BusinessOS.Restaurant.Licensing;

public sealed class LicenseManager
{
    private readonly LicenseApiClient _client;
    private readonly SignedLeaseVerifier _verifier;
    private readonly WindowsActivationStore _store;
    private readonly InstallationIdentityProvider _identity;

    public LicenseManager(
        LicenseApiClient client,
        SignedLeaseVerifier verifier,
        WindowsActivationStore store,
        InstallationIdentityProvider identity)
    {
        _client = client;
        _verifier = verifier;
        _store = store;
        _identity = identity;
    }

    public async Task<ActivationState> ActivateAsync(
        Uri tenantBaseUri,
        string licenseKey,
        string? appVersion,
        CancellationToken cancellationToken = default)
    {
        var deviceUid = await _identity.GetOrCreateAsync(cancellationToken);
        var publicKey = await _client.GetPublicKeyAsync(tenantBaseUri, cancellationToken);

        if (!string.Equals(publicKey.Algorithm, "Ed25519", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The server returned an unsupported license signing algorithm.");
        }

        var response = await _client.ActivateAsync(
            tenantBaseUri,
            new LicenseActivationRequest(
                licenseKey.Trim(),
                deviceUid,
                Environment.MachineName,
                appVersion),
            cancellationToken);

        var snapshot = _verifier.Verify(response.Lease, publicKey.PublicKey);

        if (!string.Equals(snapshot.DeviceId, response.Device.Id, StringComparison.Ordinal) ||
            !string.Equals(snapshot.DeviceUid, deviceUid, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The signed lease does not belong to this Windows installation.");
        }

        var state = new ActivationState(
            tenantBaseUri.AbsoluteUri.TrimEnd('/'),
            response.Device.Id,
            deviceUid,
            response.DeviceSecret,
            publicKey.PublicKey,
            publicKey.KeyId,
            response.Lease,
            snapshot,
            DateTimeOffset.UtcNow);

        await _store.SaveAsync(state, cancellationToken);
        return state;
    }

    public async Task<ActivationState> RefreshAsync(
        ActivationState current,
        string? appVersion,
        CancellationToken cancellationToken = default)
    {
        var tenantBaseUri = new Uri(current.TenantBaseUrl, UriKind.Absolute);
        var response = await _client.RefreshLeaseAsync(
            tenantBaseUri,
            current.DeviceId,
            current.DeviceSecret,
            appVersion,
            cancellationToken);

        var snapshot = _verifier.Verify(response.Lease, current.PublicKey);

        if (!string.Equals(snapshot.DeviceId, current.DeviceId, StringComparison.Ordinal) ||
            !string.Equals(snapshot.DeviceUid, current.DeviceUid, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The refreshed lease does not belong to this Windows installation.");
        }

        var updated = current with
        {
            Lease = response.Lease,
            Snapshot = snapshot,
            LastVerifiedAt = DateTimeOffset.UtcNow,
        };

        await _store.SaveAsync(updated, cancellationToken);
        return updated;
    }

    public static bool CanRunOffline(ActivationState state, DateTimeOffset nowUtc) =>
        nowUtc.ToUniversalTime() <= state.Snapshot.OfflineValidUntil.ToUniversalTime();
}
