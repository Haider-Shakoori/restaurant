<?php

namespace Tests\Feature;

use App\Models\Bill;
use App\Models\CashierSession;
use App\Models\DailyClosing;
use App\Models\DiningArea;
use App\Models\DiningTable;
use App\Models\MenuCategory;
use App\Models\MenuItem;
use App\Models\Order;
use App\Models\RestaurantBranch;
use App\Models\Tenant;
use App\Models\TenantPayment;
use App\Models\TenantUser;
use App\Services\Tenant\BillingService;
use App\Services\Tenant\CashierService;
use App\Services\Tenant\DailyClosingService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\File;
use Illuminate\Support\Facades\Schema;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;
use Tests\TestCase;

class BillingAndClosingTest extends TestCase
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

    public function test_tenant_schema_contains_pos_and_daily_closing_tables(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        $this->assertTrue(Schema::hasTable('cashier_sessions'));
        $this->assertTrue(Schema::hasTable('bills'));
        $this->assertTrue(Schema::hasTable('bill_lines'));
        $this->assertTrue(Schema::hasTable('tenant_payments'));
        $this->assertTrue(Schema::hasTable('bill_events'));
        $this->assertTrue(Schema::hasTable('daily_closings'));
        $this->assertTrue(Schema::hasTable('daily_closing_snapshots'));
        $this->assertTrue(Schema::hasTable('daily_closing_events'));
    }

    public function test_bill_discount_and_split_payment_close_order_only_after_full_settlement(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        [$cashier, $branch, $table, $order] = $this->seedServedOrder('300.00');

        $cashiers = app(CashierService::class);
        $billing = app(BillingService::class);

        $session = $cashiers->openSession($branch, $cashier, '1000.00');
        $bill = $billing->createBill($order, $cashier);

        $this->assertSame('300.00', $bill->subtotal);
        $this->assertSame(Order::STATUS_BILLED, $order->fresh()->status);

        $bill = $billing->applyDiscount($bill, $cashier, 'percent', '10.00', 'Manager approved');

        $this->assertSame('30.00', $bill->discount_amount);
        $this->assertSame('270.00', $bill->total);
        $this->assertSame('270.00', $bill->balance_due);

        $first = $billing->addPayment($bill, $session, $cashier, [
            'client_payment_id' => 'PAY-SPLIT-001',
            'method' => 'cash',
            'amount' => '100.00',
        ]);

        $this->assertSame('100.00', $first->amount);
        $this->assertSame(Bill::STATUS_OPEN, $bill->fresh()->status);
        $this->assertSame('170.00', $bill->fresh()->balance_due);
        $this->assertSame(DiningTable::STATUS_OCCUPIED, $table->fresh()->status);

        $second = $billing->addPayment($bill->fresh(), $session, $cashier, [
            'client_payment_id' => 'PAY-SPLIT-002',
            'method' => 'card',
            'amount' => '170.00',
            'reference' => 'CARD-REF-1',
        ]);

        $this->assertSame('170.00', $second->amount);
        $this->assertSame(Bill::STATUS_PAID, $bill->fresh()->status);
        $this->assertSame('270.00', $bill->fresh()->paid_amount);
        $this->assertSame('0.00', $bill->fresh()->balance_due);
        $this->assertSame(Order::STATUS_CLOSED, $order->fresh()->status);
        $this->assertSame(DiningTable::STATUS_AVAILABLE, $table->fresh()->status);
    }

    public function test_payment_retry_is_idempotent_and_overpayment_is_rejected(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        [$cashier, $branch, , $order] = $this->seedServedOrder('200.00');

        $cashiers = app(CashierService::class);
        $billing = app(BillingService::class);

        $session = $cashiers->openSession($branch, $cashier, '0.00');
        $bill = $billing->createBill($order, $cashier);

        $first = $billing->addPayment($bill, $session, $cashier, [
            'client_payment_id' => 'PAY-IDEMPOTENT-001',
            'method' => 'cash',
            'amount' => '50.00',
        ]);

        $retry = $billing->addPayment($bill->fresh(), $session, $cashier, [
            'client_payment_id' => 'PAY-IDEMPOTENT-001',
            'method' => 'cash',
            'amount' => '50.00',
        ]);

        $this->assertSame($first->id, $retry->id);
        $this->assertSame(1, TenantPayment::query()->count());

        $this->expectException(ValidationException::class);

        $billing->addPayment($bill->fresh(), $session, $cashier, [
            'client_payment_id' => 'PAY-OVER-001',
            'method' => 'cash',
            'amount' => '151.00',
        ]);
    }

    public function test_cashier_close_and_daily_close_create_immutable_revisions(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        [$cashier, $branch, , $order] = $this->seedServedOrder('500.00');

        $cashiers = app(CashierService::class);
        $billing = app(BillingService::class);
        $closings = app(DailyClosingService::class);

        $session = $cashiers->openSession($branch, $cashier, '100.00');
        $bill = $billing->createBill($order, $cashier);

        $billing->addPayment($bill, $session, $cashier, [
            'client_payment_id' => 'PAY-CLOSE-001',
            'method' => 'cash',
            'amount' => '300.00',
        ]);

        $billing->addPayment($bill->fresh(), $session, $cashier, [
            'client_payment_id' => 'PAY-CLOSE-002',
            'method' => 'bank',
            'amount' => '200.00',
            'reference' => 'BANK-001',
        ]);

        $closedSession = $cashiers->closeSession($session, $cashier, '395.00');

        $this->assertSame(CashierSession::STATUS_CLOSED, $closedSession->status);
        $this->assertSame('400.00', $closedSession->expected_cash);
        $this->assertSame('-5.00', $closedSession->cash_variance);

        $date = now()->format('Y-m-d');
        $closing = $closings->finalize($branch, $date, $cashier);

        $this->assertSame(DailyClosing::STATUS_FINALIZED, $closing->status);
        $this->assertCount(1, $closing->snapshots);

        $snapshot = $closing->snapshots->first();

        $this->assertSame(1, $snapshot->version);
        $this->assertSame('500.00', $snapshot->gross_sales);
        $this->assertSame('500.00', $snapshot->net_sales);
        $this->assertSame('500.00', $snapshot->payments_total);
        $this->assertSame('300.00', $snapshot->cash_payments);
        $this->assertSame('200.00', $snapshot->bank_payments);
        $this->assertSame('400.00', $snapshot->expected_cash);
        $this->assertSame('395.00', $snapshot->declared_cash);
        $this->assertSame('-5.00', $snapshot->cash_variance);

        $reopened = $closings->reopen($closing, $cashier, 'Correcting closing note');

        $this->assertSame(DailyClosing::STATUS_REOPENED, $reopened->status);
        $this->assertCount(1, $reopened->snapshots);

        $refinalized = $closings->finalize($branch, $date, $cashier);

        $this->assertSame(DailyClosing::STATUS_FINALIZED, $refinalized->status);
        $this->assertCount(2, $refinalized->snapshots);
        $this->assertSame([1, 2], $refinalized->snapshots->pluck('version')->all());
    }

    public function test_daily_close_is_blocked_by_open_cashier_session(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        tenancy()->initialize($tenant);

        [$cashier, $branch] = $this->seedServedOrder('100.00');

        app(CashierService::class)->openSession($branch, $cashier, '0.00');

        $this->expectException(ValidationException::class);

        app(DailyClosingService::class)->finalize(
            $branch,
            now()->format('Y-m-d'),
            $cashier,
        );
    }

    /**
     * @return array{TenantUser, RestaurantBranch, DiningTable, Order}
     */
    private function seedServedOrder(string $total): array
    {
        $cashier = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Cashier',
            'email' => Str::ulid().'@restaurant.test',
            'password' => 'secret-password',
            'is_active' => true,
            'role' => 'cashier',
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
            'waiter_id' => $cashier->id,
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

        return [$cashier, $branch, $table, $order];
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
