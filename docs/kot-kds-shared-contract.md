# BusinessOS Restaurant — KOT/KDS shared contract

Status: implemented Laravel/Web + Flutter contract for the KOT/KDS realignment.

This document is the shared contract for Laravel cloud/Web and Flutter mobile. Desktop must mirror the same semantics. It is intentionally additive to the existing API and synchronization model.

## Domain invariants

- Order and KOT are different concepts.
- One operationally open order may receive many KOT dispatch rounds.
- Each SEND KOT creates exactly one logical dispatch round.
- One dispatch round may create multiple station tickets.
- One station ticket contains production items routed to one kitchen station.
- A production item has its own lifecycle.
- Previously dispatched production is never resent unless an explicit REFIRE creates new production.
- All legitimate order quantities remain on one final bill unless an explicit bill split operation is used.

## IDs and idempotency

- Existing ULID/UUID primary IDs remain canonical.
- Client mutations use stable mutation IDs.
- A SEND KOT mutation ID identifies one dispatch round.
- Replaying an identical mutation returns the original round/tickets/items.
- Reusing a mutation ID with different content is rejected.
- A dispatch round stores the client mutation identity so non-mobile/web retries can use the same guarantee.

## Implemented additive entities

### kot_dispatch_rounds

Required fields:
- id
- order_id
- sequence
- submitted_by_user_id
- client_mutation_id
- priority: normal|rush
- sent_at
- workflow_snapshot JSON
- service_context JSON nullable
- course_context JSON nullable
- timestamps

Constraints:
- unique(order_id, sequence)
- unique(client_mutation_id) within tenant, or equivalent safe scope

The workflow snapshot freezes operational settings used by that round so later settings changes do not rewrite history.

### kitchen_tickets

Existing entity, extended with:
- kot_dispatch_round_id
- human_kot_number

Existing primary ID remains unchanged.

### kitchen_ticket_items

Existing entity becomes the production-item snapshot and is extended as needed with:
- source_order_item_id
- source_quantity
- state
- started_at
- ready_at
- completed_at
- voided_at
- refire_of_kitchen_ticket_item_id nullable
- course_number nullable
- seat_number nullable
- modifiers_snapshot JSON nullable
- critical_instruction nullable
- reservation/consumption linkage where appropriate

A source order item may legitimately have multiple production items over its lifetime due to later quantity dispatch or refire. Therefore the historical unique(order_item_id) constraint cannot remain the sole production identity.

## Canonical kitchen states

Canonical persisted production states:
- held
- queued
- active
- preparing
- ready
- completed
- voided
- cancelled

`pending` remains appropriate for an unsent order line and is not itself dispatched production.

Ticket/order aggregate state should be derived from child production state wherever practical.

## Shared restaurant settings

Canonical names:
- kitchen_queue_enabled
- preparing_stage_enabled
- expo_enabled
- courses_enabled
- kot_sound_enabled
- kitchen_warning_minutes
- kitchen_late_minutes
- require_manager_approval_post_kot_void
- negative_stock_policy

These names must be used consistently by API, bootstrap, Flutter cache and Desktop.

## Workflow modes

### Queue ON / Preparing ON
SEND -> queued -> preparing -> ready

Inventory:
SEND reserves expected ingredients.
START commits permanent recipe consumption once.

### Queue ON / Preparing OFF
SEND -> queued -> ready

Inventory:
SEND reserves expected ingredients.
Consumption commits once at the deterministic acceptance point used to leave queued state for ready. No synthetic preparing/start event is recorded.

### Queue OFF / Preparing ON
SEND -> active -> preparing -> ready

Inventory:
SEND makes production active/available.
START commits consumption once.

### Queue OFF / Preparing OFF
SEND -> active -> ready

Inventory:
Consumption commits automatically once when production becomes active at dispatch. No synthetic preparing/start event is recorded.

## Settings changes

- Settings changes never recreate a tenant or reset data.
- Existing production keeps its original workflow snapshot.
- New rounds use current settings.
- Settings changes never redispatch an old round.
- Settings changes never duplicate reservation or consumption.

## Order editability

New unsent items may be added while the order is operationally open.

Allowed order states for new lines are expected to include:
- draft
- submitted
- preparing
- ready
- served, only while financially open and business rules permit

Blocked:
- closed
- cancelled
- fully settled/financially locked states according to existing billing rules

This rule belongs in one domain service/policy and must not be reimplemented inconsistently by each controller/client.

