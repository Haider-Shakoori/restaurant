# Local Cashier / POS

The Windows desktop host is the source of truth for in-restaurant cashier operations while LAN mode is active.

## Operational flow

Kitchen Ready -> Serve -> Issue Bill -> Discount (optional) -> Split allocations (optional) -> One or more payments -> Paid -> Order Closed -> Table Available -> Receipt print queue.

## Cashier sessions

A cashier opens a branch session with opening cash. Only one open session per cashier is allowed.

Closing a session calculates:

- opening cash
- posted cash payments
- expected cash
- declared cash
- variance

## Billing

A bill can only be issued after the order is served.

The local bill mirrors the cloud financial shape:

- subtotal
- discount type/value/amount/reason
- total
- paid amount
- balance due
- bill lines
- posted payments

Fixed and percentage discounts are allowed only before the first payment, matching Laravel behavior.

## Split bill behavior

The desktop keeps one canonical bill for cloud compatibility and creates local split allocations underneath it.

Each split has its own amount, paid amount, balance and status. Payments can optionally target a split while still posting to the canonical bill. All split allocations must be fully paid before the canonical bill can close.

This avoids creating a second incompatible billing model for later cloud reconciliation.

## Payment methods

Supported methods match Laravel:

- cash
- card
- bank
- mobile_money
- other

Client payment IDs are idempotent. A repeated client payment ID returns the existing payment; reusing it on another bill is rejected.

## Table transfer and merge

Active unbilled orders can move to an empty active table.

Order merge is deliberately limited to two draft orders. Once either order has been submitted to the kitchen, merge is rejected so station tickets and KOT history cannot be corrupted.

## Receipt printing

Receipt printer configuration is local to the Windows machine.

Paid bills automatically queue a receipt when an enabled receipt printer is configured. Manual reprint uses the same durable SQLite queue.

KOT and customer receipt queues are independent. A failed kitchen printer does not block receipts, and a failed receipt printer does not block KOT processing.

## LAN endpoints

- `GET /api/v1/pos/bills`
- `POST /api/v1/cashier/sessions`
- `POST /api/v1/cashier/sessions/{sessionId}/close`
- `POST /api/v1/orders/{orderId}/serve`
- `POST /api/v1/orders/{orderId}/bill`
- `POST /api/v1/pos/bills/{billId}/discount`
- `POST /api/v1/pos/bills/{billId}/splits`
- `POST /api/v1/pos/bills/{billId}/payments`
- `POST /api/v1/orders/{orderId}/transfer`
- `POST /api/v1/orders/{targetOrderId}/merge`
- `PUT /api/v1/pos/receipt-printer`
- `POST /api/v1/pos/bills/{billId}/receipt`
