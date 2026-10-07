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

## Shared KOT dispatch-round fields

The current `feat/kot-realignment-web` contract uses these canonical names:

- `sequence`
- `submitted_by_user_id`
- `client_mutation_id`
- `kot_number`
- `priority`
- `workflow_snapshot`
- `service_context` (nullable)
- `course_context` (nullable)
- `sent_at`

Desktop now emits these canonical names in KOT-round snapshots. It also keeps the older Desktop aliases `round_number`, `display_number`, `business_date`, `client_dispatch_id`, `dispatched_at`, and `workflow` additively so existing Desktop/LAN consumers are not broken.

Desktop internal property/column names do not need a destructive rename as long as the shared payload preserves the canonical Web/Flutter semantics.

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

The current Web contract defines these canonical persisted production states:

- `held`
- `queued`
- `active`
- `preparing`
- `ready`
- `completed`
- `voided`
- `cancelled`

Desktop uses the same meanings. `pending` remains an unsent order-line state rather than dispatched production.

Desktop `expo` is still additive execution detail because the current Web model does not define `expo` as a canonical persisted production state. The UI-only label `DELAYED` remains derived from elapsed time and is never persisted as another status.

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

## Handoff note

At Web head `83d9813dcc00ac0a4443d74214d02b0ba8a9e1c8`, Queue-off `active` and configurable Queue/Preparing snapshots are part of the Web realignment contract. Flutter mobile sync still preserves the legacy `order.submit` mutation, so Desktop continues to accept it alongside additive `order.kot.send`.

Desktop-only operational actions such as Expo pass, recall, waste and active-order item movement remain additive until the Web/Flutter branch explicitly adopts them. They do not rename or replace existing shared mutations/states.

Before any future shared-contract change, re-read this file **and** re-inspect the current Web/Flutter branch head; this snapshot can become stale as the parallel branch advances.
