<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\DiningArea;
use App\Models\DiningTable;
use App\Models\InventoryBalance;
use App\Models\InventoryItem;
use App\Models\RecipeItem;
use App\Models\MenuCategory;
use App\Models\MenuItem;
use App\Models\RestaurantBranch;
use App\Models\StockMovement;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Storage;
use Illuminate\Support\Str;
use Illuminate\Validation\Rule;
use Illuminate\Validation\ValidationException;

class TenantDesktopManagementController extends Controller
{
    public function index(): JsonResponse
    {
        return response()->json(['data' => [
            'branches' => RestaurantBranch::query()->orderBy('name')->get(['id', 'name', 'is_active']),
            'areas' => DiningArea::query()->orderBy('name')->get(['id', 'branch_id', 'name', 'is_active']),
            'tables' => DiningTable::query()->orderBy('code')->get(['id', 'dining_area_id', 'code', 'name', 'capacity', 'status', 'is_active']),
            'categories' => MenuCategory::query()->orderBy('name')->get(['id', 'name', 'is_active']),
            'menu_items' => MenuItem::query()->orderBy('name')->get(['id', 'menu_category_id', 'sku', 'name', 'description', 'price', 'is_available', 'image_path']),
            'inventory_items' => InventoryItem::query()->orderBy('name')->get(['id', 'sku', 'name', 'base_unit', 'purchase_unit', 'purchase_to_base_factor', 'reorder_level', 'is_active']),
        ]]);
    }

    public function category(Request $request, ?MenuCategory $menuCategory = null): JsonResponse
    {
        $data = $request->validate([
            'name' => ['required', 'string', 'max:255'],
            'is_active' => ['sometimes', 'boolean'],
        ]);
        if ($menuCategory && array_key_exists('is_active', $data) && ! $data['is_active'] &&
            MenuItem::query()->where('menu_category_id', $menuCategory->id)->where('is_available', true)->exists()) {
            throw ValidationException::withMessages(['is_active' => 'Archive or move available menu items before deactivating this category.']);
        }

        $entity = $menuCategory ?? new MenuCategory;
        $entity->name = trim($data['name']);
        $entity->is_active = $data['is_active'] ?? ($menuCategory?->is_active ?? true);
        if (! $menuCategory) {
            $entity->sort_order = 0;
        }
        $entity->save();

        return response()->json(['data' => $entity], $menuCategory ? 200 : 201);
    }

    public function menuItem(Request $request, ?MenuItem $menuItem = null): JsonResponse
    {
        $data = $request->validate([
            'menu_category_id' => ['nullable', Rule::exists('menu_categories', 'id')],
            'sku' => ['nullable', 'string', 'max:80', Rule::unique('menu_items', 'sku')->ignore($menuItem?->id)],
            'name' => ['required', 'string', 'max:255'],
            'description' => ['nullable', 'string', 'max:1000'],
            'price' => ['required', 'numeric', 'min:0'],
            'is_available' => ['sometimes', 'boolean'],
        ]);

        $entity = $menuItem ?? new MenuItem;
        $entity->fill([
            'menu_category_id' => $data['menu_category_id'] ?? null,
            'sku' => filled($data['sku'] ?? null) ? Str::upper(trim($data['sku'])) : null,
            'name' => trim($data['name']),
            'description' => $data['description'] ?? null,
            'price' => $data['price'],
            'is_available' => $data['is_available'] ?? ($menuItem?->is_available ?? true),
        ]);
        if (! $menuItem) {
            $entity->sort_order = 0;
        }
        $entity->save();

        return response()->json(['data' => $entity], $menuItem ? 200 : 201);
    }

    public function image(Request $request, MenuItem $menuItem): JsonResponse
    {
        $request->validate(['image' => ['required', 'image', 'mimes:jpg,jpeg,png,webp', 'max:5120']]);
        $old = $menuItem->image_path;
        $new = $request->file('image')->store('menu-items', 'public');
        $menuItem->update(['image_path' => $new]);
        if (filled($old) && $old !== $new) Storage::disk('public')->delete($old);

        return response()->json(['data' => ['id' => $menuItem->id, 'image_path' => $new]]);
    }

