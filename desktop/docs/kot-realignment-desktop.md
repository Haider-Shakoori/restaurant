# Desktop KOT/KDS realignment

## Scope

PR #68 realigns **Windows Desktop only**. The Laravel web/SaaS and Flutter mobile code are intentionally not modified in this branch.

## Architecture mapping

The existing architecture is extended in place:

| Existing Desktop area | Realignment responsibility |
| --- | --- |
| `LocalSyncService` | open order, add items, multi-KOT send, courses, void/cancel, idempotent mutation contract |
| `LocalKitchenService` | station routing, per-item state machine, Expo gate, recall, waste, re-fire, KOT snapshots, printer payload |
| `LocalInventoryService` | recipe reservation and production-level exactly-once consumption |
| `LocalCashierService` | serve/bill/payment plus table transfer, active-target safe merge, unsent-item move and safe split |
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
- recall without duplicate inventory consumption
- manual and cancellation-driven waste without inventory return
- course firing
- non-table service type and full takeaway settlement
- active-target merge with immutable prior KOT history
- safe unsent-line move and split
- full Desktop golden path: multiple KOT rounds -> production -> ready -> serve -> bill -> payment -> table release

Existing cashier, daily closing, inventory, kitchen, local ordering, reconciliation, terminal, licensing and release tests remain in the same Desktop solution.


## Final implementation report

### Reused

The existing Desktop architecture remains authoritative:

- `LocalSyncService` mutation/outbox/pull-cursor model
- `LocalKitchenService` and durable print queue
- `LocalInventoryService` balances, weighted-average valuation, recipes and stock movements
- `LocalCashierService` billing/payments/settlement
- `LocalOperationsControlService` audit events
- SQLite/EF Core persistence and migration-safe startup upgrades
- WPF shell, navigation, theme resources and LAN/offline hosting

No second KOT, inventory, billing or synchronization subsystem was introduced.

### Modified

- incremental multi-KOT dispatch on one open order
- per-round daily display numbering and workflow snapshots
- item-level Queue/Preparing/Expo execution
- modifier min/max validation and price deltas
- service types, seats, courses, allergy/kitchen instructions and rush priority
- reservation at KOT send and exactly-once production consumption
- reasoned void/cancel, recall, waste and re-fire
- active-order transfer/merge/move/split safeguards
- item-card KDS with hot reload, sound, aging and derived Delayed label
- workflow settings persistence/cloud reconciliation
- kitchen performance reporting
- billing settlement for non-table service types

### New persisted Desktop data

Migration-safe additions include:

- KOT rounds and branch/business-date KOT counters
- round/priority/course/seat/modifier/allergy/instruction metadata
- workflow settings
- inventory reservations and production-level consumption references
- service type/branch context on orders
- refire linkage/reason
- recall time/reason/user
- waste time/reason/user

Legacy local KOTs are backfilled as historical Round 1 records without destructive reset.

### Desktop-local API additions

The Desktop LAN host adds/extends:

- item-level kitchen start/ready
- Expo pass
- re-fire
- restaurant workflow settings
- safe order split endpoint
- kitchen performance report

The existing shared `order.submit` mutation remains supported. Desktop also accepts `order.kot.send` as an additive alias.

Recall, waste and active-order item move are currently Windows Desktop operational actions and intentionally were not added as new Web/Flutter shared mutations in this branch.

### Shared-contract handoff

See `desktop/docs/web-mobile-sync-handoff.md`.

At the inspected Web/Flutter head, the shared round names remain `round_number`, `display_number`, `business_date`, `client_dispatch_id`, `dispatched_at`; mobile submission remains `order.submit`; baseline kitchen statuses remain `queued / preparing / ready / completed / cancelled`.

Desktop `active` and `expo` are additive execution states. `DELAYED` is UI-derived only.

### Remaining cross-app limitation

This branch intentionally does **not** modify Laravel or Flutter. The parallel Web/Flutter branch must explicitly adopt any additive Desktop execution states/operations it wants to exchange through the shared cloud contract. Until then, core names are preserved and Desktop-only additions remain additive rather than replacing the existing Web/Flutter vocabulary.

### Acceptance gate

PR #68 is ready to leave draft only after the current `Restaurant Desktop CI` head completes Build, Test, publish/package validation and startup smoke tests successfully. Generic repository CI may also run Laravel/Flutter checks because of repository workflow triggers, but this branch contains no non-Desktop file changes.
