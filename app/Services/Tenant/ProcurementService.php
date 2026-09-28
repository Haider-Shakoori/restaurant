<?php

namespace App\Services\Tenant;

use App\Models\GoodsReceipt;
use App\Models\InventoryItem;
use App\Models\PurchaseOrder;
use App\Models\PurchaseOrderLine;
use App\Models\RestaurantBranch;
use App\Models\StockMovement;
use App\Models\Supplier;
use App\Models\TenantUser;
use App\Support\Money;
use App\Support\Quantity;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;

class ProcurementService
{
    public function __construct(
        private readonly InventoryService $inventory,
        private readonly InventoryValuationService $valuation,
        private readonly AccountingService $accounting,
    ) {}

    public function createPurchaseOrder(
        RestaurantBranch $branch,
        Supplier $supplier,
        TenantUser $actor,
        array $data,
    ): PurchaseOrder {
        return DB::connection('tenant')->transaction(function () use ($branch, $supplier, $actor, $data): PurchaseOrder {
            if (! $branch->is_active || ! $supplier->is_active) {
                throw ValidationException::withMessages([
                    'purchase_order' => 'The selected branch and supplier must both be active.',
                ]);
            }

            $po = PurchaseOrder::query()->create([
                'branch_id' => $branch->id,
                'supplier_id' => $supplier->id,
                'ordered_by_user_id' => $actor->getKey(),
                'po_number' => 'PO-'.strtoupper((string) Str::ulid()),
                'status' => PurchaseOrder::STATUS_ORDERED,
                'estimated_total' => '0.00',
                'notes' => $data['notes'] ?? null,
                'ordered_at' => now(),
            ]);

            $totalMinor = 0;

            foreach ($data['lines'] as $line) {
                $item = InventoryItem::query()
                    ->whereKey($line['inventory_item_id'])
                    ->where('is_active', true)
                    ->first();

                if (! $item) {
                    throw ValidationException::withMessages([
                        'lines' => 'One or more inventory items are missing or inactive.',
                    ]);
                }

                $purchaseQuantity = Quantity::normalize((string) $line['purchase_quantity']);

                if (Quantity::toScaled($purchaseQuantity) <= 0) {
                    throw ValidationException::withMessages([
                        'lines' => 'Purchase quantities must be greater than zero.',
                    ]);
                }

                $factor = Quantity::factor((string) $item->purchase_to_base_factor);
                $baseQuantity = Quantity::multiplyByFactor($purchaseQuantity, $factor);
                $unitCost = Money::fromMinor(Money::toMinor((string) $line['unit_cost']));
                $lineTotal = Quantity::multiplyMoney($unitCost, $purchaseQuantity);
                $totalMinor += Money::toMinor($lineTotal);

                $po->lines()->create([
                    'inventory_item_id' => $item->id,
                    'item_name' => $item->name,
                    'purchase_unit' => $item->purchase_unit ?: $item->base_unit,
                    'conversion_factor' => $factor,
                    'ordered_purchase_quantity' => $purchaseQuantity,
                    'ordered_base_quantity' => $baseQuantity,
                    'received_base_quantity' => '0.0000',
                    'unit_cost' => $unitCost,
                    'line_total' => $lineTotal,
                ]);
            }

            $po->update(['estimated_total' => Money::fromMinor($totalMinor)]);

            return $po->fresh()->load(['branch', 'supplier', 'lines.item']);
        });
    }

