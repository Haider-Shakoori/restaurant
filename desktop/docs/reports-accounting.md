# Desktop Batch 13 — Reports and accounting surfaces

Batch 13 adds local-first management reporting without changing operational authority.

## Delivered

- Period/branch financial summary: gross sales, discounts, net sales, payments, COGS and gross profit.
- Current local inventory valuation and daily-closing cash variance.
- Payment-method breakdown and top-selling item report.
- Finalized daily-closing history using the latest immutable snapshot version.
- Authenticated local report endpoints for owner, manager, accountant and auditor roles.
- Native WPF Reports & Accounting dashboard.
- Regression tests proving reports are derived from local SQLite and remain independent of cloud availability.

## Architecture

Reports are read models over the same local SQLite records used by POS, daily closing and inventory. Android ordering remains Android -> Desktop LAN -> SQLite. Reporting never becomes a cloud dependency and does not mutate operational records.

Cloud reconciliation continues independently through the durable Batch 11 outbox. A temporary WAN outage therefore does not prevent the restaurant from viewing locally available operational reports.

## Endpoints

- GET /api/v1/reports/summary
- GET /api/v1/reports/payments
- GET /api/v1/reports/top-items
- GET /api/v1/reports/closings

All endpoints accept branch_id, from and to. top-items additionally accepts limit.
