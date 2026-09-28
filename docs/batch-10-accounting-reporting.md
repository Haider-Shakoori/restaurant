# Batch 10 — Double-Entry Accounting and Management Reporting

Batch 10 connects restaurant source documents to a tenant-local double-entry ledger.

## Delivered

- tenant chart of accounts with protected system accounts;
- immutable journal entries and journal lines;
- source-document and idempotency keys on every journal;
- balanced-journal enforcement;
- manual journals for opening balances and accountant adjustments;
- immutable journal reversal rather than editing/deleting posted entries;
- automatic sales, discount and payment posting;
- automatic GRN inventory / accounts-payable posting;
- perpetual inventory valuation;
- automatic COGS posting from recipe consumption;
- valuation-aware stock-adjustment journals;
- cashier over/short journals;
- operating-expense documents;
- supplier payments and AP reduction;
- trial balance;
- general ledger by account;
- income statement / profit and loss;
- balance sheet;
- receivables;
- supplier payables;
- branch-filtered management summary.

## System chart of accounts

The accounting service creates required system accounts inside each tenant database on demand:

- Cash;
- Card Clearing;
- Bank;
- Mobile Money;
- Other Payment Clearing;
- Accounts Receivable;
- Inventory Asset;
- Accounts Payable;
- Owner Equity;
- Sales Revenue;
- Sales Discounts;
- Cost of Goods Sold;
- Operating Expenses;
- Cash Over / Short;
- Inventory Adjustment.

Restaurants may add additional custom accounts without altering system mappings.

## Source-document posting

Restaurant bill issuance posts Accounts Receivable against Sales Revenue.

Discount changes post only the delta against Sales Discounts and Accounts Receivable.

Payments debit the relevant cash/bank/card/mobile account and credit Accounts Receivable.

A GRN debits Inventory Asset and credits Accounts Payable for the supplier.

Recipe consumption uses perpetual average inventory valuation and posts Cost of Goods Sold against Inventory Asset.

Supplier payments debit Accounts Payable and credit the selected asset/payment account.

Operating expenses debit an expense account and credit the selected payment account.

Cashier closing differences post to Cash Over / Short against Cash.

All source-generated journal postings are idempotent.

## Inventory valuation

Accounting keeps a branch-and-ingredient valuation record containing quantity, inventory value and average unit cost in the ingredient base unit.

GRNs add quantity and purchase value. Recipe consumption reduces quantity and value at the current average unit cost. This makes restaurant COGS available immediately without waiting for end-of-period costing.

Negative operational stock remains allowed. If an ingredient has no cost history yet, its consumption remains operationally visible but produces no invented COGS value.

## Corrections

Posted journals are not edited or deleted. An accountant creates a reversal entry with debit and credit sides swapped. The original entry is marked reversed but retained, and both the original and reversal remain visible in the ledger/audit trail.

## Tenant API

Accounting readers: owner, admin, manager, accountant.

- GET /api/v1/accounting/accounts
- GET /api/v1/accounting/journals
- GET /api/v1/accounting/journals/{journalEntry}
- GET/POST /api/v1/accounting/expenses
- GET/POST /api/v1/accounting/supplier-payments
- GET /api/v1/accounting/reports/trial-balance
- GET /api/v1/accounting/reports/ledger/{chartAccount}
- GET /api/v1/accounting/reports/income-statement
- GET /api/v1/accounting/reports/balance-sheet
- GET /api/v1/accounting/reports/receivables
- GET /api/v1/accounting/reports/payables
- GET /api/v1/accounting/reports/management-summary

Restricted to owner, admin and accountant:

- POST /api/v1/accounting/accounts
- POST /api/v1/accounting/journals
- POST /api/v1/accounting/journals/{journalEntry}/reverse

## Batch boundary

Batch 10 completes the server-side operational and financial core needed before aggressive mobile/offline synchronization.

Batch 11 hardens the Flutter/offline sync protocol, conflict handling, resumable queues and device recovery behavior.

Batch 12 is production subdomain deployment and full end-to-end regression.
