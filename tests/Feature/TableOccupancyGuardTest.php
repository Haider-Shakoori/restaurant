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
use App\Services\Tenant\OrderOperationsService;
use App\Services\Tenant\OrderService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\File;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;
use Tests\TestCase;

/**
 * Batch 6 regression guards: table occupancy follows active orders exactly,
 * cancellation releases only idle tables, overrides are role-gated, and late
 * guests join the same order without losing lines.
 */
class TableOccupancyGuardTest extends TestCase
{
    use RefreshDatabase;

    /** @var array<int, string> */
    private array $tenantDatabases = [];

    /** @var array<int, string> */
    private array $tenantStoragePaths = [];

    private TenantUser $user;

    private DiningTable $table;

    private MenuItem $menuItem;

    protected function tearDown(): void
    {
        if (tenancy()->initialized) {
            tenancy()->end();
        }

        foreach ($this->tenantStoragePaths as $path) {
            File::deleteDirectory($path);
        }

        foreach ($this->tenantDatabases as $database) {
            @unlink(database_path($database));
        }

        parent::tearDown();
    }

    public function test_cancelling_the_last_active_order_releases_the_table(): void
    {
        $this->prepare();
        $operations = app(OrderOperationsService::class);

        $order = app(OrderService::class)->open($this->user, [
            'client_order_id' => 'TBL-CANCEL-1',
            'dining_table_id' => $this->table->id,
            'guest_count' => 2,
        ]);

        $this->assertSame(DiningTable::STATUS_OCCUPIED, $this->table->fresh()->status);

        $operations->cancel($order, $this->user, 'Guest left without ordering');

        $this->assertSame(Order::STATUS_CANCELLED, $order->fresh()->status);
        $this->assertSame('0.00', $order->fresh()->total);
        $this->assertSame(DiningTable::STATUS_AVAILABLE, $this->table->fresh()->status);

        // Retrying the same cancellation is idempotent and never duplicates work.
        $operations->cancel($order->fresh(), $this->user, 'Guest left without ordering');

        $this->assertSame(1, Order::query()->count());
        $this->assertSame(DiningTable::STATUS_AVAILABLE, $this->table->fresh()->status);
    }

    public function test_cancelling_one_of_several_active_orders_keeps_the_table_occupied(): void
    {
        $this->prepare();
        $orders = app(OrderService::class);
        $operations = app(OrderOperationsService::class);

        $first = $orders->open($this->user, [
            'client_order_id' => 'TBL-A', 'dining_table_id' => $this->table->id, 'guest_count' => 2,
        ]);

        // A legacy/second active order on the same table (for example created
        // before this guard existed or by a second device) must keep it occupied.
        $second = Order::query()->create([
            'client_order_id' => 'TBL-B',
            'branch_id' => $first->branch_id,
            'dining_table_id' => $this->table->id,
            'waiter_id' => $this->user->id,
            'service_type' => Order::SERVICE_DINE_IN,
            'status' => Order::STATUS_DRAFT,
            'guest_count' => 2,
            'subtotal' => '0.00',
            'total' => '0.00',
            'opened_at' => now(),
        ]);

        $operations->cancel($first, $this->user, 'Duplicate session');
        $this->assertSame(DiningTable::STATUS_OCCUPIED, $this->table->fresh()->status);

        $operations->cancel($second, $this->user, 'Party finished');
        $this->assertSame(DiningTable::STATUS_AVAILABLE, $this->table->fresh()->status);
    }

    public function test_management_override_is_role_gated_and_blocks_active_orders(): void
    {
        $this->prepare();
        $operations = app(OrderOperationsService::class);

        $order = app(OrderService::class)->open($this->user, [
            'client_order_id' => 'TBL-OVERRIDE-1',
            'dining_table_id' => $this->table->id,
            'guest_count' => 1,
        ]);

        $waiter = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Waiter',
            'email' => 'waiter-override@example.test',
            'password' => 'secret-password',
            'is_active' => true,
            'role' => 'waiter',
        ]);

        try {
            $operations->releaseTableForManagement($this->table, $waiter, 'Stale occupancy');
            $this->fail('Waiters must not override table occupancy.');
        } catch (ValidationException) {
            // Expected: role-gated override.
        }

        try {
            $operations->releaseTableForManagement($this->table, $this->user, 'Stale occupancy');
            $this->fail('An active unpaid order must block the override.');
        } catch (ValidationException) {
            // Expected: no silent order changes.
        }

        $this->assertSame(Order::STATUS_DRAFT, $order->fresh()->status);
        $this->assertSame(DiningTable::STATUS_OCCUPIED, $this->table->fresh()->status);

        $operations->cancel($order, $this->user, 'Resolved through cancellation');
        $released = $operations->releaseTableForManagement($this->table, $this->user, 'Stale occupancy confirmed');

        $this->assertSame(DiningTable::STATUS_AVAILABLE, $released->status);
    }

    public function test_additional_guests_join_the_same_order_without_losing_items(): void
    {
        $this->prepare();
        $orders = app(OrderService::class);
        $operations = app(OrderOperationsService::class);

        $order = $orders->open($this->user, [
            'client_order_id' => 'TBL-GUESTS-1',
            'dining_table_id' => $this->table->id,
            'guest_count' => 1,
        ]);
        $orders->addItem($order, $this->user, [
            'menu_item_id' => $this->menuItem->id,
            'quantity' => 2,
            'client_line_id' => 'GUEST-LINE-1',
        ]);

        $updated = $operations->updateGuests($order, $this->user, 4);

        $this->assertSame($order->id, $updated->id);
        $this->assertSame(4, (int) $updated->guest_count);
        $this->assertSame(1, $updated->items()->count());
        $this->assertSame(DiningTable::STATUS_OCCUPIED, $this->table->fresh()->status);

        try {
            $operations->updateGuests($order, $this->user, 5);
            $this->fail('Table capacity must be enforced.');
        } catch (ValidationException) {
            // Expected: capacity guard.
        }

        Order::query()->whereKey($order->id)->update(['status' => Order::STATUS_BILLED]);

        try {
            $operations->updateGuests($order->fresh(), $this->user, 2);
            $this->fail('Billed orders cannot change guests.');
        } catch (ValidationException) {
            // Expected: state guard.
        }
    }

    private function prepare(): void
    {
        $tenant = $this->createTenant('table-guard', 'table-guard.test');
        tenancy()->initialize($tenant);

        $this->user = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Floor Manager',
            'email' => 'floor-manager@example.test',
            'password' => 'secret-password',
            'is_active' => true,
            'role' => 'manager',
        ]);

        $branch = RestaurantBranch::query()->create([
            'code' => 'MAIN', 'name' => 'Main Branch', 'is_active' => true,
        ]);
        $area = DiningArea::query()->create([
            'branch_id' => $branch->id, 'name' => 'Main Hall', 'sort_order' => 1, 'is_active' => true,
        ]);
        $this->table = DiningTable::query()->create([
            'dining_area_id' => $area->id, 'code' => 'T-01', 'name' => 'Table 1',
            'capacity' => 4, 'status' => DiningTable::STATUS_AVAILABLE, 'is_active' => true,
        ]);

        $category = MenuCategory::query()->create([
            'name' => 'Main', 'sort_order' => 1, 'is_active' => true,
        ]);
        $this->menuItem = MenuItem::query()->create([
            'menu_category_id' => $category->id, 'sku' => 'FOOD-001', 'name' => 'Kabuli Pulao',
            'price' => '250.00', 'is_available' => true, 'sort_order' => 1,
        ]);
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
