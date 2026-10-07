# Local restaurant ordering

The Windows desktop host is the local operational authority while the restaurant is using LAN/Wi-Fi or a valid signed offline lease.

## Request path

Waiter/mobile terminal -> `http://<desktop-lan-ip>:8787/api/v1/sync/*` -> Desktop SQLite -> kitchen/cashier.

Accepted local order mutations do not require public-cloud availability.

## Supported service types

`order.open` supports:

- `dine_in` — requires `dining_table_id`
- `takeaway`
- `delivery`
- `counter`

Non-table services require `branch_id` and never create a fake dining table. `service_reference` can carry the pickup/delivery/counter reference.

## Incremental order contract

The Desktop accepts idempotent mutations including:

- `order.open`
- `order.item.add`
- `order.kot.send` (and legacy-compatible `order.submit`)
- `course.fire`
- `order.item.void`
- `order.cancel`

An order remains open for later additions after the first KOT. `order.kot.send` sends only currently unsent `pending` lines. Mutation IDs are persisted per device so retries return the original result instead of creating duplicate production.

## Order-item detail

`order.item.add` supports quantity plus:

- structured `modifiers` containing modifier-option IDs
- `notes`
- `kitchen_instructions`
- `allergy_instructions`
- `seat_number`
- `course_number` / `course_name`
- `held`
- `priority` (`normal` or `rush`)

Modifier selection is validated against the menu item's configured modifier groups and the selected price deltas are included in the line price.

## Courses

When Courses is enabled, an item may be added as `held` with a course number. `course.fire` turns the held lines for that course into the next production/KOT round. The fire mutation is idempotent.

## Voids and cancellation

A reason is required.

- before KOT: normal permitted ordering roles may void/cancel
- after KOT: a manager/owner/admin must perform the action when the manager-approval setting is enabled
- unstarted recipe reservations are released
- already committed production is not automatically returned to inventory

## Table/order operations

Desktop cashier/floor controls support:

- table transfer
- conservative draft-order merge
- safe split of unsent pending/held lines to another available table
- bill allocation/split payments

Sent KOT items are deliberately not moved by split/merge because production history must remain immutable.

## Credential trust and pull cursor

The mobile terminal keeps its existing BusinessOS bearer/device credentials. The Desktop validates a new pairing against cloud when possible and caches credential hashes plus staff/device identity for later LAN/offline operation.

Desktop changes use an incrementing SQLite cursor. Waiters receive their own order mutations while shared menu/table/settings changes remain visible.

## Control-plane proxy

The narrow cloud proxy remains limited to activation/authentication/lease routes. Restaurant orders are not made cloud-dependent.

This PR changes Desktop only; it does not modify Laravel or Flutter.
