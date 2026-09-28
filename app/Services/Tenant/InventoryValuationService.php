<?php

namespace App\Services\Tenant;

use App\Models\InventoryItem;
use App\Models\InventoryValuation;
use App\Models\RestaurantBranch;
use App\Support\InventoryCost;
use App\Support\Money;
use App\Support\Quantity;
use Illuminate\Support\Facades\DB;

class InventoryValuationService
{
    public function receive(
        RestaurantBranch $branch,
        InventoryItem $item,
        string $quantity,
        string $value,
    ): InventoryValuation {
        return DB::connection('tenant')->transaction(function () use ($branch, $item, $quantity, $value): InventoryValuation {
            InventoryValuation::query()->firstOrCreate(
                [
                    'branch_id' => $branch->id,
                    'inventory_item_id' => $item->id,
                ],
                [
                    'quantity' => '0.0000',
                    'value' => '0.00',
                    'average_unit_cost' => '0.000000',
                ],
            );

            $valuation = InventoryValuation::query()
                ->where('branch_id', $branch->id)
                ->where('inventory_item_id', $item->id)
                ->lockForUpdate()
                ->firstOrFail();

            $newQuantity = Quantity::add((string) $valuation->quantity, $quantity);
            $newValue = Money::add((string) $valuation->value, $value);
            $average = Quantity::toScaled($newQuantity) !== 0
                ? InventoryCost::unitCostFromValue($newValue, $newQuantity)
                : (string) $valuation->average_unit_cost;

            $valuation->update([
                'quantity' => $newQuantity,
                'value' => $newValue,
                'average_unit_cost' => $average,
            ]);

            return $valuation->fresh();
        });
    }

    public function consume(
        RestaurantBranch $branch,
        InventoryItem $item,
        string $quantity,
    ): string {
        return DB::connection('tenant')->transaction(function () use ($branch, $item, $quantity): string {
            InventoryValuation::query()->firstOrCreate(
                [
                    'branch_id' => $branch->id,
                    'inventory_item_id' => $item->id,
                ],
                [
                    'quantity' => '0.0000',
                    'value' => '0.00',
                    'average_unit_cost' => '0.000000',
                ],
            );

            $valuation = InventoryValuation::query()
                ->where('branch_id', $branch->id)
                ->where('inventory_item_id', $item->id)
                ->lockForUpdate()
                ->firstOrFail();

            $cost = InventoryCost::valueForQuantity(
                (string) $valuation->average_unit_cost,
                $quantity,
            );

            $valuation->update([
                'quantity' => Quantity::subtract((string) $valuation->quantity, $quantity),
                'value' => Money::subtract((string) $valuation->value, $cost),
            ]);

            return $cost;
        });
    }
}
