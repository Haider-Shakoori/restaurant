<?php

declare(strict_types=1);

use App\Http\Controllers\Tenant\AccountingReportController;
use App\Http\Controllers\Tenant\ApplyBillDiscountController;
use App\Http\Controllers\Tenant\BillController;
use App\Http\Controllers\Tenant\BillPaymentController;
use App\Http\Controllers\Tenant\BootstrapController;
use App\Http\Controllers\Tenant\CashierSessionController;
use App\Http\Controllers\Tenant\ChartAccountController;
use App\Http\Controllers\Tenant\DailyClosingController;
use App\Http\Controllers\Tenant\DiningTableController;
use App\Http\Controllers\Tenant\InventoryItemController;
use App\Http\Controllers\Tenant\JournalEntryController;
use App\Http\Controllers\Tenant\KitchenRouteController;
use App\Http\Controllers\Tenant\KitchenStationController;
use App\Http\Controllers\Tenant\KitchenTicketController;
use App\Http\Controllers\Tenant\LicenseActivationController;
use App\Http\Controllers\Tenant\LicensePublicKeyController;
use App\Http\Controllers\Tenant\MenuController;
use App\Http\Controllers\Tenant\OfflineLeaseController;
use App\Http\Controllers\Tenant\OperatingExpenseController;
use App\Http\Controllers\Tenant\OrderController;
use App\Http\Controllers\Tenant\OrderItemController;
use App\Http\Controllers\Tenant\PurchaseOrderController;
use App\Http\Controllers\Tenant\ReadyKitchenTicketController;
use App\Http\Controllers\Tenant\RecipeController;
use App\Http\Controllers\Tenant\ServeOrderController;
use App\Http\Controllers\Tenant\StartKitchenTicketController;
use App\Http\Controllers\Tenant\StockMovementController;
use App\Http\Controllers\Tenant\SubmitOrderController;
use App\Http\Controllers\Tenant\SubscriptionStatusController;
use App\Http\Controllers\Tenant\SupplierController;
use App\Http\Controllers\Tenant\SupplierPaymentController;
use App\Http\Controllers\Tenant\SyncBootstrapController;
use App\Http\Controllers\Tenant\SyncPullController;
use App\Http\Controllers\Tenant\SyncPushController;
use App\Http\Controllers\Tenant\TenantAuthController;
use App\Http\Middleware\InitializeRestaurantTenancy;
use Illuminate\Support\Facades\Route;

$tenantMiddleware = [
    InitializeRestaurantTenancy::class,
];

Route::middleware(['web', ...$tenantMiddleware])->group(function (): void {
    Route::get('/', function () {
        return response()->json([
            'service' => 'BusinessOS Restaurant',
            'tenant_id' => tenant('id'),
        ]);
    })->middleware('subscription.active')->name('tenant.home');
});

