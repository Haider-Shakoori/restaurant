<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\DiningArea;
use App\Models\DiningTable;
use App\Models\InventoryItem;
use App\Models\KitchenStation;
use App\Models\MenuCategory;
use App\Models\MenuItem;
use App\Models\RestaurantBranch;
use App\Models\Supplier;
use App\Models\TenantUser;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Str;
use Illuminate\Validation\Rule;

class TenantPortalSetupController extends Controller
{
    public function branch(Request $request): RedirectResponse
    {
        $data = $request->validate([
            'code' => ['required', 'string', 'max:32', Rule::unique('branches', 'code')],
            'name' => ['required', 'string', 'max:255'],
        ]);

        RestaurantBranch::query()->create([
            'code' => Str::upper($data['code']),
            'name' => $data['name'],
            'is_active' => true,
        ]);

        return back()->with('status', 'Branch created.');
    }

    public function area(Request $request): RedirectResponse
    {
        $data = $request->validate([
            'branch_id' => ['required', Rule::exists('branches', 'id')],
            'name' => ['required', 'string', 'max:255'],
        ]);

        DiningArea::query()->create([
            ...$data,
            'sort_order' => 0,
            'is_active' => true,
        ]);

        return back()->with('status', 'Dining area created.');
    }

    public function table(Request $request): RedirectResponse
    {
        $data = $request->validate([
            'dining_area_id' => ['required', Rule::exists('dining_areas', 'id')],
            'code' => ['required', 'string', 'max:50', Rule::unique('dining_tables', 'code')],
            'name' => ['required', 'string', 'max:255'],
            'capacity' => ['required', 'integer', 'min:1', 'max:100'],
        ]);

        DiningTable::query()->create([
            ...$data,
            'code' => Str::upper($data['code']),
            'status' => DiningTable::STATUS_AVAILABLE,
            'is_active' => true,
        ]);

        return back()->with('status', 'Dining table created.');
    }

    public function station(Request $request): RedirectResponse
    {
        $data = $request->validate([
            'branch_id' => ['required', Rule::exists('branches', 'id')],
            'code' => ['required', 'string', 'max:50', Rule::unique('kitchen_stations', 'code')],
            'name' => ['required', 'string', 'max:255'],
        ]);

        KitchenStation::query()->create([
            ...$data,
            'code' => Str::upper($data['code']),
            'sort_order' => 0,
            'is_active' => true,
        ]);

        return back()->with('status', 'Kitchen station created.');
    }

    public function category(Request $request): RedirectResponse
    {
        $data = $request->validate([
            'name' => ['required', 'string', 'max:255'],
        ]);

        MenuCategory::query()->create([
            'name' => $data['name'],
            'sort_order' => 0,
            'is_active' => true,
        ]);

        return back()->with('status', 'Menu category created.');
    }

    public function menuItem(Request $request): RedirectResponse
    {
        $data = $request->validate([
            'menu_category_id' => ['nullable', Rule::exists('menu_categories', 'id')],
            'sku' => ['nullable', 'string', 'max:80', Rule::unique('menu_items', 'sku')],
            'name' => ['required', 'string', 'max:255'],
            'description' => ['nullable', 'string', 'max:1000'],
            'price' => ['required', 'numeric', 'min:0'],
        ]);

        MenuItem::query()->create([
            ...$data,
            'sku' => filled($data['sku'] ?? null) ? Str::upper($data['sku']) : null,
            'is_available' => true,
            'sort_order' => 0,
        ]);

        return back()->with('status', 'Menu item created.');
    }

    public function inventoryItem(Request $request): RedirectResponse
    {
        $data = $request->validate([
            'sku' => ['required', 'string', 'max:80', Rule::unique('inventory_items', 'sku')],
            'name' => ['required', 'string', 'max:255'],
            'base_unit' => ['required', 'string', 'max:32'],
            'reorder_level' => ['nullable', 'numeric', 'min:0'],
        ]);

        InventoryItem::query()->create([
            'sku' => Str::upper($data['sku']),
            'name' => $data['name'],
            'base_unit' => Str::lower($data['base_unit']),
            'purchase_unit' => Str::lower($data['base_unit']),
            'purchase_to_base_factor' => '1.000000',
            'reorder_level' => $data['reorder_level'] ?? 0,
            'is_active' => true,
        ]);

        return back()->with('status', 'Inventory item created.');
    }

    public function supplier(Request $request): RedirectResponse
    {
        $data = $request->validate([
            'code' => ['required', 'string', 'max:50', Rule::unique('suppliers', 'code')],
            'name' => ['required', 'string', 'max:255'],
            'phone' => ['nullable', 'string', 'max:50'],
            'email' => ['nullable', 'email', 'max:255'],
        ]);

        Supplier::query()->create([
            ...$data,
            'code' => Str::upper($data['code']),
            'is_active' => true,
        ]);

        return back()->with('status', 'Supplier created.');
    }

    public function user(Request $request): RedirectResponse
    {
        $roles = ['owner', 'admin', 'manager', 'waiter', 'cashier', 'kitchen', 'inventory'];

        $data = $request->validate([
            'name' => ['required', 'string', 'max:255'],
            'email' => ['required', 'email', 'max:255', Rule::unique('users', 'email')],
            'phone' => ['nullable', 'string', 'max:50'],
            'role' => ['required', Rule::in($roles)],
            'password' => ['required', 'string', 'min:8', 'max:255'],
        ]);

        TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => $data['name'],
            'email' => Str::lower($data['email']),
            'phone' => $data['phone'] ?? null,
            'role' => $data['role'],
            'password' => $data['password'],
            'is_active' => true,
        ]);

        return back()->with('status', 'Restaurant user created.');
    }
}
