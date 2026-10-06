# Local Android ordering

The Windows desktop host is the operational authority while the restaurant is using the local network.

## Request path

Android waiter -> `http://<desktop-lan-ip>:8787/api/v1/sync/*` -> desktop SQLite -> kitchen/cashier.

The public cloud is not required for accepted local order mutations.

## Credential trust

The Android app continues to use its normal BusinessOS device headers and bearer token.

On first local synchronization:

1. The desktop validates those credentials once against the configured tenant cloud endpoint.
2. The tenant and active staff identity must match the activated desktop tenant.
3. The desktop stores only SHA-256 credential hashes plus the staff/device identity.
4. Later LAN requests can be authenticated from the local cache while Internet is unavailable.

If the Android token/device credentials change, the desktop validates the new values against cloud before replacing the cached pairing.

## Control-plane proxy

The desktop exposes only the control-plane routes needed by the existing Flutter setup flow:

- `GET /api/v1/license/public-key`
- `POST /api/v1/license/activate`
- `POST /api/v1/auth/login`
- `POST /api/v1/license/lease`

Those routes are narrow proxies to the configured tenant cloud URL. They do not make restaurant orders cloud-dependent.

## Local sync contract

The desktop implements the same mutation contract already used by Laravel:

- `order.open`
- `order.item.add`
- `order.submit`

Responses preserve the same status and conflict concepts:

- `accepted`
- `conflict`
- `rejected`
- `table_busy`
- `order_state_conflict`
- `menu_unavailable`
- `dependency_missing`
- `mutation_id_reused`

Mutation IDs are persisted per Android device, so retries are idempotent.

## Pull cursor

Desktop changes use an incrementing SQLite sequence. Android pulls with a cursor exactly as it does against Laravel. Waiters only receive their own order changes while shared table/menu state remains visible.

## Offline lease

When the Android signed lease is still valid, a failed proactive cloud refresh does not stop LAN synchronization. An expired/invalid signed lease remains a hard stop.

## Next batch

Kitchen/KOT execution will consume submitted local orders and publish preparation/ready state changes back through the same local pull stream.