    public function receive(
        PurchaseOrder $purchaseOrder,
        TenantUser $actor,
        array $data,
    ): GoodsReceipt {
        return DB::connection('tenant')->transaction(function () use ($purchaseOrder, $actor, $data): GoodsReceipt {
            if (! empty($data['client_receipt_id'])) {
                $existing = GoodsReceipt::query()
                    ->where('client_receipt_id', $data['client_receipt_id'])
                    ->first();

                if ($existing) {
                    abort_unless($existing->purchase_order_id === $purchaseOrder->id, 409);

                    return $existing->load(['lines.item', 'purchaseOrder', 'supplier', 'branch']);
                }
            }

            $purchaseOrder = PurchaseOrder::query()
                ->with(['branch', 'supplier', 'lines.item'])
                ->lockForUpdate()
                ->findOrFail($purchaseOrder->getKey());

            if (! in_array($purchaseOrder->status, [
                PurchaseOrder::STATUS_ORDERED,
                PurchaseOrder::STATUS_PARTIALLY_RECEIVED,
            ], true)) {
                throw ValidationException::withMessages([
                    'purchase_order' => 'Only ordered or partially received purchase orders can receive stock.',
                ]);
            }

            $receipt = GoodsReceipt::query()->create([
                'purchase_order_id' => $purchaseOrder->id,
                'branch_id' => $purchaseOrder->branch_id,
                'supplier_id' => $purchaseOrder->supplier_id,
                'received_by_user_id' => $actor->getKey(),
                'receipt_number' => 'GRN-'.strtoupper((string) Str::ulid()),
                'client_receipt_id' => $data['client_receipt_id'] ?? null,
                'status' => GoodsReceipt::STATUS_POSTED,
                'received_at' => now(),
                'notes' => $data['notes'] ?? null,
            ]);

            $receiptTotalMinor = 0;

            foreach ($data['lines'] as $receivedLine) {
                /** @var PurchaseOrderLine|null $poLine */
                $poLine = $purchaseOrder->lines->firstWhere('id', $receivedLine['purchase_order_line_id']);

                if (! $poLine) {
                    throw ValidationException::withMessages([
                        'lines' => 'A receipt line does not belong to this purchase order.',
                    ]);
                }

                $purchaseQuantity = Quantity::normalize((string) $receivedLine['purchase_quantity']);

                if (Quantity::toScaled($purchaseQuantity) <= 0) {
                    throw ValidationException::withMessages([
                        'lines' => 'Received quantities must be greater than zero.',
                    ]);
                }

                $baseQuantity = Quantity::multiplyByFactor(
                    $purchaseQuantity,
                    (string) $poLine->conversion_factor,
                );

                $newReceived = Quantity::add(
                    (string) $poLine->received_base_quantity,
                    $baseQuantity,
                );

                if (Quantity::toScaled($newReceived) > Quantity::toScaled((string) $poLine->ordered_base_quantity)) {
                    throw ValidationException::withMessages([
                        'lines' => 'Receipt quantity exceeds the remaining purchase order quantity.',
                    ]);
                }

                $lineTotal = Quantity::multiplyMoney(
                    (string) $poLine->unit_cost,
                    $purchaseQuantity,
                );
                $receiptTotalMinor += Money::toMinor($lineTotal);

                $receiptLine = $receipt->lines()->create([
                    'purchase_order_line_id' => $poLine->id,
                    'inventory_item_id' => $poLine->inventory_item_id,
                    'received_purchase_quantity' => $purchaseQuantity,
                    'received_base_quantity' => $baseQuantity,
                    'unit_cost' => $poLine->unit_cost,
                    'line_total' => $lineTotal,
                ]);

                $this->inventory->recordMovement(
                    $purchaseOrder->branch,
                    $poLine->item,
                    $actor,
                    StockMovement::TYPE_RECEIPT,
                    $baseQuantity,
                    'goods_receipt',
                    $receipt->id,
                    'goods-receipt:'.$receipt->id.':'.$poLine->id,
                    $receiptLine->id,
                    (string) $poLine->unit_cost,
                    'Purchase order receipt '.$purchaseOrder->po_number,
                );

                $this->valuation->receive(
                    $purchaseOrder->branch,
                    $poLine->item,
                    $baseQuantity,
                    $lineTotal,
                );

                $poLine->update(['received_base_quantity' => $newReceived]);
            }

            $purchaseOrder->refresh()->load('lines');

            $fullyReceived = $purchaseOrder->lines->every(
                fn (PurchaseOrderLine $line) => Quantity::toScaled((string) $line->received_base_quantity)
                    >= Quantity::toScaled((string) $line->ordered_base_quantity)
            );

            $purchaseOrder->update([
                'status' => $fullyReceived
                    ? PurchaseOrder::STATUS_RECEIVED
                    : PurchaseOrder::STATUS_PARTIALLY_RECEIVED,
                'completed_at' => $fullyReceived ? now() : null,
            ]);

            $receipt = $receipt->load(['lines.item', 'purchaseOrder', 'supplier', 'branch']);

            $this->accounting->postGoodsReceipt(
                $receipt,
                $actor,
                Money::fromMinor($receiptTotalMinor),
            );

            return $receipt;
        });
    }
}
