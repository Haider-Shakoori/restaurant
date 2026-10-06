# Local KOT, KDS and thermal printing

The Windows desktop remains the operational authority for kitchen execution while the restaurant is using the local network.

## Flow

Android waiter -> Desktop LAN order -> station routing -> KOT ticket(s) -> KDS / thermal printer -> Preparing -> Ready -> Android/Cashier pull stream.

## Station routing

The cloud bootstrap provides:

- active kitchen stations per branch
- menu-item -> kitchen-station routes

If an item has no route for its branch, the desktop creates/uses a local `GENERAL` station.

Submitting the same order again does not duplicate KOT tickets.

## KDS endpoints

Authenticated kitchen/manager/owner terminals can use:

- `GET /api/v1/kitchen/tickets?station_id=...`
- `POST /api/v1/kitchen/tickets/{ticketId}/start`
- `POST /api/v1/kitchen/tickets/{ticketId}/ready`

Ticket states:

`queued -> preparing -> ready -> completed`

Completion remains tied to the later serve/cashier workflow so local semantics stay aligned with Laravel.

## Thermal printer routing

Each station can have a desktop-local Windows printer binding:

`PUT /api/v1/kitchen/stations/{stationId}/printer`

Body:

```json
{
  "printer_name": "Kitchen Thermal",
  "copies": 1,
  "enabled": true
}
```

Printer mappings are deliberately local because Windows printer names and USB/LAN printer installation are machine-specific.

## Durable print queue

KOT acceptance never depends on printer health.

1. KOT is committed to SQLite.
2. A print job is committed for stations with an enabled printer binding.
3. The Windows print worker sends RAW ESC/POS output through the Windows spooler.
4. Failed jobs remain in SQLite and retry automatically.
5. A PC/printer interruption does not lose the KOT.

## Status synchronization

Starting the first KOT moves the order to `preparing`.

When every station ticket is `ready` or `completed`, the order becomes `ready`.

Both ticket and order updates are written to the local change cursor so waiter Android devices receive them through the existing pull channel without Internet access.
