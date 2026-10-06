# Desktop ↔ Cloud reconciliation

Batch 11 adds durable two-way synchronization between the Windows LAN authority and the Laravel tenant cloud.

## Local-first rule

Restaurant service never depends on live Internet.

Local operations commit to SQLite first. The same SQLite transaction appends a semantic cloud mutation to the outbox. If the Internet is unavailable, the mutation remains retryable while Android, KOT, cashier, inventory and closing workflows continue on LAN.

The desktop reconciliation processor runs while the desktop app is open and sync is enabled.

## Local outbox

Each mutation stores:

- stable mutation ID
- semantic operation
- entity type
- local entity ID
- original actor public ID
- JSON payload
- occurrence timestamp
- attempt count
- status
- cloud entity ID after acknowledgement
- error/conflict information

Normal states are:

`pending -> sending -> synced`

Network failures move the item to `retry`.

Business conflicts move the item to `conflict` or `rejected` and create a separate conflict record without deleting the local payload.

## Local ↔ cloud entity links

SQLite and Laravel do not assume that their primary keys are identical.

A durable mapping stores:

`entity type + local ID -> cloud ID`

Laravel also stores the device-specific reverse mapping. This is used when later dependent operations are reconciled, for example:

- local cashier session -> cloud cashier session
- local bill -> cloud bill
- local supplier -> cloud supplier
- local inventory item -> cloud inventory item
- local PO/PO line -> cloud PO/PO line
- local daily closing -> cloud daily closing

## Semantic reconciliation

The desktop currently queues semantic mutations for:

- Android/LAN order open, item add and submit
- serve order
- cashier session open/close
- issue bill
- discount
- payment
- inventory item creation
- inventory adjustment
- supplier creation
- recipe version creation
- purchase order creation
- goods receipt posting
- staff shift upsert
- daily closing finalize/reopen
- append-only local audit events

The cloud applies native business services rather than blindly copying SQLite rows. This preserves Laravel validation, accounting hooks, inventory valuation, KOT rules and idempotency.

## Reconciliation-only records

Staff shifts and the original LAN audit stream do not have equivalent native Laravel business models yet.

They are retained in `desktop_operational_records` with:

- activated device ID
- record type
- local entity ID
- original actor
- payload
- occurrence timestamp

This preserves the operational record without forcing it into an incompatible cloud model.

## Retry and idempotency

Laravel reuses the existing sync mutation ledger.

A mutation ID can be safely retried with the same payload. Reusing the same mutation ID with different content is rejected.

Existing domain idempotency is also preserved:

- client order IDs
- client line IDs
- client payment IDs
- client adjustment IDs
- client receipt IDs

## Cloud -> desktop

Cloud models now publish changes for:

- orders
- tables/menu reference data
- bills
- cashier sessions
- inventory items
- inventory balances
- stock movements
- suppliers
- recipes
- purchase orders
- goods receipts
- daily closings

The desktop pulls these changes with a monotonic cursor.

Fresh relational snapshots are returned instead of stale event fragments.

Safe cloud changes are applied to linked local records. Cloud-origin inventory items, suppliers and orders can also be imported when their dependencies exist locally.

## Conflict handling

Before applying a cloud change, the desktop checks for an unsynced local mutation on the same linked record.

If both sides changed the record, the desktop does not overwrite local state. It creates an open conflict containing:

- operation
- entity type
- local entity ID
- local payload
- cloud payload when available
- conflict code/message
- creation time

This makes conflicts explicit and auditable.

## Authentication

Desktop reconciliation requires:

- a valid tenant bearer session
- activated device ID
- activated device secret
- active subscription
- an operational tenant role

The original actor public ID is included in each mutation. Laravel resolves that actor inside the same tenant so audit/accounting actions retain the staff identity that performed the LAN operation.

## Endpoints

- `POST /api/v1/desktop/reconcile/push`
- `GET /api/v1/desktop/reconcile/pull?cursor=...&limit=...`

## Automatic cadence

While the Windows desktop is running and synchronization is enabled, it attempts reconciliation every 30 seconds.

A failed attempt does not block local service. The next cycle retries the durable outbox.
