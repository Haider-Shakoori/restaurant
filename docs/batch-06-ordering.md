# Batch 6 — Tables, Menu and Waiter Ordering

Batch 6 starts the restaurant operational golden path on top of the already-proven tenant, subscription and offline-license foundations.

## Scope delivered

- tenant user role field and server-side role middleware;
- tenant API login/logout with Sanctum tokens;
- compact /api/v1/bootstrap contract for mobile clients;
- branches, dining areas and dining tables;
- table state: available, occupied, reserved, disabled;
- menu categories and menu items with AFN decimal pricing;
- draft orders and immutable menu-name/price snapshots on order lines;
- one active order per table enforced transactionally;
- client-generated order and line identifiers for retry-safe offline/mobile writes;
- exact two-decimal money multiplication without floating-point arithmetic;
- order event/audit history;
- waiter ownership enforcement;
- management/cashier operational access;
- order submission transition ready for Batch 7 KOT/KDS generation.

## Tenant API

Public tenant endpoints:

- POST /api/v1/auth/login
- GET /api/v1/health
- subscription and license endpoints from earlier batches

Authenticated + active-subscription endpoints:

- POST /api/v1/auth/logout
- GET /api/v1/bootstrap
- GET /api/v1/menu
- GET /api/v1/tables
- GET /api/v1/orders
- POST /api/v1/orders
- GET /api/v1/orders/{order}
- POST /api/v1/orders/{order}/items
- POST /api/v1/orders/{order}/submit

## Offline/idempotency contract

The Android waiter app should generate a stable client_order_id before enqueueing an order locally. Retrying the same create request returns the existing server order instead of creating a duplicate.

Each locally-created line should also use a stable client_line_id. Retrying an add-line request on the same order returns the existing line.

Server-generated ULIDs remain the canonical database primary keys.

## Batch boundary

Batch 6 stops at submitted order. It does not pretend to implement kitchen execution, billing or settlement.

Batch 7 will convert submitted order lines into KOT/KDS tickets and add kitchen preparation/ready state transitions.

Batch 8 will add cashier/POS, bills, split payments, reconciliation and daily closing.
