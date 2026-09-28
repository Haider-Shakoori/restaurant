# Batch 11 — Flutter Offline Sync Hardening

Batch 11 adds the Android waiter client and the tenant synchronization protocol required for unreliable and intermittent internet.

## Delivered server contract

Authenticated mobile synchronization requires both:

- the waiter/manager tenant Sanctum bearer token; and
- an active restaurant device credential from Batch 5.

Device credentials are tenant-bound through the central BusinessOS restaurant record. A valid tenant user token alone cannot call the mobile sync endpoints without a valid activated device secret.

Tenant endpoints:

- GET /api/v1/sync/bootstrap
- POST /api/v1/sync/push
- GET /api/v1/sync/pull

### Bootstrap

Bootstrap returns a compact initial mobile snapshot:

- tenant/device/user identity;
- menu;
- dining tables;
- active waiter orders;
- current monotonic sync cursor;
- server time and configured batch size.

### Push

Push accepts an ordered batch of offline mutations. Each mutation carries a stable mutation_id and its own payload.

Supported waiter operations in Batch 11:

- order.open
- order.item.add
- order.submit

The tenant database records processed mutations by central device + mutation ID. Replaying identical content returns the stored result. Reusing an ID with different content is rejected.

Existing client_order_id and client_line_id uniqueness/idempotency remains the transactional order safety layer underneath sync.

### Deterministic conflicts

Expected domain conflicts return machine-readable codes rather than being retried forever:

- table_busy
- order_state_conflict
- menu_unavailable
- dependency_missing
- unsupported_operation
- invalid_payload
- mutation_id_reused

Conflict records retain operation, mutation ID and the server response for troubleshooting.

### Pull

Mobile-visible changes use a monotonically increasing sync_changes sequence.

Orders, tables, dining areas, menu categories and menu items emit incremental change records. Pull scans sequence pages, returns the visible snapshots, advances the cursor and exposes has_more for resumable continuation.

Waiter order detail remains filtered to the authenticated waiter. Tenant database isolation remains the outer boundary.

## Flutter client

The Flutter workspace is under mobile/.

The client uses:

- SQLite for menu/tables/orders/outbox/conflicts/system state;
- Flutter secure storage for device credential, bearer token and lease material;
- Ed25519 public-key verification for offline leases;
- UUID identifiers for client orders, lines and mutations;
- connectivity-triggered and periodic synchronization;
- bounded exponential retry for transient failures;
- a durable conflict inbox;
- English/Dari/Pashto labels with RTL for Dari/Pashto.

### Waiter flow

The initial functional UI supports:

Device activation/login → table grid → start table order → browse cached menu → add items offline → send order to kitchen queue → automatic/manual sync → conflict visibility.

The app is deliberately usable from cached SQLite state without requiring live menu/table requests for every screen.

## Lease and subscription behavior

The raw restaurant license key is used only during device activation and is not retained.

Before an offline mutation, the Flutter client verifies:

- Ed25519 signature;
- device identity;
- offline_valid_until;
- subscription_ends_at.

If a refreshed lease cannot be verified, it is not trusted or stored as authoritative authorization.

A server-side 423 subscription lock is persisted locally and blocks new offline operational mutations without deleting cached restaurant data.

## CI gates

Batch 11 CI runs both server and mobile jobs.

Server gate:

- Composer validation/security audit;
- Pint;
- production cache/route/view compilation;
- migrations;
- complete PHPUnit regression suite;
- frontend build.

Mobile gate:

- Flutter stable setup;
- Android platform generation;
- dependency resolution;
- Dart formatting;
- flutter analyze;
- flutter test;
- Android debug APK build.

## Batch boundary

Batch 11 hardens the internal waiter/mobile synchronization layer.

Batch 12 completes production subdomain deployment, production Android platform/signing configuration, release build distribution and full end-to-end regression across web, waiter mobile, kitchen, cashier, inventory and accounting.
