# Batch 8 — Cashier/POS, Payments and Daily Closing

Batch 8 extends the served-order workflow into settlement and auditable end-of-day reconciliation.

## Delivered

- cashier sessions per branch and cashier;
- opening cash, expected cash, declared cash and variance;
- one open cashier session per cashier;
- immutable bill and bill-line snapshots from served orders;
- fixed and percentage discounts before payment;
- split payments across cash, card, bank, mobile money and other methods;
- client payment IDs for retry-safe payment posting;
- overpayment prevention;
- automatic order/table settlement only after the bill reaches zero balance;
- zero-balance complimentary bills through a full discount;
- daily closing per branch/business date;
- daily close validation that blocks open cashier sessions and unsettled bills;
- immutable versioned daily-closing snapshots;
- audited daily-close reopen and re-finalize flow.

## Financial boundaries

Money uses integer minor-unit arithmetic. The POS path does not use binary floating-point arithmetic for bill, discount, payment or variance calculations.

Bills snapshot order item names, quantities and prices. Later menu changes do not rewrite a historical bill.

A posted payment is a separate tenant financial record. Retrying a mobile/client payment with the same client_payment_id returns the original payment and does not double-post it.

A table remains occupied during partial settlement. Only a fully paid or zero-balance bill closes the order and returns the table to available.

## Daily closing

Closing is branch-specific and uses the restaurant's configured Asia/Kabul timezone.

Before finalization:

- all cashier sessions for the business date must be closed;
- all bills through that date must be settled.

Each finalization creates a new immutable snapshot containing bill/payment/session counts, gross sales, discounts, net sales, payment-method totals, expected cash, declared cash and cash variance.

Reopening changes only the closing control state and records an audit event. Existing snapshots are retained. Re-finalizing creates the next snapshot version.

## Tenant API

Cashier/management routes:

- GET /api/v1/cashier/sessions
- POST /api/v1/cashier/sessions
- POST /api/v1/cashier/sessions/{cashierSession}/close
- GET /api/v1/pos/bills
- POST /api/v1/orders/{order}/bill
- GET /api/v1/pos/bills/{bill}
- POST /api/v1/pos/bills/{bill}/discount
- POST /api/v1/pos/bills/{bill}/payments
- GET /api/v1/daily-closings
- GET /api/v1/daily-closings/{dailyClosing}
- POST /api/v1/daily-closings/finalize

Owner/admin/manager only:

- POST /api/v1/daily-closings/{dailyClosing}/reopen

## Batch boundary

Batch 8 records operational restaurant money and reconciliation. It does not yet post double-entry accounting journals or inventory consumption.

Batch 9 adds inventory, purchasing and recipes. Batch 10 will connect financial source documents to the accounting ledger and reporting layer.
