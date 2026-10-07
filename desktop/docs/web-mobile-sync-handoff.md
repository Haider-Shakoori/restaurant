# Desktop ↔ Web/Flutter KOT sync handoff

This file was created from the current repository state after checking both realignment branches.

- Desktop branch: `feat/kot-realignment-desktop`
- Web/Flutter branch inspected: `feat/kot-realignment-web`
- Web/Flutter reference commit at inspection: `83d9813dcc00ac0a4443d74214d02b0ba8a9e1c8`
- Scope of this branch: **Desktop only**. Do not modify Laravel or Flutter here.

## Compatibility rule

The Web/Flutter branch remains the reference for names already shared across clients. Desktop may add local-only implementation detail, but it must not rename or reinterpret the existing shared identifiers.

## Shared mutation names

The current Web/Flutter mobile sync contract defines:

- `order.open`
- `order.item.add`
- `order.submit`

Desktop continues to support all three.

Desktop also accepts `order.kot.send` as an additive/local alias for incremental KOT dispatch. Do **not** remove `order.submit` while Web/Flutter still uses that mutation name.

## Shared KOT round fields

Use these exact names:

- `round_number`
- `display_number`
- `business_date`
- `client_dispatch_id`
- `dispatched_at`

Desktop may also expose `kot_number` for staff display. It represents the human-readable form of `display_number`, for example `KOT-0001`.

Station ticket-number formatting is an implementation/display detail. Web currently formats station tickets with date/branch/display/station tokens; Desktop may use a local printer/display format. The stable shared identities are the round/ticket IDs and the fields above, not the rendered ticket string.

## Shared order states

The current Web branch defines these order states:

- `draft`
- `submitted`
- `preparing`
- `ready`
- `served`
- `billed`
- `closed`
- `cancelled`

Desktop keeps those meanings.

Desktop currently has an additive local `expo` aggregate order state when Expo is enabled. Web/Flutter at the inspected reference does not yet define that order state, so future shared/cloud synchronization must either add Expo explicitly on the Web/Flutter branch or map it at the boundary. Do not rename the existing core states.

## Shared kitchen states

The current Web branch defines:

- `queued`
- `preparing`
- `ready`
- `completed`
- `cancelled`

Desktop keeps these exact meanings.

Desktop has two additive local execution states required by the configurable KDS flow:

- `active` — Queue was skipped for this KOT round
- `expo` — kitchen production is finished but Expo has not passed the item

These are not replacements for the shared states. They are Desktop execution detail until Web/Flutter explicitly adopts them. New Desktop-only work must not introduce another synonym for Queue/Preparing/Ready.

The UI-only label `DELAYED` is derived from elapsed time and the configured late threshold. It is **not** persisted as a new kitchen status.

## Queue / Preparing matrix

Desktop snapshots Queue/Preparing settings on each KOT round:

| Queue | Preparing | Desktop execution |
| --- | --- | --- |
| ON | ON | queued → preparing → ready |
| ON | OFF | queued → ready |
| OFF | ON | active → preparing → ready |
| OFF | OFF | active → ready |

Expo, when enabled, inserts `expo` before `ready`.

Existing rounds retain the settings captured when they were dispatched.

## Incremental dispatch and idempotency

One Order can have multiple KOT rounds.

Each dispatch sends only currently unsent production quantities/lines. A retry with the same dispatch/mutation identity must return the original result and must not:

- create another KOT round,
- create another station ticket,
- reserve ingredients twice,
- consume ingredients twice.

Desktop stores a per-order round number and a per-branch/business-date display number, matching the Web branch naming.

## Modifier payload

Desktop follows the existing menu modifier model and accepts structured selections as:

```json
{
  "modifiers": [
    { "option_id": "..." }
  ]
}
```

Desktop validates active menu/group membership plus min/max selections and applies configured price deltas. Modifier names/options are snapshotted into the order/KOT item so later catalog edits do not change historical production detail.

## Additive Desktop fields awaiting shared adoption

The Desktop implementation also carries operational fields required by the realignment brief:

- `service_type` / `service_reference`
- `seat_number`
- `course_number` / `course_name`
- `priority`
- `allergy_instructions`
- `kitchen_instructions`
- workflow snapshot flags for Queue/Preparing/Expo/Courses
- refire linkage/reason
- recall metadata
- waste metadata

Do not rename these on Desktop. If Web/Flutter later introduces equivalent fields with different names, reconcile deliberately rather than supporting parallel synonyms.

## Local-only operational actions

The current Desktop branch includes local operational actions that are not yet present in the inspected Web/Flutter sync service:

- course fire
- post-KOT item void/cancel rules
- Expo pass
- re-fire
- recall
- production-waste recording
- active-order unsent-item move/split

They must remain additive and auditable. Do not make the existing Web/Flutter mutations incompatible to expose them.

## Inventory boundary

Desktop uses:

**KOT send → reserve recipe stock → actual production → commit consumption**

Serving is an idempotent catch-up boundary, not the primary deduction point.

Cancellation before production releases the reservation. Cancellation/waste after production does not return consumed ingredients. A re-fire creates another production item and therefore another exactly-once reservation/consumption.

## Sync/offline invariants

Across Desktop/Web/Flutter:

- mutation/dispatch IDs remain idempotent;
- retries must not duplicate KOTs or stock usage;
- pull/change cursors remain monotonic;
- printer failure cannot invalidate an already accepted KOT;
- Internet failure cannot erase accepted local orders;
- reconnect must reconcile rather than create a second logical order/KOT;
- historical IDs/timestamps/events are not destructively rewritten.

## Current Web contract delta requiring a later Desktop phase

The newer Web branch has also moved the KOT dispatch-round contract to `sequence`, `client_mutation_id`, `kot_number`, `workflow_snapshot`, `service_context`, `course_context`, and `sent_at`, and now treats `active` as canonical kitchen state. Desktop still carries its earlier round aliases internally. That round-contract migration is deliberately **not** part of the Restaurant Settings / negative-stock phase in this commit and should be handled as the next isolated compatibility phase.

## Handoff note

At the inspected Web/Flutter reference, the Web kitchen service still uses the baseline fixed queued → preparing → ready → completed flow and Flutter mobile sync still names submission `order.submit`. The Desktop branch therefore keeps those core names intact and treats Queue-off `active`, Expo, and the newer operations as additive until the Web/Flutter branch adopts them.

Before any future shared-contract change, re-read this file **and** re-inspect the current Web/Flutter branch head; this snapshot can become stale as the parallel branch advances.
