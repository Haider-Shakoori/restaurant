<?php

namespace Tests\Feature;

use App\Models\DiningArea;
use App\Models\DiningTable;
use App\Models\GoodsReceipt;
use App\Models\InventoryBalance;
use App\Models\InventoryConsumption;
use App\Models\InventoryItem;
use App\Models\InventoryReservation;
use App\Models\KitchenStation;
use App\Models\KitchenTicket;
use App\Models\MenuCategory;
use App\Models\MenuItem;
use App\Models\Order;
use App\Models\PurchaseOrder;
use App\Models\Recipe;
use App\Models\RestaurantBranch;
use App\Models\StockMovement;
use App\Models\Supplier;
use App\Models\Tenant;
use App\Models\TenantUser;
use App\Services\Tenant\InventoryService;
use App\Services\Tenant\KitchenService;
use App\Services\Tenant\OrderService;
use App\Services\Tenant\ProcurementService;
use App\Services\Tenant\RecipeService;
use App\Services\Tenant\RestaurantSettingsService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\File;
use Illuminate\Support\Facades\Schema;
use Illuminate\Support\Str;
use Tests\TestCase;

class InventoryProcurementRecipeTest extends TestCase
{
    use RefreshDatabase;

    /** @var array<int, string> */
    private array $tenantDatabases = [];

    /** @var array<int, string> */
    private array $tenantStoragePaths = [];

    protected function tearDown(): void
    {
        if (tenancy()->initialized) {
            tenancy()->end();
        }

        foreach ($this->tenantDatabases as $database) {
            @unlink(database_path($database));
        }

        foreach ($this->tenantStoragePaths as $path) {
            File::deleteDirectory($path);
        }

        parent::tearDown();
    }

    public function test_tenant_schema_contains_inventory_procurement_and_recipe_tables(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        $this->assertTrue(Schema::hasTable('suppliers'));
        $this->assertTrue(Schema::hasTable('inventory_items'));
        $this->assertTrue(Schema::hasTable('inventory_balances'));
        $this->assertTrue(Schema::hasTable('stock_movements'));
        $this->assertTrue(Schema::hasTable('purchase_orders'));
        $this->assertTrue(Schema::hasTable('purchase_order_lines'));
        $this->assertTrue(Schema::hasTable('goods_receipts'));
        $this->assertTrue(Schema::hasTable('goods_receipt_lines'));
        $this->assertTrue(Schema::hasTable('recipes'));
        $this->assertTrue(Schema::hasTable('recipe_items'));
        $this->assertTrue(Schema::hasTable('inventory_consumptions'));
        $this->assertTrue(Schema::hasTable('inventory_consumption_lines'));
    }

    public function test_partial_and_final_receipts_convert_purchase_units_and_update_stock_ledger(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        [$user, $branch, $supplier, $rice] = $this->seedProcurement();

        $procurement = app(ProcurementService::class);

        $po = $procurement->createPurchaseOrder($branch, $supplier, $user, [
            'lines' => [[
                'inventory_item_id' => $rice->id,
                'purchase_quantity' => '2.0000',
                'unit_cost' => '100.00',
            ]],
        ]);

        $line = $po->lines->first();

        $this->assertSame('2000.0000', $line->ordered_base_quantity);
        $this->assertSame('200.00', $po->estimated_total);

        $first = $procurement->receive($po, $user, [
            'client_receipt_id' => 'GRN-CLIENT-001',
            'lines' => [[
                'purchase_order_line_id' => $line->id,
                'purchase_quantity' => '1.0000',
            ]],
        ]);

        $this->assertSame(PurchaseOrder::STATUS_PARTIALLY_RECEIVED, $po->fresh()->status);
        $this->assertSame('1000.0000', InventoryBalance::query()->value('quantity'));
        $this->assertSame(1, StockMovement::query()->where('movement_type', StockMovement::TYPE_RECEIPT)->count());

        $retry = $procurement->receive($po->fresh(), $user, [
            'client_receipt_id' => 'GRN-CLIENT-001',
            'lines' => [[
                'purchase_order_line_id' => $line->id,
                'purchase_quantity' => '1.0000',
            ]],
        ]);

        $this->assertSame($first->id, $retry->id);
        $this->assertSame(1, GoodsReceipt::query()->count());
        $this->assertSame('1000.0000', InventoryBalance::query()->value('quantity'));

        $procurement->receive($po->fresh(), $user, [
            'client_receipt_id' => 'GRN-CLIENT-002',
            'lines' => [[
                'purchase_order_line_id' => $line->id,
                'purchase_quantity' => '1.0000',
            ]],
        ]);

        $this->assertSame(PurchaseOrder::STATUS_RECEIVED, $po->fresh()->status);
        $this->assertSame('2000.0000', InventoryBalance::query()->value('quantity'));
        $this->assertSame(2, StockMovement::query()->where('movement_type', StockMovement::TYPE_RECEIPT)->count());
    }

