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
            ->assertSee('Table 1');

        $this->post("http://{$domain}/orders/take", [
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

    /**
     * @return array{Tenant, string}
     */
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
