# KOT / KDS realignment audit

Audit baseline: `main@1642bb241424a12a3873d3d86ac5d47dcf0e07d9`.

Ownership: Laravel/Web/SaaS/API + Flutter only. Desktop source is read-only in this branch.

## Requirement matrix

| Requirement | Laravel existing | Flutter existing | Shared contract existing | Missing / mismatch | Action |
|---|---|---|---|---|---|
| Active order can receive later items | Partial: `Order::ACTIVE_STATUSES` already models operationally open states | Active orders are reopened from the table screen | No dedicated contract | `OrderService::addItem()` requires `draft`; Flutter local DB/UI also requires draft | Replace draft-only editability with one shared operational-open rule |
| Multiple KOT rounds | No | No | No | `KitchenService::dispatch()` returns all existing tickets as soon as any ticket exists | Add additive dispatch rounds; only unsent quantities/items may dispatch |
| KOT idempotency | Partial | Strong mutation IDs already exist | Mobile mutation replay is already durable | Current protection is one-KOT-per-order/station rather than per dispatch mutation | Reuse `sync_mutations` and stable client mutation IDs at round granularity |
| Human KOT number | Partial | Read-only consumer only | Existing ticket number is exposed in kitchen entities | Current number is `KOT-<ULID>`, not sequential human display | Add concurrency-safe human KOT number while retaining ULID PK |
| Station routing / General Kitchen | Yes | Consumes kitchen bootstrap | Yes | No redesign needed | Reuse |
| Item-level kitchen state | Schema exists on `kitchen_ticket_items.status` | Order item status is cached | No independent transition API | Service transitions whole ticket and bulk-updates children | Make child item state canonical; ticket/order state derived |
| Queue optional | No | No | No | Queue is hard-coded first state | Add shared setting + one state machine |
| Preparing optional | No | No | No | Start/preparing is hard-coded | Add shared setting + one state machine |
| Expo | No confirmed implementation | No | No | Missing | Add derived readiness projection; avoid redundant order model |
| Modifiers | Existing project contains modifier infrastructure, but it is not yet in current order/KOT snapshot flow | Not in current order UI/local line schema | No final contract | Needs integration, not parallel architecture | Audit modifier models/migrations then extend snapshots |
| Notes | Yes | Yes | Yes | Already flows at order-line level | Preserve |
| Allergies / critical instructions | No confirmed structured field | No | No | Missing | Add structured critical instruction snapshot |
| Courses / hold / fire | No confirmed | No | No | Missing | Add optional course fields and fire mutation |
| Service types | Current schema requires a dining table | Table-first UX | No | Takeaway/delivery/counter cannot be tableless | Add service type + nullable table with migration/backfill compatibility |
| Seat number | No | No | No | Missing | Add optional line seat number |
| Void after KOT | No complete audited flow | No | No | Missing | Add explicit audited mutation; never delete dispatched history |
| Refire | No | No | No | Missing | Add new production linked to original item |
| Inventory reservation | No confirmed reservation layer | Not client-owned | No | Current inventory is consumption-centric | Add reservation granularity tied to production items |
| Consumption per production/KOT item | No | Not client-owned | No | `serve()` calls order-level `consumeOrder()`; old idempotency can block later rounds | Move commitment to production granularity |
| Waste | Existing stock movement/accounting can be reused | Not client-owned | No final contract | Missing restaurant waste workflow | Extend stock movement semantics |
| Web POS incremental order UI | Existing take-order UI exists | N/A | N/A | It creates a new order and does not separate sent/unsent rounds | Extend existing page rather than create parallel POS |
| Flutter active-order reopening | Yes, table screen reopens existing active order | Yes | Yes | After submission the order screen becomes read-only | Keep reopening; allow later unsent lines |
| Flutter offline-first / LAN-first | Yes server sync | Yes and recently hardened with separate LAN/cloud cursors | Yes | Must be preserved | Extend current outbox/sync contracts only |
| Billing one final bill | Existing billing/POS | Consumes order | Existing order relation | Multi-round must not duplicate bill lines | Reuse bill architecture and regression-test |
| Daily closing | Existing | N/A waiter client | Existing | Must remain unchanged | Regression-test only unless defect found |

## Confirmed current implementation details

### Laravel

- `OrderService::addItem()` currently rejects every non-draft order.
- `OrderService::submit()` retries dispatch for submitted/preparing/ready states.
- `KitchenService::dispatch()` returns the order's existing tickets when any ticket already exists, which makes the current design intentionally one-shot.
- `kitchen_tickets` has a unique `(order_id, kitchen_station_id)` key, preventing later station tickets for the same order/station.
- `kitchen_ticket_items` has a unique `order_item_id`, preventing deliberate extra production/refire and making quantity-level incremental production impossible.
- Kitchen transitions operate on a full ticket and bulk-update every child item.
- Serving calls `InventoryService::consumeOrder()`; production consumption is therefore currently order-level rather than round/item-level.
- General Kitchen fallback and branch-specific menu routing already exist and should be retained.
- Existing API routes already cover orders, submit, kitchen ticket start/ready, serve, billing, inventory, purchasing, recipes and sync.
- Existing mobile sync already provides durable mutation replay through `sync_mutations` and monotonically increasing `sync_changes`.

### Flutter

- Existing SQLite/outbox architecture is offline-first and must be extended, not replaced.
- `TableHomeScreen` already reopens an active order for an occupied table.
- `OrderScreen` sets `draft = order['status'] == 'draft'`; add-item and submit controls disappear after first submission.
- `LocalDatabase.addDraftItem()` enforces draft-only local editing.
- `LocalDatabase.submitDraftOrder()` updates only `draft -> submitted_pending_sync`.
- `OfflineOrderRepository` already generates stable UUID mutation IDs and line IDs.
- `SyncEngine` already preserves LAN-first automatic mode, cloud fallback and independent LAN/cloud cursors.

## Existing work to reuse

- SaaS tenancy, authentication, roles and subscriptions
- Branches, dining areas and tables
- Menu and menu images
- Order/event infrastructure
- Kitchen stations and routing
- General Kitchen fallback
- KOT/KDS base tables and audit events
- Billing, discounts, split payments, cashier sessions and daily closing
- Inventory items/balances/movements/valuation, recipes, purchasing and accounting
- Flutter secure session, signed offline lease, SQLite, outbox, conflicts and sync
- Desktop LAN/cloud interoperability contract already implicit in current bootstrap/sync payloads

## Immediate schema blockers

1. `orders.dining_table_id` is non-nullable.
2. `kitchen_tickets(order_id,kitchen_station_id)` is unique.
3. `kitchen_ticket_items.order_item_id` is unique.
4. No KOT dispatch-round entity.
5. No immutable workflow-settings snapshot on a dispatch round.
6. No round-level idempotency identity.
7. No sent/unsent production quantity tracking on an order line.
8. No item-level kitchen transition timestamps/audit identity.
9. No reservation layer between stock availability and permanent consumption.

## Desktop coordination

The branch `feat/kot-realignment-desktop` exists, but `desktop/docs/web-mobile-sync-handoff.md` is not yet committed there or on `main`.

Until that file appears:
- shared changes stay additive;
- existing field/status names are preserved where practical;
- every new shared field/status/mutation is documented in `docs/kot-kds-shared-contract.md`;
- Desktop implementation is not modified from this branch.
