<?php

namespace Tests\Feature;

use App\Models\DiningArea;
use App\Models\DiningTable;
use App\Models\MenuCategory;
use App\Models\MenuItem;
use App\Models\MenuModifierGroup;
use App\Models\MenuModifierOption;
use App\Models\Order;
use App\Models\RestaurantBranch;
use App\Models\Tenant;
use App\Models\TenantUser;
use App\Services\Tenant\OrderOperationsService;
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
        $this->assertTrue(Schema::hasColumn('menu_items', 'image_path'));
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
        $this->assertSame(4, $retry->events()->count());
    }

    public function test_takeaway_order_and_structured_modifiers_preserve_shared_kitchen_context(): void
    {
        $tenant = $this->createTenant('restaurant-context', 'context.test');
        tenancy()->initialize($tenant);

        [$waiter, $table, $menuItem] = $this->seedFloor('100.00');
        $branch = $table->diningArea->branch;

        $group = MenuModifierGroup::query()->create([
            'name' => 'Size',
            'min_selections' => 1,
            'max_selections' => 1,
            'sort_order' => 1,
            'is_active' => true,
        ]);
        $option = MenuModifierOption::query()->create([
            'menu_modifier_group_id' => $group->id,
            'name' => 'Large',
            'price_delta' => '25.00',
            'sort_order' => 1,
            'is_active' => true,
        ]);
        $menuItem->modifierGroups()->attach($group->id, ['sort_order' => 1]);

        $service = app(OrderService::class);
        $order = $service->open($waiter, [
            'client_order_id' => '01TAKEAWAYCONTEXT000000000001',
            'branch_id' => $branch->id,
            'service_type' => Order::SERVICE_TAKEAWAY,
            'service_reference' => 'TA-42',
            'guest_count' => 1,
        ]);

        $line = $service->addItem($order, $waiter, [
            'client_line_id' => '01CONTEXTLINE0000000000000001',
            'menu_item_id' => $menuItem->id,
            'quantity' => 2,
            'seat_number' => 3,
            'course_number' => 2,
            'course_name' => 'Main',
            'modifiers' => [['option_id' => $option->id]],
            'allergy_instructions' => 'Peanut allergy',
            'kitchen_instructions' => 'Sauce on side',
        ]);

        $this->assertNull($order->dining_table_id);
        $this->assertSame($branch->id, $order->branch_id);
        $this->assertSame(Order::SERVICE_TAKEAWAY, $order->service_type);
        $this->assertSame('TA-42', $order->service_reference);
        $this->assertSame('125.00', $line->unit_price);
        $this->assertSame('250.00', $line->line_total);
        $this->assertSame(3, $line->seat_number);
        $this->assertSame(2, $line->course_number);
        $this->assertSame('Large', $line->modifiers_snapshot[0]['options'][0]['option_name']);
        $this->assertSame('Peanut allergy', $line->allergy_instructions);
        $this->assertSame('Sauce on side', $line->kitchen_instructions);
    }

    public function test_active_order_can_transfer_table_and_move_only_unsent_quantity(): void
    {
        $tenant = $this->createTenant('restaurant-ops', 'ops.test');
        tenancy()->initialize($tenant);

        [$waiter, $table, $menuItem] = $this->seedFloor('80.00');
        $area = $table->diningArea;
        $targetTable = DiningTable::query()->create([
            'dining_area_id' => $area->id,
            'code' => 'T-02',
            'name' => 'Table 2',
            'capacity' => 4,
            'status' => DiningTable::STATUS_AVAILABLE,
            'is_active' => true,
        ]);
        $thirdTable = DiningTable::query()->create([
            'dining_area_id' => $area->id,
            'code' => 'T-03',
            'name' => 'Table 3',
            'capacity' => 4,
            'status' => DiningTable::STATUS_AVAILABLE,
            'is_active' => true,
        ]);

        $orders = app(OrderService::class);
        $operations = app(OrderOperationsService::class);

        $source = $orders->open($waiter, [
            'client_order_id' => '01OPSOURCE000000000000000001',
            'dining_table_id' => $table->id,
            'guest_count' => 2,
        ]);
        $orders->addItem($source, $waiter, [
            'client_line_id' => '01OPLINE00000000000000000001',
            'menu_item_id' => $menuItem->id,
            'quantity' => 1,
        ]);
        $source = $orders->submit($source, $waiter, 'op-round-1');

        $unsent = $orders->addItem($source, $waiter, [
            'client_line_id' => '01OPLINE00000000000000000002',
            'menu_item_id' => $menuItem->id,
            'quantity' => 2,
        ]);

        $target = $orders->open($waiter, [
            'client_order_id' => '01OPTARGET000000000000000001',
            'dining_table_id' => $targetTable->id,
            'guest_count' => 1,
        ]);

        $result = $operations->moveUnsentItem(
            $source->fresh(),
            $unsent,
            $target,
            $waiter,
            1,
        );

        $this->assertSame(1, $unsent->fresh()->quantity);
        $this->assertSame(1, $result['target_line']->quantity);
        $this->assertSame('160.00', $source->fresh()->total);
        $this->assertSame('80.00', $target->fresh()->total);

        $transferred = $operations->transferTable($target->fresh(), $thirdTable, $waiter);

        $this->assertSame($thirdTable->id, $transferred->dining_table_id);
        $this->assertSame(DiningTable::STATUS_AVAILABLE, $targetTable->fresh()->status);
        $this->assertSame(DiningTable::STATUS_OCCUPIED, $thirdTable->fresh()->status);
    }

    public function test_draft_orders_can_merge_without_duplicating_financial_lines(): void
    {
        $tenant = $this->createTenant('restaurant-merge', 'merge.test');
        tenancy()->initialize($tenant);

        [$waiter, $table, $menuItem] = $this->seedFloor('60.00');
        $targetTable = DiningTable::query()->create([
            'dining_area_id' => $table->dining_area_id,
            'code' => 'T-02',
            'name' => 'Table 2',
            'capacity' => 4,
            'status' => DiningTable::STATUS_AVAILABLE,
            'is_active' => true,
        ]);

        $orders = app(OrderService::class);
        $operations = app(OrderOperationsService::class);

        $source = $orders->open($waiter, [
            'client_order_id' => '01MERGESOURCE000000000000001',
            'dining_table_id' => $table->id,
            'guest_count' => 1,
        ]);
        $orders->addItem($source, $waiter, [
            'client_line_id' => '01MERGELINE00000000000000001',
            'menu_item_id' => $menuItem->id,
            'quantity' => 2,
        ]);

        $target = $orders->open($waiter, [
            'client_order_id' => '01MERGETARGET000000000000001',
            'dining_table_id' => $targetTable->id,
            'guest_count' => 1,
        ]);
        $orders->addItem($target, $waiter, [
            'client_line_id' => '01MERGELINE00000000000000002',
            'menu_item_id' => $menuItem->id,
            'quantity' => 1,
        ]);

        $merged = $operations->mergeOrders($source, $target, $waiter);

        $this->assertSame(Order::STATUS_CANCELLED, $source->fresh()->status);
        $this->assertSame('0.00', $source->fresh()->total);
        $this->assertSame('180.00', $merged->total);
        $this->assertSame(2, $merged->items()->count());
        $this->assertSame(DiningTable::STATUS_AVAILABLE, $table->fresh()->status);
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
