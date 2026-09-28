<?php

namespace Tests\Feature;

use App\Models\DiningArea;
use App\Models\DiningTable;
use App\Models\MenuCategory;
use App\Models\MenuItem;
use App\Models\Order;
use App\Models\RestaurantBranch;
use App\Models\Tenant;
use App\Models\TenantUser;
use App\Services\Tenant\OrderService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\File;
use Illuminate\Support\Facades\Schema;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;
use Tests\TestCase;

class RestaurantOrderingTest extends TestCase
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

    public function test_tenant_migration_contains_restaurant_ordering_tables(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');

        tenancy()->initialize($tenant);

        $this->assertTrue(Schema::hasColumn('users', 'role'));
        $this->assertTrue(Schema::hasTable('branches'));
        $this->assertTrue(Schema::hasTable('dining_areas'));
        $this->assertTrue(Schema::hasTable('dining_tables'));
        $this->assertTrue(Schema::hasTable('menu_categories'));
        $this->assertTrue(Schema::hasTable('menu_items'));
        $this->assertTrue(Schema::hasTable('orders'));
        $this->assertTrue(Schema::hasTable('order_items'));
        $this->assertTrue(Schema::hasTable('order_events'));
    }

    public function test_waiter_can_login_on_the_identified_tenant_domain(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');

        tenancy()->initialize($tenant);

        TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Waiter One',
            'email' => 'waiter@a.test',
            'password' => 'secret-password',
            'is_active' => true,
            'role' => 'waiter',
        ]);

        tenancy()->end();

        $this->postJson('http://a.test/api/v1/auth/login', [
            'email' => 'waiter@a.test',
            'password' => 'secret-password',
            'device_name' => 'Waiter Android',
        ])
            ->assertOk()
            ->assertJsonPath('tenant_id', 'restaurant-a')
            ->assertJsonPath('user.role', 'waiter')
            ->assertJsonStructure(['access_token']);
    }

    public function test_order_opening_is_idempotent_and_prevents_two_active_orders_on_one_table(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        [$waiter, $table] = $this->seedFloor();
        $service = app(OrderService::class);

        $first = $service->open($waiter, [
            'client_order_id' => '01ORDERCLIENT0000000000000001',
            'dining_table_id' => $table->id,
            'guest_count' => 3,
        ]);

        $retry = $service->open($waiter, [
            'client_order_id' => '01ORDERCLIENT0000000000000001',
            'dining_table_id' => $table->id,
            'guest_count' => 3,
        ]);

        $this->assertSame($first->id, $retry->id);
        $this->assertSame(DiningTable::STATUS_OCCUPIED, $table->fresh()->status);

        $this->expectException(ValidationException::class);

        $service->open($waiter, [
            'client_order_id' => '01ORDERCLIENT0000000000000002',
            'dining_table_id' => $table->id,
            'guest_count' => 2,
        ]);
    }

    public function test_add_item_snapshots_price_calculates_exact_total_and_submit_is_idempotent(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        [$waiter, $table, $menuItem] = $this->seedFloor('12.50');
        $service = app(OrderService::class);

        $order = $service->open($waiter, [
            'client_order_id' => '01ORDERCLIENT0000000000000010',
            'dining_table_id' => $table->id,
            'guest_count' => 4,
        ]);

        $line = $service->addItem($order, $waiter, [
            'client_line_id' => '01LINECLIENT00000000000000001',
            'menu_item_id' => $menuItem->id,
            'quantity' => 3,
            'notes' => 'No chili',
        ]);

        $retryLine = $service->addItem($order, $waiter, [
            'client_line_id' => '01LINECLIENT00000000000000001',
            'menu_item_id' => $menuItem->id,
            'quantity' => 3,
        ]);

        $this->assertSame($line->id, $retryLine->id);
        $this->assertSame('12.50', $line->unit_price);
        $this->assertSame('37.50', $line->line_total);
        $this->assertSame('37.50', $order->fresh()->total);

        $submitted = $service->submit($order, $waiter);
        $retry = $service->submit($submitted, $waiter);

        $this->assertSame(Order::STATUS_SUBMITTED, $retry->status);
        $this->assertNotNull($retry->submitted_at);
        $this->assertSame(3, $retry->events()->count());
    }

    /**
     * @return array{TenantUser, DiningTable, MenuItem}
     */
    private function seedFloor(string $price = '100.00'): array
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
            'name' => 'Main Course',
            'sort_order' => 1,
            'is_active' => true,
        ]);

        $item = MenuItem::query()->create([
            'menu_category_id' => $category->id,
            'sku' => 'FOOD-001',
            'name' => 'Kabuli Pulao',
            'price' => $price,
            'is_available' => true,
            'sort_order' => 1,
        ]);

        return [$waiter, $table, $item];
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
