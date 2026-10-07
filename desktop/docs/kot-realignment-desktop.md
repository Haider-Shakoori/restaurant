# Desktop KOT/KDS realignment

## Scope

PR #68 realigns **Windows Desktop only**. The Laravel web/SaaS and Flutter mobile code are intentionally not modified in this branch.

## Architecture mapping

The existing architecture is extended in place:

| Existing Desktop area | Realignment responsibility |
| --- | --- |
| `LocalSyncService` | open order, add items, multi-KOT send, courses, void/cancel, idempotent mutation contract |
| `LocalKitchenService` | station routing, per-item state machine, Expo gate, re-fire, KOT snapshots, printer payload |
| `LocalInventoryService` | recipe reservation and production-level exactly-once consumption |
| `LocalCashierService` | serve/bill/payment plus table transfer, conservative merge and safe unsent-line split |
| `LocalRestaurantSettingsService` | shared Queue/Preparing/Expo/Courses/sound/aging/void settings |
| `CloudReconciliationService` | shared restaurant-settings reconciliation |
| SQLite persistence | KOT rounds, daily sequence, workflow snapshots, reservation/consumption/refire history |
| WPF operational views | item-card KDS, restaurant workflow settings, service-type POS, table operations, kitchen reports |

No parallel KOT engine, inventory ledger, printing subsystem or duplicate order model was created.

## Workflow matrix

Queue and Preparing are independent:

- Queue ON + Preparing ON: queued -> preparing -> ready
- Queue ON + Preparing OFF: queued -> ready
- Queue OFF + Preparing ON: active -> preparing -> ready
- Queue OFF + Preparing OFF: active -> ready

Expo optionally inserts `expo` before `ready`.

## Historical migration

Existing local KOT tickets are preserved and backfilled as historical Round 1 records. Original ticket IDs/timestamps/statuses are not fabricated or replayed. Existing KitchenTicketItem uniqueness is relaxed only enough to support an auditable re-fire referencing the same original order line.

## Inventory commitment point

The documented rule is:

**KOT send reserves. Production commits.**

Preparing-enabled workflows commit on START. Preparing-disabled workflows commit on the first actual production transition; Queue OFF + Preparing OFF can commit when production becomes immediately active. Serve is a catch-up/idempotency boundary, not the primary deduction event.

## Compatibility

Shared payloads expose the Laravel-aligned round concepts where already known:

- `round_number`
- `display_number`
- `business_date`
- `client_dispatch_id`
- `dispatched_at`

Desktop also includes its local workflow snapshot so clients can render the correct stage controls for that specific round.

## Regression coverage

`KotRealignmentTests` covers:

- multiple KOT rounds
- retry/idempotency
- all four Queue/Preparing combinations
- settings changes between rounds
- settings persistence/restart and cloud-outbox creation
- inventory reserve/consume exactly once
- Expo gating
- post-KOT manager void and reservation release
- re-fire as additional production without an additional bill line
- course firing
- non-table service type
- safe unsent-line split

Existing cashier, daily closing, inventory, kitchen, local ordering, reconciliation, terminal, licensing and release tests remain in the same Desktop solution.
