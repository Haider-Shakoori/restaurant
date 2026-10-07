<?php

namespace App\Services\Tenant;

use App\Models\InventoryBalance;
use App\Models\InventoryConsumption;
use App\Models\InventoryItem;
use App\Models\InventoryReservation;
use App\Models\InventoryReservationLine;
use App\Models\KitchenTicketItem;
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
        private readonly RestaurantSettingsService $settings,
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

    public function reserveProduction(KitchenTicketItem $productionItem, TenantUser $actor): ?InventoryReservation
    {
        return DB::connection('tenant')->transaction(function () use ($productionItem, $actor): ?InventoryReservation {
            $productionItem = KitchenTicketItem::query()
                ->with([
                    'ticket.order.branch',
                    'ticket.order.table.diningArea.branch',
                    'orderItem',
                ])
                ->lockForUpdate()
                ->findOrFail($productionItem->getKey());

            $existing = InventoryReservation::query()
                ->where('kitchen_ticket_item_id', $productionItem->id)
                ->first();

            if ($existing) {
                return $existing->load('lines.inventoryItem');
            }

            $order = $productionItem->ticket->order;
            $branch = $order->branch ?? $order->table?->diningArea?->branch;

            if (! $branch || ! $productionItem->orderItem?->menu_item_id) {
                return null;
            }

            $recipe = Recipe::query()
                ->where('branch_id', $branch->id)
                ->where('menu_item_id', $productionItem->orderItem->menu_item_id)
                ->where('is_active', true)
                ->orderByDesc('version')
                ->with('items.inventoryItem')
                ->first();

            if (! $recipe) {
                return null;
            }

            $policy = $this->settings->all($branch->id)['negative_stock_policy'] ?? 'block';
            $quantities = [];

            foreach ($recipe->items as $recipeItem) {
                $quantity = Quantity::multiply(
                    (string) $recipeItem->quantity_base,
                    (string) $productionItem->quantity,
                );

                InventoryBalance::query()->firstOrCreate(
                    [
                        'branch_id' => $branch->id,
                        'inventory_item_id' => $recipeItem->inventory_item_id,
                    ],
                    ['quantity' => '0.0000'],
                );

                $balance = InventoryBalance::query()
                    ->where('branch_id', $branch->id)
                    ->where('inventory_item_id', $recipeItem->inventory_item_id)
                    ->lockForUpdate()
                    ->firstOrFail();

                $reserved = InventoryReservationLine::query()
                    ->where('inventory_item_id', $recipeItem->inventory_item_id)
                    ->whereHas('reservation', fn ($query) => $query
                        ->where('branch_id', $branch->id)
                        ->where('status', InventoryReservation::STATUS_RESERVED))
                    ->sum('quantity_base');

                $available = Quantity::subtract((string) $balance->quantity, (string) $reserved);

                if (
                    $policy === 'block'
                    && Quantity::toScaled($available) < Quantity::toScaled($quantity)
                ) {
                    throw ValidationException::withMessages([
                        'inventory' => "Insufficient {$recipeItem->inventoryItem->name} for this kitchen production.",
                    ]);
                }

                $quantities[] = [$recipeItem, $quantity];
            }

            $reservation = InventoryReservation::query()->create([
                'branch_id' => $branch->id,
                'order_id' => $order->id,
                'kitchen_ticket_item_id' => $productionItem->id,
                'status' => InventoryReservation::STATUS_RESERVED,
                'reserved_at' => now(),
            ]);

            foreach ($quantities as [$recipeItem, $quantity]) {
                $reservation->lines()->create([
                    'recipe_id' => $recipe->id,
                    'inventory_item_id' => $recipeItem->inventory_item_id,
                    'quantity_base' => $quantity,
                ]);
            }

            $order->events()->create([
                'actor_user_id' => $actor->getKey(),
                'event_type' => 'inventory.production_reserved',
                'from_status' => $order->status,
                'to_status' => $order->status,
                'payload' => [
                    'kitchen_ticket_item_id' => $productionItem->id,
                    'reservation_id' => $reservation->id,
                    'negative_stock_policy' => $policy,
                ],
                'occurred_at' => now(),
            ]);

            return $reservation->load('lines.inventoryItem');
        });
    }

    public function commitProduction(KitchenTicketItem $productionItem, TenantUser $actor): ?InventoryConsumption
    {
        return DB::connection('tenant')->transaction(function () use ($productionItem, $actor): ?InventoryConsumption {
            $productionItem = KitchenTicketItem::query()
                ->with([
                    'ticket.order.branch',
                    'ticket.order.table.diningArea.branch',
                    'orderItem',
                ])
                ->lockForUpdate()
                ->findOrFail($productionItem->getKey());

            $reservation = InventoryReservation::query()
                ->with('lines.inventoryItem')
                ->where('kitchen_ticket_item_id', $productionItem->id)
                ->lockForUpdate()
                ->first();

            if (! $reservation) {
                $reservation = $this->reserveProduction($productionItem, $actor);
            }

            if (! $reservation) {
                return null;
            }

            if ($reservation->status === InventoryReservation::STATUS_RELEASED) {
                throw ValidationException::withMessages([
                    'inventory' => 'Released production cannot be committed without creating a new production item.',
                ]);
            }

            $order = $productionItem->ticket->order;
            $branch = $order->branch ?? $order->table?->diningArea?->branch;

            $consumption = InventoryConsumption::query()->firstOrCreate(
                ['order_id' => $order->id],
                [
                    'branch_id' => $branch->id,
                    'consumed_by_user_id' => $actor->getKey(),
                    'total_cost' => '0.00',
                    'consumed_at' => now(),
                ],
            );

            if ($reservation->status === InventoryReservation::STATUS_COMMITTED) {
                return $consumption->load(['lines.stockMovement.item']);
            }

            $addedCostMinor = 0;

            foreach ($reservation->lines as $line) {
                $existingLine = $consumption->lines()
                    ->where('kitchen_ticket_item_id', $productionItem->id)
                    ->where('inventory_item_id', $line->inventory_item_id)
                    ->first();

                if ($existingLine) {
                    continue;
                }

                $ingredientCost = $this->valuation->consume(
                    $branch,
                    $line->inventoryItem,
                    (string) $line->quantity_base,
                );
                $addedCostMinor += Money::toMinor($ingredientCost);

                $movement = $this->recordMovement(
                    $branch,
                    $line->inventoryItem,
                    $actor,
                    StockMovement::TYPE_CONSUMPTION,
                    Quantity::subtract('0', (string) $line->quantity_base),
                    'kitchen_production',
                    $productionItem->id,
                    'production-consumption:'.$productionItem->id.':'.$line->inventory_item_id,
                    $productionItem->order_item_id,
                    notes: 'Recipe consumption committed for kitchen production.',
                );

                $consumption->lines()->create([
                    'order_item_id' => $productionItem->order_item_id,
                    'kitchen_ticket_item_id' => $productionItem->id,
                    'recipe_id' => $line->recipe_id,
                    'inventory_item_id' => $line->inventory_item_id,
                    'stock_movement_id' => $movement->id,
                    'quantity_base' => $line->quantity_base,
                    'cost_amount' => $ingredientCost,
                ]);
            }

            if ($addedCostMinor !== 0) {
                $consumption->update([
                    'total_cost' => Money::add(
                        (string) $consumption->total_cost,
                        Money::fromMinor($addedCostMinor),
                    ),
                ]);
            }

            $reservation->update([
                'status' => InventoryReservation::STATUS_COMMITTED,
                'committed_at' => now(),
            ]);

            return $consumption->fresh()->load(['lines.stockMovement.item']);
        });
    }

    public function releaseProduction(KitchenTicketItem $productionItem, TenantUser $actor): ?InventoryReservation
    {
        return DB::connection('tenant')->transaction(function () use ($productionItem, $actor): ?InventoryReservation {
            $reservation = InventoryReservation::query()
                ->where('kitchen_ticket_item_id', $productionItem->id)
                ->lockForUpdate()
                ->first();

            if (! $reservation || $reservation->status === InventoryReservation::STATUS_RELEASED) {
                return $reservation;
            }

            if ($reservation->status === InventoryReservation::STATUS_COMMITTED) {
                throw ValidationException::withMessages([
                    'inventory' => 'Consumed production inventory cannot be released back to stock.',
                ]);
            }

            $reservation->update([
                'status' => InventoryReservation::STATUS_RELEASED,
                'released_at' => now(),
            ]);

            $productionItem->loadMissing('ticket.order');
            $productionItem->ticket->order->events()->create([
                'actor_user_id' => $actor->getKey(),
                'event_type' => 'inventory.production_released',
                'from_status' => $productionItem->ticket->order->status,
                'to_status' => $productionItem->ticket->order->status,
                'payload' => [
                    'kitchen_ticket_item_id' => $productionItem->id,
                    'reservation_id' => $reservation->id,
                ],
                'occurred_at' => now(),
            ]);

            return $reservation;
        });
    }

    public function consumeOrder(Order $order, TenantUser $actor): InventoryConsumption
    {
        return DB::connection('tenant')->transaction(function () use ($order, $actor): InventoryConsumption {
            $order = Order::query()
                ->with([
                    'branch',
                    'table.diningArea.branch',
                    'kitchenTickets.items',
                ])
                ->lockForUpdate()
                ->findOrFail($order->getKey());

            if (! in_array($order->status, [Order::STATUS_READY, Order::STATUS_SERVED], true)) {
                throw ValidationException::withMessages([
                    'order' => 'Inventory can only be finalized for a ready or served order.',
                ]);
            }

            foreach ($order->kitchenTickets->flatMap->items as $productionItem) {
                if (in_array($productionItem->status, ['voided', 'cancelled'], true)) {
                    continue;
                }

                $this->commitProduction($productionItem, $actor);
            }

            $branch = $order->branch ?? $order->table?->diningArea?->branch;

            $consumption = InventoryConsumption::query()->firstOrCreate(
                ['order_id' => $order->id],
                [
                    'branch_id' => $branch->id,
                    'consumed_by_user_id' => $actor->getKey(),
                    'total_cost' => '0.00',
                    'consumed_at' => now(),
                ],
            );

            $this->accounting->postInventoryConsumption(
                $consumption->fresh(),
                $actor,
                (string) $consumption->fresh()->total_cost,
            );

            return $consumption->fresh()->load(['lines.stockMovement.item']);
        });
    }
}
