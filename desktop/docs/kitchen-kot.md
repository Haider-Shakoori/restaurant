# Local KOT, KDS and thermal printing

The Windows desktop is the local operational authority for KOT/KDS execution while the restaurant is on LAN or operating through a valid offline lease.

## Multi-KOT production flow

One customer order can create many immutable KOT rounds:

`Order -> KOT Round 1 -> later items -> KOT Round 2 -> later items -> KOT Round 3 ...`

`order.kot.send` dispatches only order lines whose current status is `pending`. Previously dispatched lines are never sent again. Every round stores its own round number, daily display number, branch/business date, dispatch id and a snapshot of the workflow settings that applied at send time.

KOT display numbers restart by branch/business date and are presented as `KOT-0001`, `KOT-0002`, and so on. Station-specific printer jobs append the station code to the document/ticket identity without changing the KOT round identity.

## Routing

Menu items route to their configured kitchen station. Items without an explicit route use the local `GENERAL` station.

Each kitchen item carries the production detail needed by staff:

- quantity
- structured modifier snapshot
- free-text item note
- kitchen instruction
- allergy/special instruction
- seat
- course
- priority (`normal` / `rush`)
- original order-item identity
- re-fire linkage/reason when applicable

## Configurable workflow

Queue and Preparing are independent settings. Each new KOT round snapshots both values so changing settings later never rewrites historical flow.

| Queue | Preparing | New item | Next action |
| --- | --- | --- | --- |
| ON | ON | queued | START -> PREPARING -> READY |
| ON | OFF | queued | READY |
| OFF | ON | active | START -> PREPARING -> READY |
| OFF | OFF | active | READY |

When Expo is enabled, the kitchen READY action sends the item to `expo`. An Owner, Manager or Expo user must pass it before the item/order becomes `ready`.

Courses are optional. Held course items remain out of production until `course.fire` creates a new KOT round for that course.

## Item-level KDS

The Windows KDS renders production as item cards rather than only ticket rows. Cards show KOT/round, station, service/table reference, quantity, modifiers, seat/course, notes, allergy warnings, rush state and a live age timer. Warning/late thresholds and KOT sound are configurable in Restaurant Settings.

Supported actions include:

- start item
- ready/send to Expo
- pass Expo
- recall a recent ready/completed item with a required reason
- mark already-produced food as waste without returning ingredients to stock
- re-fire item
- ticket-level start/ready for backward-compatible clients

The KDS derives staff-facing `NEW`, `ACCEPTED`, `PREPARING`, `READY` and `DELAYED` labels from the underlying workflow and configured age thresholds. `DELAYED` is presentation state only; it is not persisted as another shared kitchen status.

## Recall, waste and re-fire

Recall is allowed for recent ready/completed items while the order is still operational. It moves the production item back into the workflow without deleting the previous completion audit or reversing ingredient consumption. The original completion timestamps remain recoverable through persisted metadata/audit history, and the recall reason/user/time are stored.

Waste is allowed only after production consumption exists. Recording waste never returns ingredients to usable inventory. Post-KOT void/cancel automatically records committed production as waste; unstarted reservations are released instead.

Re-fire creates a new rush production event in a new KOT round while keeping the original order/bill line unchanged. The new kitchen item stores `refire_of_kitchen_item_id` and a reason. Inventory reservation/consumption therefore happens for the additional production exactly once without double-billing the guest.

## Void/cancel

Pre-KOT voids do not create fake kitchen events.

After a KOT has been sent, the configured manager-approval rule applies. If production has not started, the recipe reservation is released. If production was already committed, the audit records that produced stock was **not** silently returned; any stock correction must be an explicit inventory action.

## LAN endpoints

Authenticated kitchen/manager/owner clients can use:

- `GET /api/v1/kitchen/tickets?station_id=...`
- `POST /api/v1/kitchen/tickets/{ticketId}/start`
- `POST /api/v1/kitchen/tickets/{ticketId}/ready`
- `POST /api/v1/kitchen/items/{itemId}/start`
- `POST /api/v1/kitchen/items/{itemId}/ready`
- `POST /api/v1/kitchen/items/{itemId}/expo-pass`
- `POST /api/v1/kitchen/items/{itemId}/refire`
- `GET /api/v1/restaurant/settings`
- `PUT /api/v1/restaurant/settings`

Expo users can read the active kitchen board and pass Expo items. Printer configuration remains Owner/Manager only.

## Thermal printing

Each station can have a desktop-local Windows printer binding:

`PUT /api/v1/kitchen/stations/{stationId}/printer`

KOT acceptance never depends on printer health. The KOT and durable print job are committed to SQLite first; the Windows print worker retries failed spooler jobs. Re-fire tickets are clearly marked.

## Synchronization

Item, ticket, order and KOT-round changes are published to the local change cursor. Workflow settings are persisted in SQLite, exposed in bootstrap/settings payloads, queued to cloud reconciliation when changed locally, and can be applied from the shared cloud settings entity.

This PR changes Desktop only; it does not modify Laravel or Flutter.
