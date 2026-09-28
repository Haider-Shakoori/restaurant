# Batch 7 — KOT/KDS and Kitchen Execution

Batch 7 extends the Batch 6 waiter-ordering foundation from submitted orders into kitchen execution.

## Delivered

- branch-specific kitchen stations;
- branch-specific menu-item routing to kitchen stations;
- automatic General Kitchen fallback for unrouted items;
- atomic KOT creation when an order is submitted;
- one KOT per station for each submitted order;
- immutable KOT item name, quantity and notes snapshots;
- KDS active queue with station/status filtering;
- queued, preparing, ready and completed ticket states;
- kitchen ticket audit events;
- synchronized order states: submitted, preparing, ready and served;
- waiter-visible kitchen ticket state on order responses;
- role enforcement for kitchen staff versus restaurant management;
- retry-safe order submission that cannot duplicate KOTs.

## KDS API

Authenticated active-subscription routes:

- GET /api/v1/kitchen/stations
- POST /api/v1/kitchen/stations — owner/admin/manager
- POST /api/v1/menu/items/{menuItem}/kitchen-route — owner/admin/manager
- GET /api/v1/kitchen/tickets — owner/admin/manager/kitchen
- GET /api/v1/kitchen/tickets/{kitchenTicket}
- POST /api/v1/kitchen/tickets/{kitchenTicket}/start
- POST /api/v1/kitchen/tickets/{kitchenTicket}/ready
- POST /api/v1/orders/{order}/serve — owner/admin/manager/waiter/cashier

## Routing behavior

A menu item may be routed to a different kitchen station in each branch. This supports restaurant chains where the same menu is prepared by different physical stations.

If no route exists for an item at the order's branch, the system automatically creates or reuses that branch's General Kitchen station. An item is therefore never silently omitted from kitchen execution.

## Status rules

Order submission creates queued KOTs.

Starting any KOT changes the overall order to preparing. The overall order becomes ready only after every KOT is ready or completed. The waiter can mark the order served only after the order is ready. Serving completes all ready KOTs and marks all order items served.

## Batch boundary

Batch 7 deliberately stops at served food. It does not create bills, accept money or free the table.

Batch 8 owns cashier/POS, bill generation, discounts, split payments, payment reconciliation, table closing and daily closing.
