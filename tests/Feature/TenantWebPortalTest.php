<?php

namespace Tests\Feature;

use App\Enums\BusinessStatus;
use App\Enums\DeviceStatus;
use App\Enums\LicenseStatus;
use App\Enums\ProvisioningState;
use App\Models\Business;
use App\Models\DeviceActivation;
use App\Models\DiningArea;
use App\Models\DiningTable;
use App\Models\InventoryBalance;
use App\Models\InventoryItem;
use App\Models\KitchenTicket;
use App\Models\LicenseKey;
use App\Models\MenuCategory;
use App\Models\MenuItem;
use App\Models\Order;
use App\Models\Plan;
use App\Models\PurchaseOrder;
use App\Models\RestaurantBranch;
use App\Models\Supplier;
use App\Models\Tenant;
use App\Models\TenantUser;
use App\Services\Platform\SubscriptionService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Hash;
use Illuminate\Support\Str;
use Tests\TestCase;

class TenantWebPortalTest extends TestCase
{
    use RefreshDatabase;

    /** @var array<int, string> */
    private array $tenantDatabases = [];

    protected function tearDown(): void
    {
        if (tenancy()->initialized) {
            tenancy()->end();
        }

        foreach ($this->tenantDatabases as $database) {
            @unlink(database_path($database));
        }

        parent::tearDown();
    }

    public function test_tenant_root_redirects_guest_to_restaurant_login(): void
    {
        [, $domain] = $this->createActiveTenant();

        $this->get("http://{$domain}/")
            ->assertRedirect('/login');

        $this->get("http://{$domain}/login")
            ->assertOk()
            ->assertSee('Sign in to your restaurant');
    }

