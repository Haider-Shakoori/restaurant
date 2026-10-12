<?php

namespace Tests\Feature;

use App\Models\InventoryBalance;
use App\Models\InventoryItem;
use App\Models\JournalEntry;
use App\Models\RestaurantBranch;
use App\Models\RestaurantSetting;
use App\Models\StockMovement;
use App\Models\Supplier;
use App\Models\Tenant;
use App\Models\TenantUser;
use App\Services\Tenant\AccountingService;
use App\Services\Tenant\InventoryService;
use App\Services\Tenant\ProcurementService;
use App\Services\Tenant\RestaurantSettingsService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\File;
use Illuminate\Support\Facades\Route;
use Illuminate\Support\Str;
use Tests\TestCase;

/**
 * Batch 5 regression guards: manual consumption posts material cost once,
 * corrections stay on the inventory adjustment account, mode changes record
 * the responsible user and inventory endpoints enforce Batch 4 permissions.
 */
class ManualInventoryConsumptionTest extends TestCase
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

        foreach ($this->tenantStoragePaths as $path) {
            File::deleteDirectory($path);
        }

        foreach ($this->tenantDatabases as $database) {
            @unlink(database_path($database));
        }

        parent::tearDown();
    }

    public function test_manual_consumption_posts_material_cost_once_and_keeps_corrections_separate(): void
    {
        $tenant = $this->createTenant('inventory-mode-a', 'inventory-mode-a.test');
        tenancy()->initialize($tenant);

        $user = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Inventory Manager',
            'email' => 'inventory-mode-owner@example.test',
            'password' => 'secret-password',
            'is_active' => true,
            'role' => 'inventory',
        ]);
        $branch = RestaurantBranch::query()->create(['code' => 'MAIN', 'name' => 'Main Branch', 'is_active' => true]);
        $supplier = Supplier::query()->create(['code' => 'SUP-1', 'name' => 'Food Supplier', 'is_active' => true]);
        $rice = InventoryItem::query()->create([
            'sku' => 'RICE', 'name' => 'Rice', 'base_unit' => 'g', 'purchase_unit' => 'kg',
            'purchase_to_base_factor' => '1000.000000', 'reorder_level' => '100.0000', 'is_active' => true,
        ]);

        $procurement = app(ProcurementService::class);
        $po = $procurement->createPurchaseOrder($branch, $supplier, $user, [
            'lines' => [[
                'inventory_item_id' => $rice->id,
                'purchase_quantity' => '1.0000',
                'unit_cost' => '100.00',
            ]],
        ]);
        $procurement->receive($po, $user, [
            'client_receipt_id' => 'GRN-MANUAL-1',
            'lines' => [[
                'purchase_order_line_id' => $po->lines->first()->id,
                'purchase_quantity' => '1.0000',
            ]],
        ]);

        $inventory = app(InventoryService::class);
        $movement = $inventory->adjust(
            $branch,
            $rice,
            $user,
            '-300.0000',
            'COUNT-MANUAL-1',
            'Kitchen Consumption',
            asManualConsumption: true,
        );

        $retry = $inventory->adjust(
            $branch,
            $rice,
            $user,
            '-300.0000',
            'COUNT-MANUAL-1',
            'Kitchen Consumption',
            asManualConsumption: true,
        );

        $this->assertSame($movement->id, $retry->id);
        $this->assertSame('700.0000', InventoryBalance::query()->value('quantity'));
        $this->assertSame(1, StockMovement::query()->where('movement_type', StockMovement::TYPE_ADJUSTMENT)->count());

        $accounting = app(AccountingService::class);
        $cogs = $accounting->systemAccount('cost_of_goods_sold');
        $inventoryAccount = $accounting->systemAccount('inventory_asset');
        $adjustmentAccount = $accounting->systemAccount('inventory_adjustment');

        $entry = JournalEntry::query()->with('lines')->where('source_id', $movement->id)->sole();
        $this->assertSame('manual-consumption', $entry->source_action);
        $this->assertSame(JournalEntry::STATUS_POSTED, $entry->status);
        $this->assertSame('30.00', $entry->lines->firstWhere('account_id', $cogs->id)->debit);
        $this->assertSame('30.00', $entry->lines->firstWhere('account_id', $inventoryAccount->id)->credit);
        $this->assertNull($entry->lines->firstWhere('account_id', $adjustmentAccount->id));

        // A positive correction keeps the existing inventory adjustment posting.
        $correction = $inventory->adjust($branch, $rice, $user, '50.0000', 'ADJ-MANUAL-1', 'Count correction');
        $correctionEntry = JournalEntry::query()->with('lines')->where('source_id', $correction->id)->sole();
        $this->assertSame('adjustment', $correctionEntry->source_action);
        $this->assertNotNull($correctionEntry->lines->firstWhere('account_id', $adjustmentAccount->id));
    }

    public function test_module_updates_record_who_changed_the_inventory_mode(): void
    {
        $tenant = $this->createTenant('inventory-mode-b', 'inventory-mode-b.test');
        tenancy()->initialize($tenant);

        $owner = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Owner',
            'email' => 'inventory-mode-owner-b@example.test',
            'password' => 'secret-password',
            'is_active' => true,
            'role' => 'owner',
        ]);

        $settings = app(RestaurantSettingsService::class);
        $settings->put(['automatic_recipe_consumption_enabled' => false], null, $owner->id);

        $row = RestaurantSetting::query()
            ->whereNull('branch_id')
            ->where('key', 'automatic_recipe_consumption_enabled')
            ->sole();

        $this->assertSame('0', $row->value);
        $this->assertSame($owner->id, $row->updated_by_user_id);
        $this->assertFalse($settings->all()['automatic_recipe_consumption_enabled']);
    }

    public function test_inventory_endpoints_enforce_batch_four_permissions(): void
    {
        $base = 'api/'.config('restaurant.api.version', 'v1');
        $routes = collect(Route::getRoutes()->getRoutes());

        $expected = [
            ['GET', $base.'/inventory/items', 'tenant.permission:inventory.view'],
            ['POST', $base.'/inventory/items', 'tenant.permission:inventory.stock-add'],
            ['POST', $base.'/inventory/items/{inventoryItem}/adjustments', 'tenant.permission:inventory.adjustments'],
            ['GET', $base.'/inventory/movements', 'tenant.permission:inventory.view'],
            ['POST', $base.'/desktop/modules', 'tenant.permission:inventory.settings'],
        ];

        foreach ($expected as [$method, $path, $permission]) {
            $route = $routes->first(
                fn ($route) => $route->uri() === $path && in_array($method, $route->methods(), true)
            );

            $this->assertNotNull($route, "Missing {$method} {$path} in deployed tenant API contract");
            $this->assertContains($permission, $route->gatherMiddleware());
        }
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
