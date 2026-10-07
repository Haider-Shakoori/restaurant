<?php

namespace App\Services\Tenant;

use App\Models\InventoryBalance;
use App\Models\InventoryConsumption;
use App\Models\InventoryItem;
use App\Models\KitchenTicket;
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
            $order = Order::query()
                ->with([
                    'table.diningArea.branch',
                    'items',
                    'kitchenTickets.items.orderItem',
                ])
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

            $consumption = InventoryConsumption::query()->firstOrCreate(
                ['order_id' => $order->id],
                [
                    'branch_id' => $branch->id,
                    'consumed_by_user_id' => $actor->getKey(),
                    'consumed_at' => now(),
                ],
            );

            $ticketItems = $order->kitchenTickets
                ->flatMap->items
                ->filter(fn ($ticketItem): bool => in_array($ticketItem->status, [
                    KitchenTicket::STATUS_READY,
                    KitchenTicket::STATUS_COMPLETED,
                ], true));

            foreach ($ticketItems as $ticketItem) {
                $orderItem = $ticketItem->orderItem;

                if (! $orderItem?->menu_item_id) {
                    continue;
                }

                $recipe = $recipes->get($orderItem->menu_item_id);

                if (! $recipe) {
                    continue;
                }

                $ticketItemCostMinor = 0;

                foreach ($recipe->items as $recipeItem) {
                    $alreadyConsumed = $consumption->lines()
                        ->where('kitchen_ticket_item_id', $ticketItem->id)
                        ->where('inventory_item_id', $recipeItem->inventory_item_id)
                        ->exists();

                    if ($alreadyConsumed) {
                        continue;
                    }

                    $quantity = Quantity::multiply(
                        (string) $recipeItem->quantity_base,
                        (string) $ticketItem->quantity,
                    );

                    $ingredientCost = $this->valuation->consume(
                        $branch,
                        $recipeItem->inventoryItem,
                        $quantity,
                    );
                    $ticketItemCostMinor += Money::toMinor($ingredientCost);

                    $movement = $this->recordMovement(
                        $branch,
                        $recipeItem->inventoryItem,
                        $actor,
                        StockMovement::TYPE_CONSUMPTION,
                        Quantity::subtract('0', $quantity),
                        'kitchen_ticket_item',
                        $ticketItem->id,
                        'kot-consumption:'.$ticketItem->id.':'.$recipeItem->inventory_item_id,
                        $orderItem->id,
                        notes: 'Automatic recipe consumption for prepared kitchen item.',
                    );

                    $consumption->lines()->create([
                        'order_item_id' => $orderItem->id,
                        'kitchen_ticket_item_id' => $ticketItem->id,
                        'recipe_id' => $recipe->id,
                        'inventory_item_id' => $recipeItem->inventory_item_id,
                        'stock_movement_id' => $movement->id,
                        'quantity_base' => $quantity,
                    ]);
                }

                if ($ticketItemCostMinor > 0) {
                    $cost = Money::fromMinor($ticketItemCostMinor);

                    $this->accounting->post(
                        $consumption->branch_id,
                        $actor,
                        'inventory_consumption',
                        $consumption->id,
                        'ticket_item',
                        'Recipe consumption for KOT item '.$ticketItem->id,
                        now()->format('Y-m-d'),
                        [
                            [
                                'account_id' => $this->accounting->systemAccount('cost_of_goods_sold')->id,
                                'debit' => $cost,
                                'counterparty_type' => 'order',
                                'counterparty_id' => $order->id,
                            ],
                            [
                                'account_id' => $this->accounting->systemAccount('inventory_asset')->id,
                                'credit' => $cost,
                                'counterparty_type' => 'order',
                                'counterparty_id' => $order->id,
                            ],
                        ],
                        'inventory-consumption:'.$consumption->id.':ticket-item:'.$ticketItem->id,
                    );
                }
            }

            return $consumption->fresh()->load(['lines.stockMovement.item']);
        });
    }
}
