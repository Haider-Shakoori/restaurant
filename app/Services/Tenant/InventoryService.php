<?php

namespace App\Services\Tenant;

use App\Models\InventoryBalance;
use App\Models\InventoryConsumption;
use App\Models\InventoryItem;
use App\Models\Order;
use App\Models\Recipe;
use App\Models\RestaurantBranch;
use App\Models\StockMovement;
use App\Models\TenantUser;
use App\Support\Money;
use App\Support\Quantity;
use Illuminate\Support\Facades\DB;
use Illuminate\Validation\ValidationException;

class InventoryService
{
    public function __construct(
        private readonly InventoryValuationService $valuation,
        private readonly AccountingService $accounting,
    ) {}

    public function recordMovement(
        RestaurantBranch $branch,
        InventoryItem $item,
        TenantUser $actor,
        string $movementType,
        string $quantityDelta,
        string $sourceType,
        string $sourceId,
        string $idempotencyKey,
        ?string $sourceLineId = null,
        ?string $unitCost = null,
        ?string $notes = null,
    ): StockMovement {
        return DB::connection('tenant')->transaction(function () use (
            $branch,
            $item,
            $actor,
            $movementType,
            $quantityDelta,
            $sourceType,
            $sourceId,
            $idempotencyKey,
            $sourceLineId,
            $unitCost,
            $notes,
        ): StockMovement {
            $existing = StockMovement::query()
                ->where('idempotency_key', $idempotencyKey)
                ->first();

            if ($existing) {
                return $existing->load(['branch', 'item', 'actor']);
            }

            $delta = Quantity::normalize($quantityDelta);

            if (Quantity::toScaled($delta) === 0) {
                throw ValidationException::withMessages([
                    'quantity' => 'Stock movement quantity cannot be zero.',
                ]);
            }

            InventoryBalance::query()->firstOrCreate(
                [
                    'branch_id' => $branch->id,
                    'inventory_item_id' => $item->id,
                ],
                ['quantity' => '0.0000'],
            );

            $balance = InventoryBalance::query()
                ->where('branch_id', $branch->id)
                ->where('inventory_item_id', $item->id)
                ->lockForUpdate()
                ->firstOrFail();

            $movement = StockMovement::query()->create([
                'branch_id' => $branch->id,
                'inventory_item_id' => $item->id,
                'actor_user_id' => $actor->getKey(),
                'movement_type' => $movementType,
                'quantity_delta' => $delta,
                'unit_cost' => $unitCost,
                'source_type' => $sourceType,
                'source_id' => $sourceId,
                'source_line_id' => $sourceLineId,
                'idempotency_key' => $idempotencyKey,
                'notes' => $notes,
                'occurred_at' => now(),
            ]);

            $balance->update([
                'quantity' => Quantity::add((string) $balance->quantity, $delta),
            ]);

            return $movement->load(['branch', 'item', 'actor']);
        });
    }

    public function adjust(
        RestaurantBranch $branch,
        InventoryItem $item,
        TenantUser $actor,
        string $quantityDelta,
        string $clientAdjustmentId,
        string $reason,
    ): StockMovement {
        return DB::connection('tenant')->transaction(function () use (
            $branch,
            $item,
            $actor,
            $quantityDelta,
            $clientAdjustmentId,
            $reason,
        ): StockMovement {
            $idempotencyKey = 'adjustment:'.$branch->id.':'.$clientAdjustmentId;
            $existing = StockMovement::query()
                ->where('idempotency_key', $idempotencyKey)
                ->first();

            if ($existing) {
                return $existing->load(['branch', 'item', 'actor']);
            }

            $movement = $this->recordMovement(
                $branch,
                $item,
                $actor,
                StockMovement::TYPE_ADJUSTMENT,
                $quantityDelta,
                'manual_adjustment',
                $clientAdjustmentId,
                $idempotencyKey,
                notes: $reason,
            );

            $valueDelta = $this->valuation->adjust(
                $branch,
                $item,
                $quantityDelta,
            );

            $this->accounting->postInventoryAdjustment(
                $movement,
                $actor,
                $valueDelta,
            );

            return $movement;
        });
    }

    public function consumeOrder(Order $order, TenantUser $actor): InventoryConsumption
    {
        return DB::connection('tenant')->transaction(function () use ($order, $actor): InventoryConsumption {
            $existing = InventoryConsumption::query()
                ->where('order_id', $order->getKey())
                ->first();

            if ($existing) {
                return $existing->load(['lines.stockMovement.item']);
            }

            $order = Order::query()
                ->with(['table.diningArea.branch', 'items'])
                ->lockForUpdate()
                ->findOrFail($order->getKey());

            if (! in_array($order->status, [Order::STATUS_READY, Order::STATUS_SERVED], true)) {
                throw ValidationException::withMessages([
                    'order' => 'Inventory can only be consumed for a ready or served order.',
                ]);
            }

            $branch = $order->table->diningArea->branch;
            $menuItemIds = $order->items->pluck('menu_item_id')->filter()->unique();

            $recipes = Recipe::query()
                ->where('branch_id', $branch->id)
                ->where('is_active', true)
                ->whereIn('menu_item_id', $menuItemIds)
                ->with('items.inventoryItem')
                ->get()
                ->keyBy('menu_item_id');

            $consumption = InventoryConsumption::query()->create([
                'order_id' => $order->id,
                'branch_id' => $branch->id,
                'consumed_by_user_id' => $actor->getKey(),
                'consumed_at' => now(),
            ]);

            $costMinor = 0;

            foreach ($order->items as $orderItem) {
                if (! $orderItem->menu_item_id) {
                    continue;
                }

                $recipe = $recipes->get($orderItem->menu_item_id);

                if (! $recipe) {
                    continue;
                }

                foreach ($recipe->items as $recipeItem) {
                    $quantity = Quantity::multiply(
                        (string) $recipeItem->quantity_base,
                        (string) $orderItem->quantity,
                    );

                    $ingredientCost = $this->valuation->consume(
                        $branch,
                        $recipeItem->inventoryItem,
                        $quantity,
                    );
                    $costMinor += Money::toMinor($ingredientCost);

                    $movement = $this->recordMovement(
                        $branch,
                        $recipeItem->inventoryItem,
                        $actor,
                        StockMovement::TYPE_CONSUMPTION,
                        Quantity::subtract('0', $quantity),
                        'order',
                        $order->id,
                        'order-consumption:'.$order->id.':'.$orderItem->id.':'.$recipeItem->inventory_item_id,
                        $orderItem->id,
                        notes: 'Automatic recipe consumption for served order.',
                    );

                    $consumption->lines()->create([
                        'order_item_id' => $orderItem->id,
                        'recipe_id' => $recipe->id,
                        'inventory_item_id' => $recipeItem->inventory_item_id,
                        'stock_movement_id' => $movement->id,
                        'quantity_base' => $quantity,
                    ]);
                }
            }

            $this->accounting->postInventoryConsumption(
                $consumption,
                $actor,
                Money::fromMinor($costMinor),
            );

            return $consumption->load(['lines.stockMovement.item']);
        });
    }
}
