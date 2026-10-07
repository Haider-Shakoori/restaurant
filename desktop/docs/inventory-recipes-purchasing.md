# Local inventory, recipes and purchasing

The existing Desktop inventory ledger remains the single source for ingredient balances, valuation, purchasing and production usage.

## Inventory items and valuation

Inventory items retain SKU, name, base/purchase unit, conversion factor, reorder level and active status. Each branch/item pair has one on-hand balance and weighted-average valuation row.

Goods receipts increase quantity/value. Production consumption reduces both using the current average base-unit cost. Manual adjustments remain explicit append-only movements.

## Reservation vs consumption

KOT realignment separates **reservation** from **actual consumption**:

1. Sending a KOT reserves recipe quantities for each kitchen production item.
2. Reservation changes `reserved` and therefore `available = on_hand - reserved`; it does not reduce on-hand valuation.
3. When actual production starts, the reservation is committed into append-only stock consumption exactly once.
4. If Preparing is disabled, commit happens at the configured production transition (READY or immediate active flow).
5. Serving performs an idempotent catch-up only; it cannot double-consume already committed kitchen items.

Consumption idempotency is keyed to the kitchen production item, not merely the customer order. Therefore later KOT rounds and re-fires consume their own recipe usage correctly.

## Void/cancel inventory rule

If an item is voided/cancelled before production starts, its reservation is released.

If production has already been committed, the Desktop does **not** silently add stock back. The audit event records that produced inventory was not returned. Any permitted correction must be an explicit stock adjustment, preserving operational and financial audit history.

## Re-fire

A re-fire creates a new kitchen production item linked to the original item. It receives its own reservation and exactly-once consumption. The guest's original order/bill line is not duplicated.

## Stock visibility

Inventory responses expose:

- `on_hand`
- `reserved`
- `available`
- low-stock state based on available quantity

This prevents a second order from treating already-reserved recipe stock as freely available while keeping valuation unchanged until production.

## Recipes

Recipes remain branch/menu-item/version specific. Creating a new version deactivates the previous active version. Recipe components are stored in base units and validated against active inventory items.

## Suppliers and purchase orders

Local procurement continues to support suppliers, POs, purchase-unit conversion, partial/full goods receipts and idempotent receipt IDs. Receiving stock updates the same inventory ledger used by KOT reservations and production consumption.

## LAN inventory endpoints

Existing inventory, recipe, supplier, movement, PO and receipt endpoints remain unchanged. The KOT realignment extends their underlying stock semantics rather than introducing another inventory subsystem.

This PR changes Desktop only; it does not modify Laravel or Flutter.
