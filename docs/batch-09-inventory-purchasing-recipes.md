# Batch 9 — Inventory, Purchasing and Recipes

Batch 9 connects procurement and ingredient stock to the restaurant execution flow.

## Delivered

- tenant-local suppliers;
- ingredient/raw-material inventory items;
- base units plus purchase-unit conversion factors;
- branch-specific inventory balances;
- immutable stock-movement ledger;
- retry-safe manual stock adjustments;
- purchase orders with price and unit snapshots;
- partial and final goods receipts (GRNs);
- retry-safe receipt IDs;
- receipt-driven stock increases;
- branch-specific, versioned recipes for menu items;
- automatic recipe consumption when a ready order is served;
- retry-safe order consumption;
- low-stock state based on reorder level;
- negative stock visibility without blocking restaurant service.

## Stock design

Stock movements are the audit source of truth. inventory_balances is a fast derived balance updated only by the inventory service.

Every movement records:

- branch;
- ingredient;
- signed quantity delta;
- movement type;
- source document;
- actor;
- idempotency key;
- occurrence time.

The operational API never directly edits a balance row.

## Units

Each ingredient has a base unit used by recipes and stock balances.

Examples:

- rice: base g, purchase kg, factor 1000;
- cooking oil: base ml, purchase liter, factor 1000;
- eggs: base pcs, purchase tray, factor 30.

Purchase order and receipt quantities use the purchase unit. They are converted to the base unit using the conversion factor snapshotted on the PO line.

## Purchasing

A purchase order snapshots item name, purchase unit, conversion factor, ordered quantities and unit cost.

Receipts can be partial. Receiving creates GRN lines and immutable positive stock movements. Reusing the same client_receipt_id returns the original receipt instead of double-posting stock.

## Recipes and consumption

Recipes are branch-specific because the same menu item may have different ingredient usage at different restaurant branches.

Creating a new recipe version deactivates the previous active version without deleting history.

When an order reaches the Serve action:

1. the active recipe for that menu item and branch is resolved;
2. ingredient usage is multiplied by the ordered menu quantity;
3. immutable negative stock movements are posted;
4. an inventory_consumptions document links the order, recipe lines and movements;
5. retrying Serve cannot consume stock twice.

Menu items without a configured recipe are served normally and create no ingredient movement. Negative inventory is permitted and surfaced as a reconciliation signal rather than interrupting table service.

## Tenant API

Inventory role plus owner/admin/manager:

- GET /api/v1/inventory/items
- POST /api/v1/inventory/items
- POST /api/v1/inventory/items/{inventoryItem}/adjustments
- GET /api/v1/inventory/movements
- GET /api/v1/suppliers
- POST /api/v1/suppliers
- GET /api/v1/purchasing/orders
- POST /api/v1/purchasing/orders
- GET /api/v1/purchasing/orders/{purchaseOrder}
- POST /api/v1/purchasing/orders/{purchaseOrder}/receive
- GET /api/v1/recipes
- POST /api/v1/menu/items/{menuItem}/recipes

## Batch boundary

Batch 9 creates operational procurement and inventory source documents. It does not yet post accounts payable or inventory valuation journals.

Batch 10 will connect bills, payments, purchases, inventory movements and daily closing to double-entry accounting and management reporting.
