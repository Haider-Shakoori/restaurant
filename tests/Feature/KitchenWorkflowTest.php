<?php

namespace Tests\Feature;

use App\Http\Middleware\EnsureTenantSubscriptionActive;
use App\Models\DiningArea;
use App\Models\DiningTable;
use App\Models\KitchenStation;
use App\Models\KitchenTicket;
use App\Models\KitchenTicketItem;
use App\Models\KotDispatchRound;
use App\Models\MenuCategory;
use App\Models\MenuItem;
use App\Models\MenuItemKitchenRoute;
use App\Models\Order;
use App\Models\RestaurantBranch;
use App\Models\Tenant;
use App\Models\TenantUser;
use App\Models\WaiterPushDevice;
use App\Services\Tenant\KitchenService;
use App\Services\Tenant\OrderService;
use App\Services\Tenant\RestaurantSettingsService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\DB;
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
        $this->assertTrue(Schema::hasTable('restaurant_settings'));
        $this->assertTrue(Schema::hasTable('kot_dispatch_rounds'));
        $this->assertTrue(Schema::hasColumn('order_items', 'dispatched_quantity'));
        $this->assertTrue(Schema::hasColumn('kitchen_tickets', 'kot_dispatch_round_id'));
        $this->assertTrue(Schema::hasColumn('kitchen_tickets', 'human_kot_number'));
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

        $kitchen->start($barTicket, $waiter);
        $kitchen->ready($barTicket->fresh(), $waiter);

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

    public function test_active_order_can_send_multiple_incremental_kot_rounds_without_resending_old_items(): void
    {
        $tenant = $this->createTenant('restaurant-multi-kot', 'multi-kot.test');
        tenancy()->initialize($tenant);

        [$waiter, $table, $branch, $food, $drink] = $this->seedRestaurant();
        $orders = app(OrderService::class);

        $order = $orders->open($waiter, [
            'client_order_id' => '01MULTIKOTORDER0000000000001',
            'dining_table_id' => $table->id,
            'guest_count' => 2,
        ]);

        $firstLine = $orders->addItem($order, $waiter, [
            'client_line_id' => '01MULTIKOTLINE00000000000001',
            'menu_item_id' => $food->id,
            'quantity' => 2,
        ]);

        $roundOneOrder = $orders->submit($order, $waiter, 'mutation-round-1');

        $this->assertSame(1, KotDispatchRound::query()->count());
        $this->assertSame(1, KitchenTicket::query()->count());
        $this->assertSame(2, $firstLine->fresh()->dispatched_quantity);
        $this->assertSame('KOT-0001', KotDispatchRound::query()->firstOrFail()->kot_number);

        $secondLine = $orders->addItem($roundOneOrder, $waiter, [
            'client_line_id' => '01MULTIKOTLINE00000000000002',
            'menu_item_id' => $drink->id,
            'quantity' => 1,
        ]);

        $roundTwoOrder = $orders->submit($roundOneOrder->fresh(), $waiter, 'mutation-round-2');

        $this->assertSame(2, KotDispatchRound::query()->count());
        $this->assertSame(2, KitchenTicket::query()->count());
        $this->assertSame(2, $firstLine->fresh()->dispatched_quantity);
        $this->assertSame(1, $secondLine->fresh()->dispatched_quantity);
        $this->assertSame('KOT-0002', KotDispatchRound::query()->where('sequence', 2)->value('kot_number'));

        $retry = $orders->submit($roundTwoOrder->fresh(), $waiter, 'mutation-round-2');

        $this->assertSame(2, KotDispatchRound::query()->count());
        $this->assertSame(2, KitchenTicket::query()->count());
        $this->assertSame($roundTwoOrder->id, $retry->id);
    }

    public function test_queue_and_preparing_settings_support_all_four_workflow_modes(): void
    {
        $modes = [
            ['queue' => true, 'preparing' => true, 'initial' => KitchenTicket::STATUS_QUEUED, 'start' => true],
            ['queue' => true, 'preparing' => false, 'initial' => KitchenTicket::STATUS_QUEUED, 'start' => false],
            ['queue' => false, 'preparing' => true, 'initial' => KitchenTicket::STATUS_ACTIVE, 'start' => true],
            ['queue' => false, 'preparing' => false, 'initial' => KitchenTicket::STATUS_ACTIVE, 'start' => false],
        ];

        foreach ($modes as $index => $mode) {
            $tenant = $this->createTenant('restaurant-mode-'.$index, 'mode-'.$index.'.test');
            tenancy()->initialize($tenant);

            [$waiter, $table, $branch, $food] = $this->seedRestaurant();
            app(RestaurantSettingsService::class)->put([
                'kitchen_queue_enabled' => $mode['queue'],
                'preparing_stage_enabled' => $mode['preparing'],
            ]);

            $orders = app(OrderService::class);
            $kitchen = app(KitchenService::class);

            $order = $orders->open($waiter, [
                'client_order_id' => '01MODEORDER000000000000000'.$index,
                'dining_table_id' => $table->id,
                'guest_count' => 1,
            ]);

            $orders->addItem($order, $waiter, [
                'client_line_id' => '01MODELINE0000000000000000'.$index,
                'menu_item_id' => $food->id,
                'quantity' => 1,
            ]);

            $orders->submit($order, $waiter, 'mode-mutation-'.$index);
            $ticket = KitchenTicket::query()->firstOrFail();

            $this->assertSame($mode['initial'], $ticket->status);

            if ($mode['start']) {
                $ticket = $kitchen->start($ticket, $waiter);
                $this->assertSame(KitchenTicket::STATUS_PREPARING, $ticket->status);
            }

            $ticket = $kitchen->ready($ticket->fresh(), $waiter);
            $this->assertSame(KitchenTicket::STATUS_READY, $ticket->status);
            $this->assertSame(Order::STATUS_READY, $order->fresh()->status);

            tenancy()->end();
        }
    }

    public function test_item_level_kds_transitions_drive_ticket_and_order_aggregates(): void
    {
        $tenant = $this->createTenant('restaurant-item-kds', 'item-kds.test');
        tenancy()->initialize($tenant);

        [$waiter, $table, $branch, $food, $drink] = $this->seedRestaurant();
        $station = KitchenStation::query()->create([
            'branch_id' => $branch->id,
            'code' => 'HOT',
            'name' => 'Hot Kitchen',
            'sort_order' => 1,
            'is_active' => true,
        ]);

        foreach ([$food, $drink] as $menuItem) {
            MenuItemKitchenRoute::query()->create([
                'menu_item_id' => $menuItem->id,
                'branch_id' => $branch->id,
                'kitchen_station_id' => $station->id,
            ]);
        }

        $orders = app(OrderService::class);
        $kitchen = app(KitchenService::class);

        $order = $orders->open($waiter, [
            'client_order_id' => '01ITEMKDSORDER00000000000001',
            'dining_table_id' => $table->id,
            'guest_count' => 2,
        ]);

        foreach ([$food, $drink] as $index => $menuItem) {
            $orders->addItem($order, $waiter, [
                'client_line_id' => '01ITEMKDSLINE0000000000000'.$index,
                'menu_item_id' => $menuItem->id,
                'quantity' => 1,
            ]);
        }

        $orders->submit($order, $waiter, 'item-kds-round-1');
        $ticket = KitchenTicket::query()->firstOrFail();
        $items = $ticket->items()->orderBy('id')->get();

        $first = $kitchen->startItem($items[0], $waiter);
        $this->assertSame(KitchenTicketItem::STATUS_PREPARING, $first->status);
        $this->assertSame(KitchenTicket::STATUS_PREPARING, $ticket->fresh()->status);
        $this->assertSame(Order::STATUS_PREPARING, $order->fresh()->status);

        $kitchen->readyItem($first, $waiter);
        $this->assertSame(KitchenTicket::STATUS_PREPARING, $ticket->fresh()->status);

        $second = $kitchen->startItem($items[1], $waiter);
        $kitchen->readyItem($second, $waiter);

        $this->assertSame(KitchenTicket::STATUS_READY, $ticket->fresh()->status);
        $this->assertSame(Order::STATUS_READY, $order->fresh()->status);
        $this->assertNotNull($items[0]->fresh()->ready_at);
        $this->assertNotNull($items[1]->fresh()->ready_at);
    }

    public function test_ready_item_queues_one_push_only_for_assigned_waiter_and_retries_do_not_duplicate(): void
    {
        $tenant = $this->createTenant('restaurant-push-ready', 'push-ready.test');
        tenancy()->initialize($tenant);

        [$waiter, $table, $branch, $food] = $this->seedRestaurant();
        $station = KitchenStation::query()->create([
            'branch_id' => $branch->id,
            'code' => 'HOT',
            'name' => 'Hot Kitchen',
            'sort_order' => 1,
            'is_active' => true,
        ]);
        MenuItemKitchenRoute::query()->create([
            'menu_item_id' => $food->id,
            'branch_id' => $branch->id,
            'kitchen_station_id' => $station->id,
        ]);
        WaiterPushDevice::query()->create([
            'central_device_id' => 'push-test-01',
            'tenant_user_id' => $waiter->id,
            'fcm_token' => str_repeat('a', 75),
            'platform' => 'android',
            'enabled' => true,
        ]);

        $orders = app(OrderService::class);
        $kitchen = app(KitchenService::class);
        $order = $orders->open($waiter, [
            'client_order_id' => '01PUSHREADYORDER0000000000001',
            'dining_table_id' => $table->id,
            'guest_count' => 2,
        ]);
        $orders->addItem($order, $waiter, [
            'client_line_id' => '01PUSHREADYLINE00000000000001',
            'menu_item_id' => $food->id,
            'quantity' => 1,
        ]);
        $orders->submit($order, $waiter, 'push-ready-round');
        $item = KitchenTicket::query()->firstOrFail()->items()->firstOrFail();
        $kitchen->startItem($item, $waiter);
        $kitchen->readyItem($item, $waiter);
        $kitchen->readyItem($item->fresh(), $waiter);

        $pending = DB::connection('tenant')
            ->table('waiter_push_deliveries')->get();
        $this->assertCount(1, $pending);
        $this->assertSame($item->id, $pending[0]->ready_item_id);
        $this->assertSame($order->id, $pending[0]->order_id);
        $this->assertSame('push-test-01', $pending[0]->central_device_id);
        $this->assertNull($pending[0]->sent_at);
    }

    public function test_expo_endpoint_is_derived_from_ready_production_items(): void
    {
        $this->withoutMiddleware(EnsureTenantSubscriptionActive::class);
        $tenant = $this->createTenant('restaurant-expo', 'expo.test');
        tenancy()->initialize($tenant);

        [$waiter, $table, $branch, $food] = $this->seedRestaurant();
        app(RestaurantSettingsService::class)->put([
            'expo_enabled' => true,
        ]);

        $orders = app(OrderService::class);
        $kitchen = app(KitchenService::class);

        $order = $orders->open($waiter, [
            'client_order_id' => '01EXPOORDER00000000000000001',
            'dining_table_id' => $table->id,
            'guest_count' => 1,
        ]);
        $orders->addItem($order, $waiter, [
            'client_line_id' => '01EXPOLINE000000000000000001',
            'menu_item_id' => $food->id,
            'quantity' => 1,
        ]);
        $orders->submit($order, $waiter, 'expo-round-1');

        $ticket = KitchenTicket::query()->firstOrFail();
        $kitchen->start($ticket, $waiter);
        $kitchen->ready($ticket->fresh(), $waiter);

        $waiter->update(['role' => 'kitchen']);

        $response = $this->actingAs($waiter->fresh(), 'sanctum')
            ->getJson('http://expo.test/api/v1/kitchen/expo');

        $response->assertOk()
            ->assertJsonPath('data.0.order_id', $order->id)
            ->assertJsonPath('data.0.ready_to_serve', true)
            ->assertJsonPath('data.0.ready_count', 1);
    }

    public function test_void_before_production_preserves_history_and_cancels_only_that_production(): void
    {
        $tenant = $this->createTenant('restaurant-void', 'void.test');
        tenancy()->initialize($tenant);

        [$waiter, $table, , $food] = $this->seedRestaurant();
        $orders = app(OrderService::class);
        $kitchen = app(KitchenService::class);

        $order = $orders->open($waiter, [
            'client_order_id' => '01VOIDORDER000000000000000001',
            'dining_table_id' => $table->id,
            'guest_count' => 1,
        ]);
        $orders->addItem($order, $waiter, [
            'client_line_id' => '01VOIDLINE0000000000000000001',
            'menu_item_id' => $food->id,
            'quantity' => 1,
        ]);
        $orders->submit($order, $waiter, 'void-round-1');

        $production = KitchenTicketItem::query()->firstOrFail();
        $voided = $kitchen->voidItem($production, $waiter, 'Guest changed mind');

        $this->assertSame(KitchenTicketItem::STATUS_VOIDED, $voided->status);
        $this->assertSame('Guest changed mind', $voided->void_reason);
        $this->assertNotNull($voided->voided_at);
        $this->assertSame(KitchenTicket::STATUS_CANCELLED, $voided->ticket->fresh()->status);
        $this->assertSame(1, KitchenTicketItem::query()->count());
    }

    public function test_refire_is_retry_safe_and_creates_new_round_without_rewriting_original(): void
    {
        $tenant = $this->createTenant('restaurant-refire', 'refire.test');
        tenancy()->initialize($tenant);

        [$waiter, $table, , $food] = $this->seedRestaurant();
        $orders = app(OrderService::class);
        $kitchen = app(KitchenService::class);

        $order = $orders->open($waiter, [
            'client_order_id' => '01REFIREORDER000000000000001',
            'dining_table_id' => $table->id,
            'guest_count' => 1,
        ]);
        $orders->addItem($order, $waiter, [
            'client_line_id' => '01REFIRELINE0000000000000001',
            'menu_item_id' => $food->id,
            'quantity' => 1,
        ]);
        $orders->submit($order, $waiter, 'refire-original-round');

        $original = KitchenTicketItem::query()->firstOrFail();
        $original = $kitchen->startItem($original, $waiter);
        $original = $kitchen->readyItem($original, $waiter);

        $refire = $kitchen->refireItem($original, $waiter, 'Dropped plate', 'refire-op-001');
        $retry = $kitchen->refireItem($original, $waiter, 'Dropped plate', 'refire-op-001');

        $this->assertSame($refire->id, $retry->id);
        $this->assertSame($original->id, $refire->refire_of_kitchen_ticket_item_id);
        $this->assertSame(2, KotDispatchRound::query()->count());
        $this->assertSame(2, KitchenTicket::query()->count());
        $this->assertSame(2, KitchenTicketItem::query()->count());
        $this->assertSame('Dropped plate', $refire->production_reason);
        $this->assertNotSame($original->id, $refire->id);
    }

    public function test_ready_item_can_be_recalled_without_restoring_consumed_stock(): void
    {
        $tenant = $this->createTenant('restaurant-recall', 'recall.test');
        tenancy()->initialize($tenant);

        [$waiter, $table, , $food] = $this->seedRestaurant();
        $orders = app(OrderService::class);
        $kitchen = app(KitchenService::class);

        $order = $orders->open($waiter, [
            'client_order_id' => '01RECALLORDER000000000000001',
            'dining_table_id' => $table->id,
            'guest_count' => 1,
        ]);
        $orders->addItem($order, $waiter, [
            'client_line_id' => '01RECALLLINE0000000000000001',
            'menu_item_id' => $food->id,
            'quantity' => 1,
        ]);
        $orders->submit($order, $waiter, 'recall-round-1');

        $production = KitchenTicketItem::query()->firstOrFail();
        $production = $kitchen->startItem($production, $waiter);
        $production = $kitchen->readyItem($production, $waiter);

        $recalled = $kitchen->recallItem($production, $waiter, 'Needs garnish correction');

        $this->assertSame(KitchenTicketItem::STATUS_PREPARING, $recalled->status);
        $this->assertSame('Needs garnish correction', $recalled->recall_reason);
        $this->assertNotNull($recalled->recalled_at);
        $this->assertSame(Order::STATUS_PREPARING, $order->fresh()->status);
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
