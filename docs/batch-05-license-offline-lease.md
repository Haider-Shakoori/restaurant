# Batch 5 — License Generation, Device Activation and Signed Offline Lease

## Purpose

Batch 5 establishes the secure licensing foundation required by the future Android waiter application.

The design separates three credentials:

1. **Restaurant license key** — used only to activate a device.
2. **Device credential** — generated per activated device and used to refresh leases.
3. **Signed offline lease** — time-bounded authorization that can be verified locally without internet.

The raw restaurant license key is never ordinary API authentication.

## Restaurant license keys

Generated format:

```text
RST-XXXX-XXXX-XXXX-XXXX
```

The alphabet excludes commonly confused characters.

Properties:

- generated using cryptographically secure randomness;
- scoped to one restaurant/business;
- versioned;
- revocable;
- regeneratable;
- raw value shown once;
- only an HMAC-SHA256 hash is stored;
- old licenses are revoked when a new version is generated;
- device limit is snapshotted from the active subscription feature `max_devices` or the configured default.

A restaurant must have an active trial/subscription before a license can be generated.

## Device activation

Tenant endpoint:

```http
POST /api/v1/license/activate
```

Required fields:

- `license_key`;
- `device_uid`;
- `platform=android`.

Optional:

- `device_name`;
- `app_version`.

Successful activation returns:

- central device activation ID;
- one-time device secret;
- first signed offline lease.

The raw license key is not returned.

## Device credential

Each activated device receives a random 256-bit secret.

Only its HMAC-SHA256 hash is stored centrally.

Lease refresh:

```http
POST /api/v1/license/lease
X-Device-Id: <device activation ULID>
X-Device-Secret: <one-time device secret>
```

The Android app should store the device secret in platform secure storage, never SQLite/plain preferences.

Revoking a device blocks future lease refreshes.

## Offline lease

Offline leases use **Ed25519 detached signatures**.

The private key stays server-only. Flutter needs only the public key.

Lease payload includes:

- schema version;
- lease ID;
- signing key ID;
- tenant ID;
- central business ID;
- subscription ID;
- license version;
- device activation ID;
- device UID;
- plan code/name snapshot;
- feature-entitlement snapshot;
- device-limit snapshot;
- issued-at timestamp;
- offline-valid-until timestamp;
- subscription-end timestamp.

The offline expiry is:

```text
min(server now + configured offline grace, subscription end)
```

Therefore a lease can never authorize offline use beyond the central subscription end.

Default offline grace is seven days.

## Canonical signing format

Before signing, the server:

1. recursively sorts JSON object keys lexicographically;
2. preserves array order;
3. encodes UTF-8 JSON without escaped slashes or escaped Unicode;
4. signs the resulting bytes with Ed25519;
5. returns the detached signature as base64url without padding.

Flutter verification must implement the exact same canonicalization.

After cryptographic verification, Flutter must also reject the lease when:

- `key_id` is unknown;
- tenant ID does not match the configured tenant;
- device ID/UID does not match the local activation;
- `offline_valid_until` is in the past;
- `offline_valid_until` is after `subscription_ends_at`;
- schema version is unsupported.

Do not trust payload fields before signature verification.

## Public key endpoint

Tenant endpoint:

```http
GET /api/v1/license/public-key
```

Returns only:

- algorithm;
- key ID;
- schema version;
- public verification key.

No private key material is returned.

The production Flutter app should pin known public keys. The endpoint is useful for diagnostics and controlled key distribution, not as a substitute for trust pinning.

## Signing key generation

Generate the server keypair:

```bash
php artisan licenses:generate-signing-keys
```

Default paths:

```text
storage/app/private/keys/license-ed25519.secret
storage/app/private/keys/license-ed25519.public
```

The private key file is created with restrictive permissions and is covered by the existing private-storage ignore rule.

Never:

- commit the private key;
- put the private key in Flutter;
- send the private key to a restaurant;
- log the private key;
- overwrite the production keypair casually.

Intentional rotation uses `--force` only after changing the key ID and planning client public-key rollout. Existing offline leases cannot be remotely revoked while a device is disconnected; their exposure is bounded by the signed offline expiry.

## Environment configuration

Relevant settings:

- `PLATFORM_LICENSE_PREFIX`;
- `PLATFORM_LICENSE_SCHEMA_VERSION`;
- `PLATFORM_LICENSE_OFFLINE_GRACE_DAYS`;
- `PLATFORM_LICENSE_DEFAULT_MAX_DEVICES`;
- `PLATFORM_LICENSE_SIGNING_KEY_ID`;
- optional inline private/public signing keys;
- optional license/device hash peppers.

`ext-sodium` is an explicit Composer platform requirement.

## Central tables

- `license_keys`;
- `device_activations`;
- `offline_leases`;
- `license_events`.

These tables remain in the **central SaaS database**. Restaurant operational databases do not contain platform license secrets.

## Revocation model

License rotation/revocation:

- revokes the central license;
- revokes active device credentials under it;
- prevents future activation/lease refresh with those credentials.

Device revocation:

- revokes only the selected device credential.

Already-issued offline leases remain valid until their signed expiry because an offline device cannot receive a server revocation signal. This is why the lease duration is deliberately bounded.

No revocation deletes restaurant operational data.

## Verification coverage

Batch 5 tests prove:

- license format and raw-key non-storage;
- Ed25519 signature verification;
- public-key-only endpoint;
- lease expiry capped by subscription end;
- license key cannot substitute for device credential;
- device-limit enforcement;
- license rotation revokes old device credentials;
- device revocation blocks future refresh;
- tenant database remains intact after device revocation;
- raw license is displayed once in Platform Admin;
- Support is read-only;
- signing key generation does not print private key material.