    public function test_recipe_versions_deactivate_previous_recipe(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        [, $branch, , $rice, $menuItem] = $this->seedProcurement(true);

        $recipes = app(RecipeService::class);

        $v1 = $recipes->createVersion($branch, $menuItem, [
            'name' => 'Rice Recipe',
            'items' => [[
                'inventory_item_id' => $rice->id,
                'quantity_base' => '200.0000',
            ]],
        ]);

        $v2 = $recipes->createVersion($branch, $menuItem, [
            'name' => 'Rice Recipe Updated',
            'items' => [[
                'inventory_item_id' => $rice->id,
                'quantity_base' => '250.0000',
            ]],
        ]);

        $this->assertFalse($v1->fresh()->is_active);
        $this->assertTrue($v2->is_active);
        $this->assertSame(2, $v2->version);
        $this->assertSame(1, Recipe::query()->where('is_active', true)->count());
    }

    public function test_recipe_quantity_and_unit_convert_to_ingredient_base_without_changing_costing(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        [, $branch, , $rice, $menuItem] = $this->seedProcurement(true);
        $recipes = app(RecipeService::class);

        $metric = $recipes->createVersion($branch, $menuItem, [
            'items' => [[
                'inventory_item_id' => $rice->id,
                'quantity' => '0.2500',
                'unit' => 'kg',
            ]],
        ]);

        $this->assertSame('250.0000', $metric->items->first()->quantity_base);
        $this->assertSame('g', $rice->base_unit);

        $legacy = $recipes->createVersion($branch, $menuItem, [
            'items' => [[
                'inventory_item_id' => $rice->id,
                'quantity_base' => '150.0000',
            ]],
        ]);

        $this->assertSame('150.0000', $legacy->items->first()->quantity_base);
        $this->assertFalse($metric->fresh()->is_active);
        $this->assertTrue($legacy->is_active);
    }

    public function test_recipe_rejects_mixing_kilograms_with_liters_and_fractional_pieces(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);
        [, $branch, , $rice, $menuItem] = $this->seedProcurement(true);
        $service = app(RecipeService::class);

        try {
            $service->createVersion($branch, $menuItem, [
                'items' => [[
                    'inventory_item_id' => $rice->id,
                    'quantity' => '2.0000',
                    'unit' => 'l',
                ]],
            ]);
            $this->fail('Incompatible recipe unit was accepted.');
        } catch (\Illuminate\Validation\ValidationException $ex) {
            $this->assertArrayHasKey('items', $ex->errors());
        }

