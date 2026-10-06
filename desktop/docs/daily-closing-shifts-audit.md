# Daily closing, staff shifts and audit controls

Batch 9 adds the local operational controls required to finish a restaurant business day while the Windows desktop is the LAN authority.

## Daily closing

The local closing flow mirrors the Laravel daily-closing model:

- one closing per branch and business date
- open / finalized / reopened states
- immutable versioned snapshots
- management-only reopen
- re-finalizing a reopened date creates the next snapshot version

A closing snapshot stores:

- bill count
- payment count
- cashier-session count
- gross sales
- discounts
- net sales
- total payments
- cash/card/bank/mobile-money/other payment totals
- expected cash
- declared cash
- cash variance

## Closing safeguards

Finalization is rejected while the selected branch/date still has:

- an open cashier session
- an open bill
- an open staff shift that started on or before the business date

This prevents a day from being finalized while restaurant operations are still active.

## Staff shifts

Waiters, kitchen staff, cashiers and management can open their own local shifts.

A shift records:

- branch
- staff identity and role
- start/end timestamps
- break minutes
- worked minutes
- closing note
- open/closed status

A staff member cannot have more than one open shift. The shift owner can close their own shift; management may close a shift when necessary.

## Audit trail

Sensitive operational actions append an immutable local audit event with:

- unique event ID
- monotonic sequence/cursor
- category and event type
- actor user ID, name and role
- branch
- entity type and ID
- structured payload
- UTC timestamp

Batch 9 audits at least:

- shift opened / closed
- cashier session opened / closed
- bill issued
- discount applied
- split configuration
- payment posted
- order transfer
- draft-order merge
- receipt-printer configuration
- daily closing finalized
- daily closing reopened

Reopen requires a reason and never overwrites the previous finalized snapshot.

## LAN endpoints

- `POST /api/v1/shifts`
- `POST /api/v1/shifts/{shiftId}/close`
- `GET /api/v1/shifts/active`
- `GET /api/v1/daily-closings`
- `POST /api/v1/daily-closings/finalize`
- `POST /api/v1/daily-closings/{closingId}/reopen`
- `GET /api/v1/audit?cursor=0&limit=100&category=...`

Management roles are required for audit access and reopening finalized business dates.
