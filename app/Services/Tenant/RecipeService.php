<?php

namespace App\Services\Tenant;

use App\Models\InventoryItem;
use App\Models\MenuItem;
use App\Models\Recipe;
use App\Models\RestaurantBranch;
use App\Support\Quantity;
use Illuminate\Support\Facades\DB;
use Illuminate\Validation\ValidationException;

class RecipeService
{
    public function createVersion(
        RestaurantBranch $branch,
        MenuItem $menuItem,
        array $data,
    ): Recipe {
        return DB::connection('tenant')->transaction(function () use ($branch, $menuItem, $data): Recipe {
            $latestVersion = (int) Recipe::query()
                ->where('branch_id', $branch->id)
                ->where('menu_item_id', $menuItem->id)
                ->max('version');

            Recipe::query()
                ->where('branch_id', $branch->id)
                ->where('menu_item_id', $menuItem->id)
                ->where('is_active', true)
                ->update(['is_active' => false]);

            $recipe = Recipe::query()->create([
                'branch_id' => $branch->id,
                'menu_item_id' => $menuItem->id,
                'name' => $data['name'] ?? $menuItem->name.' Recipe',
                'version' => $latestVersion + 1,
                'is_active' => true,
            ]);

            foreach ($data['items'] as $component) {
                $item = InventoryItem::query()
                    ->whereKey($component['inventory_item_id'])
                    ->where('is_active', true)
                    ->first();

                if (! $item) {
                    throw ValidationException::withMessages([
                        'items' => 'One or more recipe inventory items are missing or inactive.',
                    ]);
                }

                $quantity = Quantity::normalize((string) $component['quantity_base']);

                if (Quantity::toScaled($quantity) <= 0) {
                    throw ValidationException::withMessages([
                        'items' => 'Recipe quantities must be greater than zero.',
                    ]);
                }

                if ($recipe->items()->where('inventory_item_id', $item->id)->exists()) {
                    throw ValidationException::withMessages([
                        'items' => 'The same inventory item cannot appear twice in one recipe.',
                    ]);
                }

                $recipe->items()->create([
                    'inventory_item_id' => $item->id,
                    'quantity_base' => $quantity,
                ]);
            }

            return $recipe->load(['branch', 'menuItem', 'items.inventoryItem']);
        });
    }
}