    public function area(Request $request, ?DiningArea $diningArea = null): JsonResponse
    {
        $data = $request->validate([
            'branch_id' => ['required', Rule::exists('branches', 'id')],
            'name' => ['required', 'string', 'max:255'],
            'is_active' => ['sometimes', 'boolean'],
        ]);

        if ($diningArea &&
            ((string) $diningArea->branch_id !== (string) $data['branch_id'] || ! ($data['is_active'] ?? true)) &&
            DiningTable::query()->where('dining_area_id', $diningArea->id)->where('status', '!=', DiningTable::STATUS_AVAILABLE)->exists()) {
            throw ValidationException::withMessages(['name' => 'Move or close all occupied tables before changing the floor branch or disabling it.']);
        }

        $entity = $diningArea ?? new DiningArea;
        $entity->branch_id = $data['branch_id'];
        $entity->name = trim($data['name']);
        $entity->is_active = $data['is_active'] ?? ($diningArea?->is_active ?? true);
        if (! $diningArea) {
            $entity->sort_order = 0;
        }
        $entity->save();

        return response()->json(['data' => $entity], $diningArea ? 200 : 201);
    }

    public function table(Request $request, ?DiningTable $diningTable = null): JsonResponse
    {
        $data = $request->validate([
            'dining_area_id' => ['required', Rule::exists('dining_areas', 'id')],
            'code' => ['required', 'string', 'max:50', Rule::unique('dining_tables', 'code')->ignore($diningTable?->id)],
            'name' => ['required', 'string', 'max:255'],
            'capacity' => ['required', 'integer', 'min:1', 'max:100'],
            'is_active' => ['sometimes', 'boolean'],
        ]);
        $area = DiningArea::query()->findOrFail($data['dining_area_id']);
        if (! $area->is_active) {
            throw ValidationException::withMessages(['dining_area_id' => 'Select an active dining floor.']);
        }
        if ($diningTable && $diningTable->status !== DiningTable::STATUS_AVAILABLE &&
            ((string) $diningTable->dining_area_id !== (string) $data['dining_area_id'] || ! ($data['is_active'] ?? true))) {
            throw ValidationException::withMessages(['dining_area_id' => 'An occupied table cannot be moved or deactivated.']);
        }

        $entity = $diningTable ?? new DiningTable;
        $entity->dining_area_id = $data['dining_area_id'];
        $entity->code = Str::upper(trim($data['code']));
        $entity->name = trim($data['name']);
        $entity->capacity = (int) $data['capacity'];
        $entity->is_active = $data['is_active'] ?? ($diningTable?->is_active ?? true);
        if (! $diningTable) {
            $entity->status = DiningTable::STATUS_AVAILABLE;
        }
        $entity->save();

        return response()->json(['data' => $entity], $diningTable ? 200 : 201);
    }

    public function inventory(Request $request, ?InventoryItem $inventoryItem = null): JsonResponse
    {
        $data = $request->validate([
            'sku' => ['required', 'string', 'max:80', Rule::unique('inventory_items', 'sku')->ignore($inventoryItem?->id)],
            'name' => ['required', 'string', 'max:255'],
            'base_unit' => ['required', 'string', 'max:32'],
            'purchase_unit' => ['required', 'string', 'max:32'],
            'purchase_to_base_factor' => ['required', 'numeric', 'gt:0'],
            'reorder_level' => ['required', 'numeric', 'min:0'],
            'is_active' => ['sometimes', 'boolean'],
        ]);
        if ($inventoryItem && StockMovement::query()->where('inventory_item_id', $inventoryItem->id)->exists() &&
            ($inventoryItem->base_unit !== Str::lower($data['base_unit']) ||
             (float) $inventoryItem->purchase_to_base_factor !== (float) $data['purchase_to_base_factor'])) {
            throw ValidationException::withMessages(['base_unit' => 'Units/conversions are locked once stock movements exist; use a new ingredient.']);
        }

        if ($inventoryItem && array_key_exists('is_active', $data) && ! $data['is_active'] &&
            (InventoryBalance::query()->where('inventory_item_id', $inventoryItem->id)->where('quantity', '!=', 0)->exists() ||
             RecipeItem::query()->where('inventory_item_id', $inventoryItem->id)->exists())) {
            throw ValidationException::withMessages(['is_active' => 'Cannot archive an ingredient with stock or recipe references.']);
        }

        $entity = $inventoryItem ?? new InventoryItem;
        $entity->sku = Str::upper(trim($data['sku']));
        $entity->name = trim($data['name']);
        $entity->base_unit = Str::lower(trim($data['base_unit']));
        $entity->purchase_unit = Str::lower(trim($data['purchase_unit']));
        $entity->purchase_to_base_factor = $data['purchase_to_base_factor'];
        $entity->reorder_level = $data['reorder_level'];
        $entity->is_active = $data['is_active'] ?? ($inventoryItem?->is_active ?? true);
        $entity->save();

        return response()->json(['data' => $entity], $inventoryItem ? 200 : 201);
    }
}
