<?php

namespace Tests\Feature;

use App\Models\DiningArea;
use App\Models\DiningTable;
use App\Models\KitchenStation;
use App\Models\KitchenTicket;
use App\Models\MenuCategory;
use App\Models\MenuItem;
use App\Models\MenuItemKitchenRoute;
use App\Models\Order;
use App\Models\RestaurantBranch;
use App\Models\Tenant;
use App\Models\TenantUser;
use App\Services\Tenant\KitchenService;
use App\Services\Tenant\OrderService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\File;
use Illuminate\Support\Facades\Schema;
use Illuminate\Support\Str;
use Tests\TestCase;

class KitchenWorkflowTest extends TestCase
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

    public function test_tenant_schema_contains_kitchen_execution_tables(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        $this->assertTrue(Schema::hasTable('kitchen_stations'));
        $this->assertTrue(Schema::hasTable('menu_item_kitchen_routes'));
        $this->assertTrue(Schema::hasTable('kitchen_tickets'));
        $this->assertTrue(Schema::hasTable('kitchen_ticket_items'));
        $this->assertTrue(Schema::hasTable('kitchen_ticket_events'));
    }

    public function test_submitted_order_splits_into_station_kots_and_reaches_served_state(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        [$waiter, $table, $branch, $food, $drink] = $this->seedRestaurant();

        $grill = KitchenStation::query()->create([
            'branch_id' => $branch->id,
            'code' => 'GRILL',
            'name' => 'Grill',
            'sort_order' => 1,
            'is_active' => true,
        ]);

        $bar = KitchenStation::query()->create([
            'branch_id' => $branch->id,
            'code' => 'BAR',
            'name' => 'Drinks',
            'sort_order' => 2,
            'is_active' => true,
        ]);

        MenuItemKitchenRoute::query()->create([
            'menu_item_id' => $food->id,
            'branch_id' => $branch->id,
            'kitchen_station_id' => $grill->id,
        ]);

        MenuItemKitchenRoute::query()->create([
            'menu_item_id' => $drink->id,
            'branch_id' => $branch->id,
            'kitchen_station_id' => $bar->id,
        ]);

        $orders = app(OrderService::class);
        $kitchen = app(KitchenService::class);

        $order = $orders->open($waiter, [
            'client_order_id' => '01KITCHENORDER000000000000001',
            'dining_table_id' => $table->id,
            'guest_count' => 2,
        ]);

        $orders->addItem($order, $waiter, [
            'client_line_id' => '01KITCHENLINE0000000000000001',
            'menu_item_id' => $food->id,
            'quantity' => 2,
        ]);

        $orders->addItem($order, $waiter, [
            'client_line_id' => '01KITCHENLINE0000000000000002',
            'menu_item_id' => $drink->id,
            'quantity' => 1,
        ]);

        $submitted = $orders->submit($order, $waiter);

        $this->assertSame(Order::STATUS_SUBMITTED, $submitted->status);
        $this->assertCount(2, $submitted->kitchenTickets);
        $this->assertSame(2, KitchenTicket::query()->count());
        $this->assertFalse(KitchenStation::query()->where('code', 'GENERAL')->exists());

        $grillTicket = KitchenTicket::query()->where('kitchen_station_id', $grill->id)->firstOrFail();
        $barTicket = KitchenTicket::query()->where('kitchen_station_id', $bar->id)->firstOrFail();

        $kitchen->start($grillTicket, $waiter);

        $this->assertSame(Order::STATUS_PREPARING, $order->fresh()->status);
        $this->assertSame(KitchenTicket::STATUS_PREPARING, $grillTicket->fresh()->status);

        $kitchen->ready($grillTicket->fresh(), $waiter);

        $this->assertSame(Order::STATUS_PREPARING, $order->fresh()->status);

        $kitchen->ready($barTicket, $waiter);

        $this->assertSame(Order::STATUS_READY, $order->fresh()->status);
        $this->assertNotNull($barTicket->fresh()->ready_at);

        $served = $kitchen->serve($order->fresh(), $waiter);

        $this->assertSame(Order::STATUS_SERVED, $served->status);
        $this->assertNotNull($served->served_at);
        $this->assertSame(2, KitchenTicket::query()->where('status', KitchenTicket::STATUS_COMPLETED)->count());
        $this->assertSame(2, $served->items()->where('status', 'served')->count());
    }

    public function test_unrouted_items_go_to_general_kitchen_and_submit_retry_does_not_duplicate_kots(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        [$waiter, $table, $branch, $food] = $this->seedRestaurant();

        $orders = app(OrderService::class);

        $order = $orders->open($waiter, [
            'client_order_id' => '01GENERALORDER000000000000001',
            'dining_table_id' => $table->id,
            'guest_count' => 1,
        ]);

        $orders->addItem($order, $waiter, [
            'client_line_id' => '01GENERALLINE0000000000000001',
            'menu_item_id' => $food->id,
            'quantity' => 1,
        ]);

        $first = $orders->submit($order, $waiter);
        $retry = $orders->submit($first, $waiter);

        $general = KitchenStation::query()
            ->where('branch_id', $branch->id)
            ->where('code', 'GENERAL')
            ->firstOrFail();

        $this->assertSame(1, KitchenTicket::query()->count());
        $this->assertSame($general->id, KitchenTicket::query()->value('kitchen_station_id'));
        $this->assertSame($first->id, $retry->id);
        $this->assertCount(1, $retry->kitchenTickets);
    }

    /**
     * @return array{TenantUser, DiningTable, RestaurantBranch, MenuItem, MenuItem}
     */
    private function seedRestaurant(): array
    {
        $waiter = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Waiter',
            'email' => Str::ulid().'@restaurant.test',
            'password' => 'secret-password',
            'is_active' => true,
            'role' => 'waiter',
        ]);

        $branch = RestaurantBranch::query()->create([
            'code' => 'MAIN',
            'name' => 'Main Branch',
            'is_active' => true,
        ]);

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
            'status' => DiningTable::STATUS_AVAILABLE,
            'is_active' => true,
        ]);

        $category = MenuCategory::query()->create([
            'name' => 'Menu',
            'sort_order' => 1,
            'is_active' => true,
        ]);

        $food = MenuItem::query()->create([
            'menu_category_id' => $category->id,
            'sku' => 'FOOD-001',
            'name' => 'Kabuli Pulao',
            'price' => '250.00',
            'is_available' => true,
            'sort_order' => 1,
        ]);

        $drink = MenuItem::query()->create([
            'menu_category_id' => $category->id,
            'sku' => 'DRINK-001',
            'name' => 'Soft Drink',
            'price' => '50.00',
            'is_available' => true,
            'sort_order' => 2,
        ]);

        return [$waiter, $table, $branch, $food, $drink];
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
