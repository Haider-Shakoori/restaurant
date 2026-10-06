# Local inventory, recipes and purchasing

Batch 10 makes ingredient stock and procurement operational while the Windows desktop is the restaurant LAN authority.

## Inventory items

Inventory items use the same core fields as Laravel:

- SKU
- name
- base unit
- purchase unit
- purchase-to-base conversion factor
- reorder level
- active status

Quantities are stored to four decimal places. Purchase conversion factors and weighted-average unit cost are stored to six decimal places.

## Stock balances and movements

Each branch/item pair has one local balance and one local valuation row.

Movements are append-only and include:

- receipt
- consumption
- adjustment

Every movement has an idempotency key. Replaying the same adjustment, goods receipt or order consumption does not apply stock twice.

The local API can filter movement history by branch and inventory item.

## Weighted-average valuation

Goods receipts increase stock quantity and inventory value.

Average base-unit cost is recalculated as:

`new total inventory value / new total base quantity`

Recipe consumption reduces quantity and value using the current average base-unit cost.

Manual adjustments use the current average cost for the value delta, matching the cloud valuation behavior.

## Recipe versions

Recipes are branch-specific and menu-item-specific.

Creating a new version:

1. deactivates the previous active version
2. increments the version number
3. validates that every ingredient is active
4. prevents the same inventory item from appearing twice in one recipe
5. stores ingredient usage in base units

## Automatic recipe consumption

Inventory is consumed when an order is served, matching Laravel.

For each served order item:

`recipe quantity per serving x sold quantity = ingredient consumption`

Consumption is exactly once per order. It is committed inside the same SQLite transaction as the serve operation, so a failure cannot leave the order served without its stock movement or deduct stock without serving the order.

Menu items without an active recipe do not create consumption movements.

Negative stock is allowed, matching the existing Laravel inventory engine. This keeps service operational during emergency stock-count mismatches while still exposing the negative quantity for correction.

## Suppliers and purchase orders

Local procurement supports:

- active suppliers
- purchase orders
- purchase quantities in purchase units
- automatic purchase-to-base conversion
- estimated PO total
- partial receipts
- full receipts
- receipt notes
- client receipt IDs for idempotent retry

A PO moves:

`ordered -> partially_received -> received`

Receiving more than the remaining PO quantity is rejected.

## Low stock

When a branch is selected, zero balance is treated as zero stock.

An item is low stock when:

`current quantity <= reorder level`

The inventory list can be filtered to low-stock items only.

## Audit

Batch 10 appends audit events for:

- inventory item creation
- manual adjustment
- recipe version creation
- supplier creation
- purchase order creation
- goods receipt posting
- automatic order consumption

## LAN endpoints

- `GET /api/v1/inventory/items?branch_id=...&low_stock=true`
- `POST /api/v1/inventory/items`
- `POST /api/v1/inventory/items/{itemId}/adjustments`
- `GET /api/v1/inventory/movements`
- `GET /api/v1/suppliers`
- `POST /api/v1/suppliers`
- `GET /api/v1/recipes`
- `POST /api/v1/menu/items/{menuItemId}/recipes`
- `GET /api/v1/purchasing/orders`
- `POST /api/v1/purchasing/orders`
- `POST /api/v1/purchasing/orders/{purchaseOrderId}/receive`

These endpoints require owner, admin, manager or inventory role access.