Route::middleware($tenantMiddleware)
    ->prefix('api/'.config('restaurant.api.version', 'v1'))
    ->group(function (): void {
        Route::get('/health', function () {
            return response()->json([
                'status' => 'ok',
                'service' => 'BusinessOS Restaurant Tenant',
                'tenant_id' => tenant('id'),
            ]);
        })->name('tenant.api.health');

        Route::get('/subscription', SubscriptionStatusController::class)
            ->name('tenant.api.subscription');

        Route::get('/license/public-key', LicensePublicKeyController::class)
            ->name('tenant.api.license.public-key');

        Route::post('/license/activate', LicenseActivationController::class)
            ->name('tenant.api.license.activate');

        Route::post('/license/lease', OfflineLeaseController::class)
            ->name('tenant.api.license.lease');

        Route::post('/auth/login', [TenantAuthController::class, 'login'])
            ->middleware('throttle:10,1')
            ->name('tenant.api.auth.login');

        Route::middleware(['auth:sanctum', 'subscription.active'])->group(function (): void {
            Route::post('/auth/logout', [TenantAuthController::class, 'logout'])
                ->name('tenant.api.auth.logout');

            Route::get('/bootstrap', BootstrapController::class)
                ->name('tenant.api.bootstrap');

            Route::middleware('tenant.role:owner,admin,manager,waiter')->group(function (): void {
                Route::get('/sync/bootstrap', SyncBootstrapController::class)
                    ->name('tenant.api.sync.bootstrap');

                Route::post('/sync/push', SyncPushController::class)
                    ->name('tenant.api.sync.push');

                Route::get('/sync/pull', SyncPullController::class)
                    ->name('tenant.api.sync.pull');
            });

            Route::get('/menu', [MenuController::class, 'index'])
                ->name('tenant.api.menu');

            Route::get('/tables', [DiningTableController::class, 'index'])
                ->name('tenant.api.tables');

            Route::get('/kitchen/stations', [KitchenStationController::class, 'index'])
                ->name('tenant.api.kitchen.stations.index');

            Route::middleware('tenant.role:owner,admin,manager')->group(function (): void {
                Route::post('/kitchen/stations', [KitchenStationController::class, 'store'])
                    ->name('tenant.api.kitchen.stations.store');

                Route::post('/menu/items/{menuItem}/kitchen-route', [KitchenRouteController::class, 'store'])
                    ->name('tenant.api.kitchen.routes.store');

                Route::post('/daily-closings/{dailyClosing}/reopen', [DailyClosingController::class, 'reopen'])
                    ->name('tenant.api.daily-closings.reopen');
            });

            Route::middleware('tenant.role:owner,admin,manager,kitchen')->group(function (): void {
                Route::get('/kitchen/tickets', [KitchenTicketController::class, 'index'])
                    ->name('tenant.api.kitchen.tickets.index');

                Route::get('/kitchen/tickets/{kitchenTicket}', [KitchenTicketController::class, 'show'])
                    ->name('tenant.api.kitchen.tickets.show');

                Route::post('/kitchen/tickets/{kitchenTicket}/start', StartKitchenTicketController::class)
                    ->name('tenant.api.kitchen.tickets.start');

                Route::post('/kitchen/tickets/{kitchenTicket}/ready', ReadyKitchenTicketController::class)
                    ->name('tenant.api.kitchen.tickets.ready');
            });

            Route::middleware('tenant.role:owner,admin,manager,waiter,cashier')->group(function (): void {
                Route::get('/orders', [OrderController::class, 'index'])
                    ->name('tenant.api.orders.index');

                Route::post('/orders', [OrderController::class, 'store'])
                    ->name('tenant.api.orders.store');

                Route::get('/orders/{order}', [OrderController::class, 'show'])
                    ->name('tenant.api.orders.show');

                Route::post('/orders/{order}/items', [OrderItemController::class, 'store'])
                    ->name('tenant.api.orders.items.store');

                Route::post('/orders/{order}/submit', SubmitOrderController::class)
                    ->name('tenant.api.orders.submit');

                Route::post('/orders/{order}/serve', ServeOrderController::class)
                    ->name('tenant.api.orders.serve');
            });

            Route::middleware('tenant.role:owner,admin,manager,cashier')->group(function (): void {
                Route::get('/cashier/sessions', [CashierSessionController::class, 'index'])
                    ->name('tenant.api.cashier.sessions.index');

                Route::post('/cashier/sessions', [CashierSessionController::class, 'store'])
                    ->name('tenant.api.cashier.sessions.store');

                Route::post('/cashier/sessions/{cashierSession}/close', [CashierSessionController::class, 'close'])
                    ->name('tenant.api.cashier.sessions.close');

                Route::get('/pos/bills', [BillController::class, 'index'])
                    ->name('tenant.api.bills.index');

                Route::post('/orders/{order}/bill', [BillController::class, 'store'])
                    ->name('tenant.api.bills.store');

                Route::get('/pos/bills/{bill}', [BillController::class, 'show'])
                    ->name('tenant.api.bills.show');

                Route::post('/pos/bills/{bill}/discount', ApplyBillDiscountController::class)
                    ->name('tenant.api.bills.discount');

                Route::post('/pos/bills/{bill}/payments', [BillPaymentController::class, 'store'])
                    ->name('tenant.api.bills.payments.store');

                Route::get('/daily-closings', [DailyClosingController::class, 'index'])
                    ->name('tenant.api.daily-closings.index');

                Route::get('/daily-closings/{dailyClosing}', [DailyClosingController::class, 'show'])
                    ->name('tenant.api.daily-closings.show');

                Route::post('/daily-closings/finalize', [DailyClosingController::class, 'finalize'])
                    ->name('tenant.api.daily-closings.finalize');
            });

            Route::middleware('tenant.role:owner,admin,manager,inventory')->group(function (): void {
                Route::get('/inventory/items', [InventoryItemController::class, 'index'])
                    ->name('tenant.api.inventory.items.index');

                Route::post('/inventory/items', [InventoryItemController::class, 'store'])
                    ->name('tenant.api.inventory.items.store');

                Route::post('/inventory/items/{inventoryItem}/adjustments', [InventoryItemController::class, 'adjust'])
                    ->name('tenant.api.inventory.items.adjust');

                Route::get('/inventory/movements', [StockMovementController::class, 'index'])
                    ->name('tenant.api.inventory.movements.index');

                Route::get('/suppliers', [SupplierController::class, 'index'])
                    ->name('tenant.api.suppliers.index');

                Route::post('/suppliers', [SupplierController::class, 'store'])
                    ->name('tenant.api.suppliers.store');

                Route::get('/purchasing/orders', [PurchaseOrderController::class, 'index'])
                    ->name('tenant.api.purchasing.orders.index');

                Route::post('/purchasing/orders', [PurchaseOrderController::class, 'store'])
                    ->name('tenant.api.purchasing.orders.store');

                Route::get('/purchasing/orders/{purchaseOrder}', [PurchaseOrderController::class, 'show'])
                    ->name('tenant.api.purchasing.orders.show');

                Route::post('/purchasing/orders/{purchaseOrder}/receive', [PurchaseOrderController::class, 'receive'])
                    ->name('tenant.api.purchasing.orders.receive');

                Route::get('/recipes', [RecipeController::class, 'index'])
                    ->name('tenant.api.recipes.index');

                Route::post('/menu/items/{menuItem}/recipes', [RecipeController::class, 'store'])
                    ->name('tenant.api.recipes.store');
            });

            Route::middleware('tenant.role:owner,admin,manager,accountant')->group(function (): void {
                Route::get('/accounting/accounts', [ChartAccountController::class, 'index'])
                    ->name('tenant.api.accounting.accounts.index');

                Route::get('/accounting/journals', [JournalEntryController::class, 'index'])
                    ->name('tenant.api.accounting.journals.index');

                Route::get('/accounting/journals/{journalEntry}', [JournalEntryController::class, 'show'])
                    ->name('tenant.api.accounting.journals.show');

                Route::get('/accounting/expenses', [OperatingExpenseController::class, 'index'])
                    ->name('tenant.api.accounting.expenses.index');

                Route::post('/accounting/expenses', [OperatingExpenseController::class, 'store'])
                    ->name('tenant.api.accounting.expenses.store');

                Route::get('/accounting/supplier-payments', [SupplierPaymentController::class, 'index'])
                    ->name('tenant.api.accounting.supplier-payments.index');

                Route::post('/accounting/supplier-payments', [SupplierPaymentController::class, 'store'])
                    ->name('tenant.api.accounting.supplier-payments.store');

                Route::get('/accounting/reports/trial-balance', [AccountingReportController::class, 'trialBalance'])
                    ->name('tenant.api.accounting.reports.trial-balance');

                Route::get('/accounting/reports/income-statement', [AccountingReportController::class, 'incomeStatement'])
                    ->name('tenant.api.accounting.reports.income-statement');

                Route::get('/accounting/reports/balance-sheet', [AccountingReportController::class, 'balanceSheet'])
                    ->name('tenant.api.accounting.reports.balance-sheet');

                Route::get('/accounting/reports/receivables', [AccountingReportController::class, 'receivables'])
                    ->name('tenant.api.accounting.reports.receivables');

                Route::get('/accounting/reports/payables', [AccountingReportController::class, 'payables'])
                    ->name('tenant.api.accounting.reports.payables');

                Route::get('/accounting/reports/management-summary', [AccountingReportController::class, 'managementSummary'])
                    ->name('tenant.api.accounting.reports.management-summary');

                Route::get('/accounting/reports/ledger/{chartAccount}', [AccountingReportController::class, 'ledger'])
                    ->name('tenant.api.accounting.reports.ledger');
            });

            Route::middleware('tenant.role:owner,admin,accountant')->group(function (): void {
                Route::post('/accounting/accounts', [ChartAccountController::class, 'store'])
                    ->name('tenant.api.accounting.accounts.store');

                Route::post('/accounting/journals', [JournalEntryController::class, 'store'])
                    ->name('tenant.api.accounting.journals.store');

                Route::post('/accounting/journals/{journalEntry}/reverse', [JournalEntryController::class, 'reverse'])
                    ->name('tenant.api.accounting.journals.reverse');
            });
        });
    });