        $rice->update(['base_unit' => 'pcs']);
        try {
            $service->createVersion($branch, $menuItem, [
                'items' => [[
                    'inventory_item_id' => $rice->id,
                    'quantity' => '1.5000',
                    'unit' => 'pcs',
                ]],
            ]);
            $this->fail('Fractional pieces were accepted.');
        } catch (\Illuminate\Validation\ValidationException $ex) {
            $this->assertArrayHasKey('items', $ex->errors());
        }
    }

    public function test_serving_order_consumes_recipe_stock_once_and_allows_visible_negative_stock(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        [$user, $branch, $supplier, $rice, $menuItem, $table] = $this->seedProcurement(true, true);

        $procurement = app(ProcurementService::class);
        $recipes = app(RecipeService::class);
        $kitchen = app(KitchenService::class);
        app(RestaurantSettingsService::class)->put([
            'negative_stock_policy' => 'allow',
        ]);

        $po = $procurement->createPurchaseOrder($branch, $supplier, $user, [
            'lines' => [[
                'inventory_item_id' => $rice->id,
                'purchase_quantity' => '0.4000',
                'unit_cost' => '100.00',
            ]],
        ]);

        $procurement->receive($po, $user, [
            'client_receipt_id' => 'GRN-CONSUME-001',
            'lines' => [[
                'purchase_order_line_id' => $po->lines->first()->id,
                'purchase_quantity' => '0.4000',
            ]],
        ]);

        $recipe = $recipes->createVersion($branch, $menuItem, [
            'items' => [[
                'inventory_item_id' => $rice->id,
                'quantity_base' => '250.0000',
            ]],
        ]);

        $order = Order::query()->create([
            'client_order_id' => (string) Str::ulid(),
            'dining_table_id' => $table->id,
            'waiter_id' => $user->id,
            'status' => Order::STATUS_READY,
            'guest_count' => 2,
            'subtotal' => '500.00',
            'total' => '500.00',
            'opened_at' => now(),
            'submitted_at' => now(),
        ]);

        $orderItem = $order->items()->create([
            'menu_item_id' => $menuItem->id,
            'client_line_id' => (string) Str::ulid(),
            'item_name' => $menuItem->name,
            'unit_price' => '250.00',
            'quantity' => 2,
            'line_total' => '500.00',
            'status' => KitchenTicket::STATUS_READY,
        ]);

        $station = KitchenStation::query()->create([
            'branch_id' => $branch->id,
            'code' => 'GENERAL',
            'name' => 'General Kitchen',
            'sort_order' => 1,
            'is_active' => true,
        ]);

        $ticket = KitchenTicket::query()->create([
            'order_id' => $order->id,
            'kitchen_station_id' => $station->id,
            'submitted_by_user_id' => $user->id,
            'ticket_number' => 'KOT-'.Str::ulid(),
            'status' => KitchenTicket::STATUS_READY,
            'queued_at' => now(),
            'started_at' => now(),
            'ready_at' => now(),
        ]);

        $ticket->items()->create([
            'order_item_id' => $orderItem->id,
            'item_name' => $menuItem->name,
            'quantity' => 2,
            'status' => KitchenTicket::STATUS_READY,
        ]);

        $served = $kitchen->serve($order, $user);

        $this->assertSame(Order::STATUS_SERVED, $served->status);
        $this->assertSame('-100.0000', InventoryBalance::query()->value('quantity'));
        $this->assertSame(1, InventoryConsumption::query()->count());
        $this->assertSame(1, StockMovement::query()->where('movement_type', StockMovement::TYPE_CONSUMPTION)->count());
        $this->assertSame($recipe->id, InventoryConsumption::query()->firstOrFail()->lines()->value('recipe_id'));

        $kitchen->serve($served->fresh(), $user);

        $this->assertSame('-100.0000', InventoryBalance::query()->value('quantity'));
        $this->assertSame(1, InventoryConsumption::query()->count());
        $this->assertSame(1, StockMovement::query()->where('movement_type', StockMovement::TYPE_CONSUMPTION)->count());
    }

    public function test_kot_send_reserves_and_start_commits_recipe_inventory_once(): void
    {
        $tenant = $this->createTenant('restaurant-production', 'production.test');
        tenancy()->initialize($tenant);

        [$user, $branch, $supplier, $rice, $menuItem, $table] = $this->seedProcurement(true, true);

        $procurement = app(ProcurementService::class);
        $recipes = app(RecipeService::class);
        $orders = app(OrderService::class);
        $kitchen = app(KitchenService::class);

        $po = $procurement->createPurchaseOrder($branch, $supplier, $user, [
            'lines' => [[
                'inventory_item_id' => $rice->id,
                'purchase_quantity' => '1.0000',
                'unit_cost' => '100.00',
            ]],
        ]);
        $procurement->receive($po, $user, [
            'client_receipt_id' => 'GRN-PRODUCTION-001',
            'lines' => [[
                'purchase_order_line_id' => $po->lines->first()->id,
                'purchase_quantity' => '1.0000',
            ]],
        ]);

        $recipes->createVersion($branch, $menuItem, [
            'items' => [[
                'inventory_item_id' => $rice->id,
                'quantity_base' => '250.0000',
            ]],
        ]);

        $order = $orders->open($user, [
            'client_order_id' => '01PRODUCTIONORDER000000000001',
            'dining_table_id' => $table->id,
            'guest_count' => 1,
        ]);
        $orders->addItem($order, $user, [
            'client_line_id' => '01PRODUCTIONLINE0000000000001',
            'menu_item_id' => $menuItem->id,
            'quantity' => 2,
        ]);

        $submitted = $orders->submit($order, $user, 'production-round-1');

        $this->assertSame('1000.0000', InventoryBalance::query()->value('quantity'));
        $this->assertSame(1, InventoryReservation::query()->where('status', 'reserved')->count());
        $this->assertSame(0, StockMovement::query()->where('movement_type', StockMovement::TYPE_CONSUMPTION)->count());

        $ticket = $submitted->kitchenTickets->first();
        $kitchen->start($ticket, $user);

        $this->assertSame('500.0000', InventoryBalance::query()->value('quantity'));
        $this->assertSame(1, InventoryReservation::query()->where('status', 'committed')->count());
        $this->assertSame(1, StockMovement::query()->where('movement_type', StockMovement::TYPE_CONSUMPTION)->count());

        $kitchen->start($ticket->fresh(), $user);
        $this->assertSame('500.0000', InventoryBalance::query()->value('quantity'));
        $this->assertSame(1, StockMovement::query()->where('movement_type', StockMovement::TYPE_CONSUMPTION)->count());
    }

    public function test_manual_adjustment_is_retry_safe(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        [$user, $branch, , $rice] = $this->seedProcurement();

        $inventory = app(InventoryService::class);

        $first = $inventory->adjust(
            $branch,
            $rice,
            $user,
            '25.0000',
            'COUNT-001',
            'Opening count',
        );

        $retry = $inventory->adjust(
            $branch,
            $rice,
            $user,
            '25.0000',
            'COUNT-001',
            'Opening count',
        );

        $this->assertSame($first->id, $retry->id);
        $this->assertSame('25.0000', InventoryBalance::query()->value('quantity'));
        $this->assertSame(1, StockMovement::query()->count());
    }

    /**
     * @return array<int, mixed>
     */
    private function seedProcurement(
        bool $withMenuItem = false,
        bool $withTable = false,
    ): array {
        $user = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Inventory Manager',
            'email' => Str::ulid().'@restaurant.test',
            'password' => 'secret-password',
            'is_active' => true,
            'role' => 'inventory',
        ]);

        $branch = RestaurantBranch::query()->create([
            'code' => 'MAIN',
            'name' => 'Main Branch',
            'is_active' => true,
        ]);

        $supplier = Supplier::query()->create([
            'code' => 'SUP-001',
            'name' => 'Food Supplier',
            'is_active' => true,
        ]);

        $rice = InventoryItem::query()->create([
            'sku' => 'RAW-RICE',
            'name' => 'Rice',
            'base_unit' => 'g',
            'purchase_unit' => 'kg',
            'purchase_to_base_factor' => '1000.000000',
            'reorder_level' => '500.0000',
            'is_active' => true,
        ]);

        $result = [$user, $branch, $supplier, $rice];

        if ($withMenuItem) {
            $category = MenuCategory::query()->create([
                'name' => 'Main',
                'sort_order' => 1,
                'is_active' => true,
            ]);

            $menuItem = MenuItem::query()->create([
                'menu_category_id' => $category->id,
                'sku' => 'FOOD-001',
                'name' => 'Kabuli Pulao',
                'price' => '250.00',
                'is_available' => true,
                'sort_order' => 1,
            ]);

            $result[] = $menuItem;
        }

        if ($withTable) {
            $area = DiningArea::query()->create([
                'branch_id' => $branch->id,
                'name' => 'Main Hall',
                'sort_order' => 1,
                'is_active' => true,
            ]);

            $table = DiningTable::query()->create([
                'dining_area_id' => $area->id,
                'code' => 'T-01',
                'name' => 'Table 1',
                'capacity' => 4,
                'status' => DiningTable::STATUS_OCCUPIED,
                'is_active' => true,
            ]);

            $result[] = $table;
        }

        return $result;
    }

    private function createTenant(string $id, string $domain): Tenant
    {
        $tenantStoragePath = storage_path(config('tenancy.filesystem.suffix_base').$id);
        File::deleteDirectory($tenantStoragePath);
        $this->tenantStoragePaths[] = $tenantStoragePath;

        if (config('database.connections.'.config('tenancy.database.central_connection').'.driver') === 'sqlite') {
            @unlink(database_path(
                config('tenancy.database.prefix').$id.config('tenancy.database.suffix')
            ));
        }

        $tenant = Tenant::create([
            'id' => $id,
            'provisioning_state' => 'ready',
        ]);

        $tenant->domains()->create(['domain' => $domain]);
        $this->tenantDatabases[] = $tenant->database()->getName();

        return $tenant;
    }
}
