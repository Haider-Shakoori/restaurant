<?php

namespace Tests\Feature;

use App\Models\ChartAccount;
use App\Models\DiningArea;
use App\Models\DiningTable;
use App\Models\InventoryItem;
use App\Models\InventoryValuation;
use App\Models\JournalEntry;
use App\Models\MenuCategory;
use App\Models\MenuItem;
use App\Models\Order;
use App\Models\RestaurantBranch;
use App\Models\Supplier;
use App\Models\Tenant;
use App\Models\TenantUser;
use App\Services\Tenant\AccountingService;
use App\Services\Tenant\BillingService;
use App\Services\Tenant\CashierService;
use App\Services\Tenant\ExpenseService;
use App\Services\Tenant\InventoryService;
use App\Services\Tenant\ProcurementService;
use App\Services\Tenant\RecipeService;
use App\Services\Tenant\ReportingService;
use App\Services\Tenant\SupplierPaymentService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\File;
use Illuminate\Support\Facades\Schema;
use Illuminate\Support\Str;
use Tests\TestCase;

class AccountingReportingTest extends TestCase
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

    public function test_tenant_schema_contains_accounting_tables(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        $this->assertTrue(Schema::hasTable('chart_accounts'));
        $this->assertTrue(Schema::hasTable('journal_entries'));
        $this->assertTrue(Schema::hasTable('journal_lines'));
        $this->assertTrue(Schema::hasTable('inventory_valuations'));
        $this->assertTrue(Schema::hasTable('operating_expenses'));
        $this->assertTrue(Schema::hasTable('supplier_payments'));
    }

    public function test_bill_discount_and_payment_post_balanced_double_entry_and_profit_report(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        [$user, $branch, $table, $order] = $this->seedServedOrder('300.00');

        $session = app(CashierService::class)->openSession($branch, $user, '0.00');
        $billing = app(BillingService::class);

        $bill = $billing->createBill($order, $user);
        $bill = $billing->applyDiscount($bill, $user, 'percent', '10.00', 'Manager discount');
        $billing->addPayment($bill, $session, $user, [
            'client_payment_id' => 'ACC-PAY-001',
            'method' => 'cash',
            'amount' => '270.00',
        ]);

        $this->assertSame(3, JournalEntry::query()->count());

        foreach (JournalEntry::query()->with('lines')->get() as $entry) {
            $this->assertSame(
                $entry->lines->sum(fn ($line) => (float) $line->debit),
                $entry->lines->sum(fn ($line) => (float) $line->credit),
            );
        }

        $reports = app(ReportingService::class);
        $income = $reports->incomeStatement($branch->id, now()->format('Y-m-d'), now()->format('Y-m-d'));
        $trial = collect($reports->trialBalance($branch->id, now()->format('Y-m-d'), now()->format('Y-m-d')));

        $this->assertSame('270.00', $income['revenue']);
        $this->assertSame('0.00', $income['expenses']);
        $this->assertSame('270.00', $income['net_profit']);

        $cash = app(AccountingService::class)->systemAccount('cash');
        $ar = app(AccountingService::class)->systemAccount('accounts_receivable');

        $this->assertSame('270.00', $trial->firstWhere('account_id', $cash->id)['balance']);
        $this->assertSame('0.00', $trial->firstWhere('account_id', $ar->id)['balance']);
        $this->assertSame(DiningTable::STATUS_AVAILABLE, $table->fresh()->status);
    }

    public function test_goods_receipt_and_recipe_consumption_update_valuation_ap_and_cogs(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        [$user, $branch] = $this->seedBranchUser();
        $supplier = Supplier::query()->create([
            'code' => 'SUP-001',
            'name' => 'Food Supplier',
            'is_active' => true,
        ]);
        $rice = InventoryItem::query()->create([
            'sku' => 'RICE',
            'name' => 'Rice',
            'base_unit' => 'g',
            'purchase_unit' => 'kg',
            'purchase_to_base_factor' => '1000.000000',
            'reorder_level' => '100.0000',
            'is_active' => true,
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
            'client_receipt_id' => 'ACC-GRN-001',
            'lines' => [[
                'purchase_order_line_id' => $po->lines->first()->id,
                'purchase_quantity' => '1.0000',
            ]],
        ]);

        $valuation = InventoryValuation::query()->firstOrFail();
        $this->assertSame('1000.0000', $valuation->quantity);
        $this->assertSame('100.00', $valuation->value);
        $this->assertSame('0.100000', $valuation->average_unit_cost);

        $category = MenuCategory::query()->create([
            'name' => 'Main',
            'sort_order' => 1,
            'is_active' => true,
        ]);
        $meal = MenuItem::query()->create([
            'menu_category_id' => $category->id,
            'sku' => 'MEAL-1',
            'name' => 'Rice Meal',
            'price' => '250.00',
            'is_available' => true,
            'sort_order' => 1,
        ]);
        app(RecipeService::class)->createVersion($branch, $meal, [
            'items' => [[
                'inventory_item_id' => $rice->id,
                'quantity_base' => '200.0000',
            ]],
        ]);

        $area = DiningArea::query()->create([
            'branch_id' => $branch->id,
            'name' => 'Hall',
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
        $order = Order::query()->create([
            'client_order_id' => (string) Str::ulid(),
            'dining_table_id' => $table->id,
            'waiter_id' => $user->id,
            'status' => Order::STATUS_READY,
            'guest_count' => 1,
            'subtotal' => '250.00',
            'total' => '250.00',
            'opened_at' => now(),
            'submitted_at' => now(),
        ]);
        $order->items()->create([
            'menu_item_id' => $meal->id,
            'client_line_id' => (string) Str::ulid(),
            'item_name' => $meal->name,
            'unit_price' => '250.00',
            'quantity' => 1,
            'line_total' => '250.00',
            'status' => 'ready',
        ]);

        app(InventoryService::class)->consumeOrder($order, $user);

        $valuation = $valuation->fresh();
        $this->assertSame('800.0000', $valuation->quantity);
        $this->assertSame('80.00', $valuation->value);

        $reports = app(ReportingService::class);
        $payables = $reports->payables($branch->id, now()->format('Y-m-d'));
        $income = $reports->incomeStatement($branch->id, now()->format('Y-m-d'), now()->format('Y-m-d'));

        $this->assertSame('100.00', $payables['total']);
        $this->assertSame('20.00', $income['expenses']);
        $this->assertSame('-20.00', $income['net_profit']);
    }

    public function test_supplier_payment_and_operating_expense_flow_into_ap_and_profit_reports(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        [$user, $branch] = $this->seedBranchUser();
        $supplier = Supplier::query()->create([
            'code' => 'SUP-001',
            'name' => 'Supplier',
            'is_active' => true,
        ]);
        $item = InventoryItem::query()->create([
            'sku' => 'OIL',
            'name' => 'Oil',
            'base_unit' => 'ml',
            'purchase_unit' => 'liter',
            'purchase_to_base_factor' => '1000.000000',
            'reorder_level' => '0.0000',
            'is_active' => true,
        ]);

        $po = app(ProcurementService::class)->createPurchaseOrder($branch, $supplier, $user, [
            'lines' => [[
                'inventory_item_id' => $item->id,
                'purchase_quantity' => '1.0000',
                'unit_cost' => '100.00',
            ]],
        ]);
        app(ProcurementService::class)->receive($po, $user, [
            'client_receipt_id' => 'AP-GRN-001',
            'lines' => [[
                'purchase_order_line_id' => $po->lines->first()->id,
                'purchase_quantity' => '1.0000',
            ]],
        ]);

        $accounting = app(AccountingService::class);
        $bank = $accounting->systemAccount('bank');
        $expenseAccount = $accounting->systemAccount('operating_expense');

        app(SupplierPaymentService::class)->post(
            $branch,
            $supplier,
            $bank,
            $user,
            [
                'client_supplier_payment_id' => 'SP-CLIENT-001',
                'amount' => '40.00',
                'payment_date' => now()->format('Y-m-d'),
            ],
        );

        app(ExpenseService::class)->post(
            $branch,
            $user,
            $expenseAccount,
            $bank,
            [
                'client_expense_id' => 'EXP-CLIENT-001',
                'description' => 'Internet',
                'amount' => '25.00',
                'expense_date' => now()->format('Y-m-d'),
            ],
        );

        $reports = app(ReportingService::class);

        $this->assertSame(
            '60.00',
            $reports->payables($branch->id, now()->format('Y-m-d'))['total'],
        );
        $this->assertSame(
            '25.00',
            $reports->incomeStatement(
                $branch->id,
                now()->format('Y-m-d'),
                now()->format('Y-m-d'),
            )['expenses'],
        );
    }

    public function test_manual_journal_reversal_nets_to_zero_without_deleting_history(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        [$user, $branch] = $this->seedBranchUser();
        $accounting = app(AccountingService::class);
        $cash = $accounting->systemAccount('cash');
        $equity = $accounting->systemAccount('owner_equity');

        $entry = $accounting->post(
            $branch->id,
            $user,
            'manual_journal',
            'OPENING-001',
            'posted',
            'Opening capital',
            now()->format('Y-m-d'),
            [
                ['account_id' => $cash->id, 'debit' => '100.00'],
                ['account_id' => $equity->id, 'credit' => '100.00'],
            ],
            'manual-journal:OPENING-001',
        );

        $reversal = $accounting->reverse($entry, $user, 'Correction');

        $this->assertSame(JournalEntry::STATUS_REVERSED, $entry->fresh()->status);
        $this->assertSame($entry->id, $reversal->reversal_of_id);
        $this->assertSame(2, JournalEntry::query()->count());

        $trial = collect(app(ReportingService::class)->trialBalance(
            $branch->id,
            now()->format('Y-m-d'),
            now()->format('Y-m-d'),
        ));

        $this->assertSame('0.00', $trial->firstWhere('account_id', $cash->id)['balance']);
        $this->assertSame('0.00', $trial->firstWhere('account_id', $equity->id)['balance']);
    }

    /**
     * @return array{TenantUser, RestaurantBranch}
     */
    private function seedBranchUser(): array
    {
        $user = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Accountant',
            'email' => Str::ulid().'@restaurant.test',
            'password' => 'secret-password',
            'is_active' => true,
            'role' => 'accountant',
        ]);

        $branch = RestaurantBranch::query()->create([
            'code' => 'MAIN',
            'name' => 'Main Branch',
            'is_active' => true,
        ]);

        return [$user, $branch];
    }

    /**
     * @return array{TenantUser, RestaurantBranch, DiningTable, Order}
     */
    private function seedServedOrder(string $total): array
    {
        [$user, $branch] = $this->seedBranchUser();
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
        $category = MenuCategory::query()->create([
            'name' => 'Menu',
            'sort_order' => 1,
            'is_active' => true,
        ]);
        $item = MenuItem::query()->create([
            'menu_category_id' => $category->id,
            'sku' => 'FOOD-001',
            'name' => 'Meal',
            'price' => $total,
            'is_available' => true,
            'sort_order' => 1,
        ]);
        $order = Order::query()->create([
            'client_order_id' => (string) Str::ulid(),
            'dining_table_id' => $table->id,
            'waiter_id' => $user->id,
            'status' => Order::STATUS_SERVED,
            'guest_count' => 1,
            'subtotal' => $total,
            'total' => $total,
            'opened_at' => now(),
            'submitted_at' => now(),
            'served_at' => now(),
        ]);
        $order->items()->create([
            'menu_item_id' => $item->id,
            'client_line_id' => (string) Str::ulid(),
            'item_name' => $item->name,
            'unit_price' => $total,
            'quantity' => 1,
            'line_total' => $total,
            'status' => 'served',
        ]);

        return [$user, $branch, $table, $order];
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
