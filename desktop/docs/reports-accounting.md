# Desktop Batch 13 — Reports and accounting surfaces

Batch 13 turns the desktop local SQLite operational data into management reporting without moving authority to the cloud.

## Delivered

- period sales summary: gross sales, discounts, net sales, collections, outstanding balance, COGS, gross profit and margin;
- daily sales/profit trend;
- payment-method and top-item breakdowns;
- latest-version daily-closing history and cash variance;
- inventory valuation, average cost, quantity and low-stock view;
- accounting summary for payment clearing, receivables, sales, discounts, COGS and inventory snapshot;
- WPF reporting workspace with dedicated tabs and AFN presentation;
- role-protected local report endpoints for owner, manager, accountant and auditor;
- period validation and regression tests.

## Accounting boundary

The accounting surface is a derived management view. It does not create a second ledger and does not mutate bills, payments, stock movements, valuation or daily-closing snapshots.

## Local-first behavior

Reports are generated from desktop SQLite. Cloud connectivity is not required. Android ordering, KOT/KDS, cashier and inventory continue through the Desktop LAN host during degraded or isolated-local operation while the signed offline lease is valid.

## Endpoints

- GET /api/v1/reports/summary
- GET /api/v1/reports/payments
- GET /api/v1/reports/top-items
- GET /api/v1/reports/closings
- GET /api/v1/reports/sales-trend
- GET /api/v1/reports/accounting
- GET /api/v1/reports/inventory

## Next batch

Batch 14 covers backup/restore, updater and diagnostics.