## Incremental dispatch

For each order line, the system must be able to distinguish:
- total ordered quantity
- already-dispatched production quantity
- currently unsent quantity
- voided/cancelled quantity where applicable

SEND KOT dispatches only eligible unsent quantity.

## Human KOT number

API exposes both:
- internal ticket ID
- human KOT number

Human format begins `KOT-0001` and is generated concurrency-safely in the final agreed business-day/branch scope.

## Station routing

Existing branch/menu-item route semantics remain canonical.
Unrouted items use the existing General Kitchen fallback.

## Modifiers / notes / critical instructions

- Structured modifiers use existing modifier infrastructure.
- Price-affecting modifiers affect order/bill totals.
- KOT stores immutable modifier snapshots.
- Free-text ordinary notes remain separate.
- Critical/allergy instructions are a distinct field and must be visually emphasized in KDS clients.

## Courses

When `courses_enabled=false`, no course workflow is required.

When enabled:
- order lines may carry course number/context;
- a held course is not kitchen-active;
- FIRE creates/activates eligible production for that course according to the same dispatch/state rules;
- Flutter may fire a course only when authorized.

## Service types

Canonical values:
- dine_in
- takeaway
- delivery
- counter

Only `dine_in` requires a table.
Tableless services use token/order/delivery references rather than fake dining-table IDs.

## Seats

`seat_number` is optional order-line context.
Seat assignment is independent from payment splitting.

## Void / cancellation

Before dispatch:
- unsent lines/quantities may be edited or removed without a kitchen cancellation event.

After dispatch:
- never delete historical production;
- explicit void/cancel records reason, actor, timestamp, original quantity, state and approval when required;
- a reservation that has not been consumed is released;
- already-consumed ingredients are not automatically returned to stock.

## Refire

REFIRE:
- points to the original production item;
- preserves original production history;
- creates new production once;
- consumes recipe again;
- may create waste for the failed preparation;
- is retry-safe.

## Inventory

Inventory remains the existing subsystem.

New production flow introduces:
- reservation at the configured commitment boundary;
- reservation release before production;
- permanent consumption at the configured production commitment point;
- no double stock movement;
- refire creates additional consumption;
- cancellation after consumption preserves consumption and may create waste.

Availability:
available = on_hand - reserved

## Expo

Expo is a projection over current production readiness, not a second order model.

It should expose by round/order:
- relevant production item count
- ready count
- all-ready / READY TO SERVE
- station/item readiness details

## Mobile sync

Existing operations remain:
- order.open
- order.item.add
- order.submit

Implemented shared operations include:
- order.kot.send (additive alias; order.submit remains backward compatible)
- order.item.void
- order.item.refire
- order.item.recall
- order.course.fire
- order.table.transfer
- order.item.move
- order.merge

Kitchen item readiness changes are exposed through authorized KDS/API surfaces.

Existing `sync_mutations` replay semantics remain authoritative.

## Bootstrap

Bootstrap should compactly include:
- restaurant workflow settings
- service-type capabilities
- station/menu routing metadata
- course capability
- warning/late thresholds

Do not include large historical KOT datasets in bootstrap.

## Backward compatibility

- Existing one-shot historical KOTs are treated semantically as Round 1.
- No migration should rewrite historical timestamps or pretend disabled workflow stages occurred.
- Existing API fields remain where practical; new fields are additive.
- Existing Flutter local data is upgraded in place.
- Existing Desktop clients may continue consuming old fields until the Desktop branch mirrors this contract.


## Final Web/Flutter adoption note

The Web/Flutter branch now explicitly adopts the Desktop handoff fields and operations that were previously marked additive/Desktop-only:

- service_type / service_reference
- seat_number
- course_number / course_name / held-fire semantics
- structured modifier snapshots and price deltas
- allergy_instructions / kitchen_instructions
- item-level production lifecycle and timestamps
- Queue/Preparing workflow snapshots, including active
- Expo derived readiness
- void / recall / refire / waste audit flows
- production inventory reservation and exactly-once consumption
- negative_stock_policy
- active-order table transfer, unsent-item move/split and draft/unsent merge
- kitchen performance reporting and warning/late aging
- Flutter offline mutations for course, recovery, transfer, move and merge
- premium responsive waiter POS layout for phone/tablet

Desktop-specific persisted expo state remains a Desktop-local execution detail; Web/Flutter treats Expo as a projection and does not add a second canonical persisted production state.