    public function test_owner_can_login_and_open_dashboard(): void
    {
        [$tenant, $domain] = $this->createActiveTenant();

        tenancy()->initialize($tenant);

        TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Restaurant Owner',
            'email' => 'owner@example.test',
            'password' => 'OwnerPass123',
            'is_active' => true,
            'role' => 'owner',
        ]);

        tenancy()->end();

        $this->post("http://{$domain}/login", [
            'email' => 'owner@example.test',
            'password' => 'OwnerPass123',
        ])->assertRedirect('/dashboard');

        $this->get("http://{$domain}/dashboard")
            ->assertOk()
            ->assertSee('Dashboard')
            ->assertSee('Open orders')
            ->assertDontSee('tenant_id');
    }

    public function test_owner_can_take_order_and_send_it_to_kitchen_from_orders_page(): void
    {
        [$tenant, $domain] = $this->createActiveTenant();

        tenancy()->initialize($tenant);

        TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Restaurant Owner',
            'email' => 'orders-owner@example.test',
            'password' => 'OwnerPass123',
            'is_active' => true,
            'role' => 'owner',
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
            'price' => '250.00',
            'is_available' => true,
            'sort_order' => 1,
        ]);

        tenancy()->end();

        $this->post("http://{$domain}/login", [
            'email' => 'orders-owner@example.test',
            'password' => 'OwnerPass123',
        ])->assertRedirect('/dashboard');

        $this->get("http://{$domain}/orders")
            ->assertOk()
            ->assertSee('New Order / Take Order')
            ->assertSee('Kabuli Pulao')
            ->assertSee('Table 1')
            ->assertSee('@click="addItem(items.find(item => item.id ===', false);

        $this->post("http://{$domain}/orders/take", [
            'service_type' => Order::SERVICE_DINE_IN,
            'dining_table_id' => $table->id,
            'guest_count' => 3,
            'notes' => 'Family table',
            'submit_action' => 'kitchen',
            'lines' => [[
                'menu_item_id' => $item->id,
                'quantity' => 2,
                'notes' => 'No chili',
            ]],
        ])
            ->assertRedirect('/orders')
            ->assertSessionHas('status', 'Order submitted to Kitchen successfully.');

        tenancy()->initialize($tenant);

        try {
            $order = Order::query()->with(['items', 'kitchenTickets.items'])->sole();

            $this->assertSame(Order::STATUS_SUBMITTED, $order->status);
            $this->assertSame('500.00', $order->total);
            $this->assertSame(3, $order->guest_count);
            $this->assertSame('No chili', $order->items->first()->notes);
            $this->assertSame(DiningTable::STATUS_OCCUPIED, $table->fresh()->status);
            $this->assertSame(1, KitchenTicket::query()->count());
            $this->assertSame('Kabuli Pulao', $order->kitchenTickets->first()->items->first()->item_name);
            $productionItemId = $order->kitchenTickets->first()->items->first()->id;
        } finally {
            tenancy()->end();
        }

        $this->post("http://{$domain}/kitchen/items/{$productionItemId}/start")
            ->assertRedirect('/kitchen')
            ->assertSessionHas('status', 'Kitchen item started.');

        $this->post("http://{$domain}/kitchen/items/{$productionItemId}/ready")
            ->assertRedirect('/kitchen')
            ->assertSessionHas('status', 'Kitchen item marked ready.');

        tenancy()->initialize($tenant);

        try {
            $this->assertSame(
                KitchenTicket::STATUS_READY,
                KitchenTicket::query()->sole()->status,
            );
            $this->assertSame(Order::STATUS_READY, Order::query()->sole()->status);
        } finally {
            tenancy()->end();
        }
    }

    public function test_cashier_photo_pos_submits_to_same_kitchen_and_returns_to_cashier(): void
    {
        [$tenant, $domain] = $this->createActiveTenant();

        tenancy()->initialize($tenant);

        TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Restaurant Owner',
            'email' => 'photo-pos-owner@example.test',
            'password' => 'OwnerPass123',
            'is_active' => true,
            'role' => 'owner',
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
            'price' => '250.00',
            'is_available' => true,
            'sort_order' => 1,
        ]);

        tenancy()->end();

        $this->post("http://{$domain}/login", [
            'email' => 'photo-pos-owner@example.test',
            'password' => 'OwnerPass123',
        ])->assertRedirect('/dashboard');

        $this->get("http://{$domain}/pos")
            ->assertOk()
            ->assertSee('Photo menu & current order', false)
            ->assertSee('BusinessOS · POS order entry')
            ->assertSee('Kabuli Pulao')
            ->assertSee('Table 1');

        $this->get("http://{$domain}/orders")
            ->assertOk()
            ->assertSee('New Order / Take Order')
            ->assertSee('Kabuli Pulao')
            ->assertSee('Table 1')
            ->assertSee('@click="addItem(items.find(item => item.id ===', false);

        $this->post("http://{$domain}/orders/take", [
            'service_type' => Order::SERVICE_DINE_IN,
            'dining_table_id' => $table->id,
            'guest_count' => 3,
            'notes' => 'Family table',
            'submit_action' => 'kitchen',
            'from_pos' => 1,
            'lines' => [[
                'menu_item_id' => $item->id,
                'quantity' => 2,
                'notes' => 'No chili',
            ]],
        ])
            ->assertRedirect('/pos')
            ->assertSessionHas('status', 'Order submitted to Kitchen successfully.');

        tenancy()->initialize($tenant);

        try {
            $order = Order::query()->with(['items', 'kitchenTickets.items'])->sole();

            $this->assertSame(Order::STATUS_SUBMITTED, $order->status);
            $this->assertSame('500.00', $order->total);
            $this->assertSame(3, $order->guest_count);
            $this->assertSame('No chili', $order->items->first()->notes);
            $this->assertSame(DiningTable::STATUS_OCCUPIED, $table->fresh()->status);
            $this->assertSame(1, KitchenTicket::query()->count());
            $this->assertSame('Kabuli Pulao', $order->kitchenTickets->first()->items->first()->item_name);
            $productionItemId = $order->kitchenTickets->first()->items->first()->id;
        } finally {
            tenancy()->end();
        }

        $this->post("http://{$domain}/kitchen/items/{$productionItemId}/start")
            ->assertRedirect('/kitchen')
            ->assertSessionHas('status', 'Kitchen item started.');

        $this->post("http://{$domain}/kitchen/items/{$productionItemId}/ready")
            ->assertRedirect('/kitchen')
            ->assertSessionHas('status', 'Kitchen item marked ready.');

        tenancy()->initialize($tenant);

        try {
            $this->assertSame(
                KitchenTicket::STATUS_READY,
                KitchenTicket::query()->sole()->status,
            );
            $this->assertSame(Order::STATUS_READY, Order::query()->sole()->status);
        } finally {
            tenancy()->end();
        }
    }

    public function test_new_web_order_retry_does_not_duplicate_order_lines_or_kot_round(): void
    {
        [$tenant, $domain] = $this->createActiveTenant();

        tenancy()->initialize($tenant);

        TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Restaurant Owner',
            'email' => 'retry-owner@example.test',
            'password' => 'OwnerPass123',
            'is_active' => true,
            'role' => 'owner',
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

        $item = MenuItem::query()->create([
            'menu_category_id' => $category->id,
            'sku' => 'FOOD-RETRY',
            'name' => 'Retry Meal',
            'price' => '125.00',
            'is_available' => true,
            'sort_order' => 1,
        ]);

        tenancy()->end();

        $this->post("http://{$domain}/login", [
            'email' => 'retry-owner@example.test',
            'password' => 'OwnerPass123',
        ])->assertRedirect('/dashboard');

        $payload = [
            'client_order_id' => 'web-order-retry-001',
            'client_mutation_id' => 'web-kot-retry-001',
            'service_type' => Order::SERVICE_DINE_IN,
            'dining_table_id' => $table->id,
            'guest_count' => 2,
            'submit_action' => 'kitchen',
            'lines' => [[
                'client_line_id' => 'web-line-retry-001',
                'menu_item_id' => $item->id,
                'quantity' => 2,
            ]],
        ];

        $this->post("http://{$domain}/orders/take", $payload)
            ->assertRedirect('/orders');

        $this->post("http://{$domain}/orders/take", $payload)
            ->assertRedirect('/orders');

        tenancy()->initialize($tenant);

        try {
            $order = Order::query()
                ->with(['items', 'kotRounds.tickets.items'])
                ->where('client_order_id', 'web-order-retry-001')
                ->sole();

            $this->assertSame(1, Order::query()->count());
            $this->assertCount(1, $order->items);
            $this->assertCount(1, $order->kotRounds);
            $this->assertSame(2, $order->kotRounds->first()->tickets->flatMap->items->sum('quantity'));
            $this->assertSame('250.00', $order->total);
        } finally {
            tenancy()->end();
        }
    }

    public function test_owner_can_add_later_web_kot_round_to_active_order_without_resending_old_items(): void
    {
        [$tenant, $domain] = $this->createActiveTenant();

        tenancy()->initialize($tenant);

        TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Restaurant Owner',
            'email' => 'round-owner@example.test',
            'password' => 'OwnerPass123',
            'is_active' => true,
            'role' => 'owner',
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
        $firstItem = MenuItem::query()->create([
            'menu_category_id' => $category->id,
            'sku' => 'FOOD-001',
            'name' => 'First Item',
            'price' => '100.00',
            'is_available' => true,
            'sort_order' => 1,
        ]);
        $secondItem = MenuItem::query()->create([
            'menu_category_id' => $category->id,
            'sku' => 'FOOD-002',
            'name' => 'Second Item',
            'price' => '50.00',
            'is_available' => true,
            'sort_order' => 2,
        ]);

        tenancy()->end();

        $this->post("http://{$domain}/login", [
            'email' => 'round-owner@example.test',
            'password' => 'OwnerPass123',
        ])->assertRedirect('/dashboard');

        $this->post("http://{$domain}/orders/take", [
            'service_type' => Order::SERVICE_DINE_IN,
            'dining_table_id' => $table->id,
            'guest_count' => 2,
            'submit_action' => 'kitchen',
            'client_mutation_id' => 'web-round-1',
            'lines' => [[
                'client_line_id' => 'web-line-1',
                'menu_item_id' => $firstItem->id,
                'quantity' => 1,
            ]],
        ])->assertRedirect('/orders');

        tenancy()->initialize($tenant);
        $orderId = Order::query()->sole()->id;
        tenancy()->end();

        $this->post("http://{$domain}/orders/take", [
            'existing_order_id' => $orderId,
            'client_mutation_id' => 'web-round-2',
            'service_type' => Order::SERVICE_DINE_IN,
            'guest_count' => 2,
            'submit_action' => 'kitchen',
            'lines' => [[
                'client_line_id' => 'web-line-2',
                'menu_item_id' => $secondItem->id,
                'quantity' => 2,
            ]],
        ])
            ->assertRedirect('/orders')
            ->assertSessionHas('status', 'New items sent as another KOT round.');

        tenancy()->initialize($tenant);

        try {
            $order = Order::query()->with(['items', 'kotRounds.tickets.items'])->findOrFail($orderId);

            $this->assertCount(2, $order->kotRounds);
            $this->assertSame(1, $order->kotRounds[0]->tickets->flatMap->items->sum('quantity'));
            $this->assertSame(2, $order->kotRounds[1]->tickets->flatMap->items->sum('quantity'));
            $this->assertSame(2, $order->items()->count());
            $this->assertSame('200.00', $order->fresh()->total);
        } finally {
            tenancy()->end();
        }
    }

    public function test_owner_can_create_purchase_order_from_purchasing_page(): void
    {
        [$tenant, $domain] = $this->createActiveTenant();

        tenancy()->initialize($tenant);

        TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Restaurant Owner',
            'email' => 'purchasing-owner@example.test',
            'password' => 'OwnerPass123',
            'is_active' => true,
            'role' => 'owner',
        ]);

        $branch = RestaurantBranch::query()->create([
            'code' => 'MAIN',
            'name' => 'Main Branch',
            'is_active' => true,
        ]);

        $supplier = Supplier::query()->create([
            'code' => 'SUP-001',
            'name' => 'Main Supplier',
            'is_active' => true,
        ]);

        $item = InventoryItem::query()->create([
            'sku' => 'RICE-001',
            'name' => 'Rice',
            'base_unit' => 'g',
            'purchase_unit' => 'kg',
            'purchase_to_base_factor' => '1000.000000',
            'reorder_level' => '5.0000',
            'is_active' => true,
        ]);

        tenancy()->end();

        $this->post("http://{$domain}/login", [
            'email' => 'purchasing-owner@example.test',
            'password' => 'OwnerPass123',
        ])->assertRedirect('/dashboard');

        $this->get("http://{$domain}/purchasing")
            ->assertOk()
            ->assertSee('New Purchase Order')
            ->assertSee('Create Purchase Order')
            ->assertSee('Main Supplier')
            ->assertSee('Rice · kg');

        $this->post("http://{$domain}/purchasing/orders", [
            'branch_id' => $branch->id,
            'supplier_id' => $supplier->id,
            'notes' => 'Weekly stock order',
            'lines' => [[
                'inventory_item_id' => $item->id,
                'purchase_quantity' => '2.0000',
                'unit_cost' => '150.00',
            ]],
        ])
            ->assertRedirect('/purchasing')
            ->assertSessionHas('status');

        tenancy()->initialize($tenant);

        try {
            $purchaseOrder = PurchaseOrder::query()->with('lines')->sole();

            $this->assertSame($branch->id, $purchaseOrder->branch_id);
            $this->assertSame($supplier->id, $purchaseOrder->supplier_id);
            $this->assertSame('300.00', $purchaseOrder->estimated_total);
            $this->assertSame('Rice', $purchaseOrder->lines->first()->item_name);
            $purchaseOrderId = $purchaseOrder->id;
            $purchaseOrderLineId = $purchaseOrder->lines->first()->id;
        } finally {
            tenancy()->end();
        }

        $this->get("http://{$domain}/purchasing")
            ->assertOk()
            ->assertSee('Receive PO')
            ->assertSee('Receive & Add to Inventory', false);

        $this->post("http://{$domain}/purchasing/orders/{$purchaseOrderId}/receive", [
            'client_receipt_id' => 'WEB-GRN-001',
            'notes' => 'All goods received',
            'lines' => [[
                'purchase_order_line_id' => $purchaseOrderLineId,
                'purchase_quantity' => '2.0000',
            ]],
        ])
            ->assertRedirect('/purchasing')
            ->assertSessionHas('status');

        tenancy()->initialize($tenant);

        try {
            $this->assertSame(PurchaseOrder::STATUS_RECEIVED, PurchaseOrder::query()->findOrFail($purchaseOrderId)->status);
            $this->assertSame('2000.0000', InventoryBalance::query()->where('inventory_item_id', $item->id)->value('quantity'));
        } finally {
            tenancy()->end();
        }
    }

    public function test_owner_can_revoke_waiter_mobile_from_restaurant_settings(): void
    {
        [$tenant, $domain] = $this->createActiveTenant();
        $business = Business::query()->where('tenant_id', $tenant->id)->firstOrFail();

        $license = LicenseKey::create([
            'business_id' => $business->id,
            'version' => 1,
            'key_hash' => hash('sha256', 'tenant-settings-test-license'),
            'key_prefix' => 'RST',
            'key_last4' => 'TEST',
            'status' => LicenseStatus::Active,
            'max_devices_snapshot' => 5,
            'generated_at' => now(),
        ]);

        $device = DeviceActivation::create([
            'business_id' => $business->id,
            'license_key_id' => $license->id,
            'device_uid' => 'tenant-settings-waiter-1',
            'device_name' => 'Waiter Tablet',
            'platform' => 'android',
            'credential_hash' => hash('sha256', 'secret'),
            'credential_last4' => 'cret',
            'status' => DeviceStatus::Active,
            'activated_at' => now(),
            'last_seen_at' => now(),
        ]);

        tenancy()->initialize($tenant);
        TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Restaurant Owner',
            'email' => 'owner-device@example.test',
            'password' => 'OwnerPass123',
            'is_active' => true,
            'role' => 'owner',
        ]);
        tenancy()->end();

        $this->post("http://{$domain}/login", [
            'email' => 'owner-device@example.test',
            'password' => 'OwnerPass123',
        ])->assertRedirect('/dashboard');

        $this->post("http://{$domain}/settings/devices/{$device->id}/revoke")
            ->assertRedirect();

        $this->assertSame(DeviceStatus::Revoked, $device->fresh()->status);
        $this->assertNotNull($device->fresh()->revoked_at);
    }

    public function test_owner_can_update_staff_and_disabled_user_cannot_log_in(): void
    {
        [$tenant, $domain] = $this->createActiveTenant();
        tenancy()->initialize($tenant);

        $owner = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Owner',
            'email' => 'staff-manager@example.test',
            'password' => 'OwnerPass123',
            'is_active' => true,
            'role' => 'owner',
        ]);
        $staff = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Kitchen Member',
            'email' => 'staff-disabled@example.test',
            'password' => 'BeforePass123',
            'is_active' => true,
            'role' => 'kitchen',
        ]);
        $staffId = $staff->id;
        tenancy()->end();

        $this->post("http://{$domain}/login", [
            'email' => $owner->email,
            'password' => 'OwnerPass123',
        ])->assertRedirect('/dashboard');

        $this->patch("http://{$domain}/users/{$staffId}", [
            'name' => 'Updated Kitchen Member',
            'email' => 'staff-disabled@example.test',
            'phone' => '',
            'role' => 'waiter',
            'is_active' => 0,
            'password' => 'AfterPass123',
        ])->assertRedirect()->assertSessionHas('status');

        tenancy()->initialize($tenant);
        try {
            $updated = TenantUser::query()->findOrFail($staffId);
            $this->assertSame('Updated Kitchen Member', $updated->name);
            $this->assertSame('waiter', $updated->role);
            $this->assertFalse($updated->is_active);
            $this->assertTrue(Hash::check('AfterPass123', $updated->password));
        } finally {
            tenancy()->end();
        }

        $this->post("http://{$domain}/logout")->assertRedirect('/login');
        $this->post("http://{$domain}/login", [
            'email' => 'staff-disabled@example.test',
            'password' => 'AfterPass123',
        ])->assertSessionHasErrors('email');
    }

    public function test_last_active_owner_cannot_be_disabled_or_demoted(): void
    {
        [$tenant, $domain] = $this->createActiveTenant();
        tenancy()->initialize($tenant);
        $owner = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Only Owner',
            'email' => 'only-owner@example.test',
            'password' => 'OwnerPass123',
            'is_active' => true,
            'role' => 'owner',
        ]);
        $ownerId = $owner->id;
        tenancy()->end();

        $this->post("http://{$domain}/login", [
            'email' => 'only-owner@example.test',
            'password' => 'OwnerPass123',
        ])->assertRedirect('/dashboard');

        $this->patch("http://{$domain}/users/{$ownerId}", [
            'name' => 'Only Owner',
            'email' => 'only-owner@example.test',
            'role' => 'owner',
            'is_active' => 0,
        ])->assertSessionHasErrors('role');

        $this->patch("http://{$domain}/users/{$ownerId}", [
            'name' => 'Only Owner',
            'email' => 'only-owner@example.test',
            'role' => 'manager',
            'is_active' => 1,
        ])->assertSessionHasErrors('role');

        tenancy()->initialize($tenant);
        try {
            $owner = TenantUser::query()->findOrFail($ownerId);
            $this->assertTrue($owner->is_active);
            $this->assertSame('owner', $owner->role);
        } finally {
            tenancy()->end();
        }
    }

    public function test_inactive_staff_session_is_revoked_before_accessing_portal(): void
    {
        [$tenant, $domain] = $this->createActiveTenant();
        tenancy()->initialize($tenant);
        $staff = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Manager',
            'email' => 'manager-disabled@example.test',
            'password' => 'ManagerPass123',
            'is_active' => true,
            'role' => 'manager',
        ]);
        $staffId = $staff->id;
        tenancy()->end();

        $this->post("http://{$domain}/login", [
            'email' => 'manager-disabled@example.test',
            'password' => 'ManagerPass123',
        ])->assertRedirect('/dashboard');

        tenancy()->initialize($tenant);
        try {
            TenantUser::query()->findOrFail($staffId)->update(['is_active' => false]);
        } finally {
            tenancy()->end();
        }

        $this->get("http://{$domain}/dashboard")->assertRedirect('/login');
        $this->get("http://{$domain}/dashboard")->assertRedirect('/login');
    }

    /**
     * @return array{Tenant, string}
     */
    public function test_desktop_staff_api_requires_owner_and_provisions_real_logins(): void
    {
        [$tenant, $domain] = $this->createActiveTenant();
        tenancy()->initialize($tenant);
        $owner = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Owner',
            'email' => 'desktop-owner@example.test',
            'password' => 'OwnerPassword123',
            'role' => 'owner',
            'is_active' => true,
        ]);
        $waiter = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Waiter',
            'email' => 'desktop-waiter@example.test',
            'password' => 'WaiterPassword123',
            'role' => 'waiter',
            'is_active' => true,
        ]);
        $ownerToken = $owner->createToken('desktop-test')->plainTextToken;
        $waiterToken = $waiter->createToken('desktop-test')->plainTextToken;
        tenancy()->end();

        $url = "http://{$domain}/api/v1/desktop/users";
        $this->withToken($waiterToken)->getJson($url)->assertForbidden();
        $this->withToken($waiterToken)->postJson($url, [
            'name' => 'Unauthorized', 'email' => 'no@example.test',
            'role' => 'owner', 'password' => 'TestPassword123',
        ])->assertForbidden();

        $this->app['auth']->forgetGuards();
        $this->withToken($ownerToken)->postJson($url, [
            'name' => 'Kitchen Operator',
            'email' => 'kitchen@example.test',
            'role' => 'kitchen',
            'password' => 'KitchenPassword123',
        ])->assertCreated()->assertJsonPath('data.role', 'kitchen');

        tenancy()->initialize($tenant);
        $created = TenantUser::query()->where('email', 'kitchen@example.test')->sole();
        $this->assertTrue(Hash::check('KitchenPassword123', $created->password));
        $createdToken = $created->createToken('previous-session')->plainTextToken;
        $createdId = $created->id;
        tenancy()->end();

        $this->withToken($ownerToken)->patchJson("{$url}/{$createdId}", [
            'name' => 'Kitchen Manager',
            'email' => 'kitchen@example.test',
            'phone' => null,
            'role' => 'manager',
            'is_active' => true,
            'password' => 'NewPassword123',
        ])->assertOk()->assertJsonPath('data.role', 'manager');

        $this->app['auth']->forgetGuards();
        $this->withToken($createdToken)->getJson("http://{$domain}/api/v1/bootstrap")
            ->assertUnauthorized();

        $this->app['auth']->forgetGuards();
        $this->withToken($ownerToken)->patchJson("{$url}/{$owner->id}", [
            'name' => 'Owner', 'email' => 'desktop-owner@example.test',
            'role' => 'waiter', 'is_active' => false,
        ])->assertUnprocessable();

        tenancy()->initialize($tenant);
        $this->assertSame('owner', $owner->fresh()->role);
        $this->assertTrue((bool) $owner->fresh()->is_active);
        $this->assertTrue(Hash::check('NewPassword123',
            TenantUser::query()->findOrFail($createdId)->password));
        tenancy()->end();
    }

    public function test_desktop_management_updates_central_catalog_without_rewriting_occupied_tables(): void
    {
        [$tenant, $domain] = $this->createActiveTenant();
        tenancy()->initialize($tenant);

        $owner = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Manager Owner',
            'email' => 'catalog-owner@example.test',
            'password' => 'OwnerPassword123',
            'role' => 'owner',
            'is_active' => true,
        ]);
        $waiter = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Waiter',
            'email' => 'catalog-waiter@example.test',
            'password' => 'WaiterPassword123',
            'role' => 'waiter',
            'is_active' => true,
        ]);
        $ownerToken = $owner->createToken('management-test')->plainTextToken;
        $waiterToken = $waiter->createToken('management-test')->plainTextToken;
        $branch = RestaurantBranch::query()->create([
            'code' => 'MAIN',
            'name' => 'Main',
            'is_active' => true,
        ]);
        tenancy()->end();

        $base = "http://{$domain}/api/v1/desktop/management";
        $this->withToken($waiterToken)->getJson($base)->assertForbidden();
        $this->app['auth']->forgetGuards();
        $this->withToken($ownerToken)->getJson($base)->assertOk()
            ->assertJsonStructure(['data' => ['branches', 'areas', 'tables', 'categories', 'menu_items', 'inventory_items']]);

        $this->withToken($ownerToken)->postJson("{$base}/categories", [
            'name' => 'Hot Meals',
        ])->assertCreated();
        tenancy()->initialize($tenant);
        $category = MenuCategory::query()->where('name', 'Hot Meals')->sole();
        tenancy()->end();

        $this->withToken($ownerToken)->postJson("{$base}/menu-items", [
            'name' => 'Chicken Tikka',
            'sku' => 'CHK-001',
            'menu_category_id' => $category->id,
            'price' => '250.00',
        ])->assertCreated();
        tenancy()->initialize($tenant);
        $menu = MenuItem::query()->where('sku', 'CHK-001')->sole();
        tenancy()->end();

        $this->withToken($ownerToken)->patchJson("{$base}/menu-items/{$menu->id}", [
            'name' => 'Chicken Tikka Extra',
            'sku' => 'CHK-001',
            'menu_category_id' => $category->id,
            'price' => '275.00',
            'is_available' => true,
        ])->assertOk();
        $this->withToken($ownerToken)->patchJson("{$base}/categories/{$category->id}", [
            'name' => 'Hot Meals',
            'is_active' => false,
        ])->assertUnprocessable();

        $this->withToken($ownerToken)->postJson("{$base}/areas", [
            'branch_id' => $branch->id,
            'name' => 'Main Hall',
        ])->assertCreated();
        $this->withToken($ownerToken)->postJson("{$base}/areas", [
            'branch_id' => $branch->id,
            'name' => 'Patio',
        ])->assertCreated();

        tenancy()->initialize($tenant);
        $hall = DiningArea::query()->where('name', 'Main Hall')->sole();
        $patio = DiningArea::query()->where('name', 'Patio')->sole();
        tenancy()->end();

        $this->withToken($ownerToken)->postJson("{$base}/tables", [
            'dining_area_id' => $hall->id, 'name' => 'Table 1',
            'code' => 'T-01', 'capacity' => 4,
        ])->assertCreated();

        tenancy()->initialize($tenant);
        $table = DiningTable::query()->where('code', 'T-01')->sole();
        $table->status = DiningTable::STATUS_OCCUPIED;
        $table->save();
        tenancy()->end();

        $this->withToken($ownerToken)->patchJson("{$base}/tables/{$table->id}", [
            'dining_area_id' => $patio->id, 'name' => 'Table 1',
            'code' => 'T-01', 'capacity' => 4,
            'is_active' => false,
        ])->assertUnprocessable();

        tenancy()->initialize($tenant);
        $this->assertSame('275.00', $menu->fresh()->price);
        $this->assertSame($hall->id, $table->fresh()->dining_area_id);
        $this->assertTrue((bool) $table->fresh()->is_active);
        tenancy()->end();
    }

    private function createActiveTenant(): array
    {
        $plan = Plan::create([
            'code' => 'basic-'.Str::lower(Str::random(6)),
            'name' => 'Basic',
            'is_active' => true,
        ]);

        $tenantId = 'portal-'.Str::lower(Str::random(8));
        $tenant = Tenant::create([
            'id' => $tenantId,
            'provisioning_state' => ProvisioningState::Ready->value,
        ]);

        $domain = $tenantId.'.test';
        $tenant->domains()->create(['domain' => $domain]);
        $this->tenantDatabases[] = $tenant->database()->getName();

        $business = Business::create([
            'tenant_id' => $tenant->id,
            'plan_id' => $plan->id,
            'name' => 'Portal Restaurant',
            'requested_subdomain' => $tenantId,
            'contact_name' => 'Owner',
            'phone' => '+93700000000',
            'email' => 'owner@example.test',
            'status' => BusinessStatus::Provisioning,
            'provisioning_state' => ProvisioningState::Ready,
        ]);

        app(SubscriptionService::class)->startTrial($business);

        return [$tenant, $domain];
    }
}
