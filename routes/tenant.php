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
use App\Http\Controllers\Tenant\DesktopReconciliationController;
use App\Http\Controllers\Tenant\DiningTableController;
use App\Http\Controllers\Tenant\ExpoController;
use App\Http\Controllers\Tenant\FireOrderCourseController;
use App\Http\Controllers\Tenant\InventoryItemController;
use App\Http\Controllers\Tenant\JournalEntryController;
use App\Http\Controllers\Tenant\KitchenPerformanceReportController;
use App\Http\Controllers\Tenant\KitchenRouteController;
use App\Http\Controllers\Tenant\KitchenStationController;
use App\Http\Controllers\Tenant\KitchenTicketController;
use App\Http\Controllers\Tenant\LicenseActivationController;
use App\Http\Controllers\Tenant\LicensePublicKeyController;
use App\Http\Controllers\Tenant\MenuController;
use App\Http\Controllers\Tenant\MenuItemImageController;
use App\Http\Controllers\Tenant\MergeOrdersController;
use App\Http\Controllers\Tenant\MoveOrderItemController;
use App\Http\Controllers\Tenant\OfflineLeaseController;
use App\Http\Controllers\Tenant\OperatingExpenseController;
use App\Http\Controllers\Tenant\OrderController;
use App\Http\Controllers\Tenant\OrderItemController;
use App\Http\Controllers\Tenant\PurchaseOrderController;
use App\Http\Controllers\Tenant\ReadyKitchenTicketController;
use App\Http\Controllers\Tenant\ReadyKitchenTicketItemController;
use App\Http\Controllers\Tenant\RecallKitchenTicketItemController;
use App\Http\Controllers\Tenant\RecipeController;
use App\Http\Controllers\Tenant\RecordProductionWasteController;
use App\Http\Controllers\Tenant\RefireKitchenTicketItemController;
use App\Http\Controllers\Tenant\RestaurantSettingsController;
use App\Http\Controllers\Tenant\ServeOrderController;
use App\Http\Controllers\Tenant\StartKitchenTicketController;
use App\Http\Controllers\Tenant\StartKitchenTicketItemController;
use App\Http\Controllers\Tenant\StockMovementController;
use App\Http\Controllers\Tenant\SubmitOrderController;
use App\Http\Controllers\Tenant\SubscriptionStatusController;
use App\Http\Controllers\Tenant\SupplierController;
use App\Http\Controllers\Tenant\SupplierPaymentController;
use App\Http\Controllers\Tenant\SyncBootstrapController;
use App\Http\Controllers\Tenant\SyncPullController;
use App\Http\Controllers\Tenant\SyncPushController;
use App\Http\Controllers\Tenant\TenantAuthController;
use App\Http\Controllers\Tenant\TenantDeviceController;
use App\Http\Controllers\Tenant\TenantDesktopUsersController;
use App\Http\Controllers\Tenant\TenantDesktopManagementController;
use App\Http\Controllers\Tenant\TenantPortalController;
use App\Http\Controllers\Tenant\TenantPortalSetupController;
use App\Http\Controllers\Tenant\TenantWebAuthController;
use App\Http\Controllers\Tenant\TenantWebKitchenController;
use App\Http\Controllers\Tenant\TenantWebOrderController;
use App\Http\Controllers\Tenant\TransferOrderTableController;
use App\Http\Controllers\Tenant\VoidKitchenTicketItemController;
use App\Http\Controllers\Tenant\WaiterPairingController;
use App\Http\Middleware\EnsureTenantSubscriptionActive;
use App\Http\Middleware\InitializeRestaurantTenancy;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Route;

$tenantMiddleware = [
    InitializeRestaurantTenancy::class,
];

Route::middleware($tenantMiddleware)
    ->get('/media/menu-items/{menuItem}', MenuItemImageController::class)
    ->name('tenant.media.menu-item');

Route::middleware(['web', ...$tenantMiddleware, 'tenant.web.guard'])->group(function (): void {
    Route::get('/login', [TenantWebAuthController::class, 'create'])->name('tenant.web.login');
    Route::post('/login', [TenantWebAuthController::class, 'store'])
        ->middleware('throttle:10,1')
        ->name('tenant.web.login.store');

    Route::get('/', function (Request $request, EnsureTenantSubscriptionActive $subscription) {
        if (! $request->expectsJson()) {
            return auth('tenant')->check() ? redirect('/dashboard') : redirect('/login');
        }

        return $subscription->handle($request, fn () => response()->json([
            'service' => 'BusinessOS Restaurant',
            'tenant_id' => tenant('id'),
        ]));
    })->name('tenant.home');

    Route::middleware('auth:tenant')->group(function (): void {
        Route::post('/logout', [TenantWebAuthController::class, 'destroy'])->name('tenant.web.logout');

        Route::middleware('subscription.active')->group(function (): void {
            Route::get('/dashboard', [TenantPortalController::class, 'dashboard'])->name('tenant.web.dashboard');
            Route::get('/menu', [TenantPortalController::class, 'menu'])->name('tenant.web.menu');

            Route::middleware('tenant.role:owner,admin,manager,waiter')->group(function (): void {
                Route::get('/tables', [TenantPortalController::class, 'tables'])->name('tenant.web.tables');
            });

            Route::middleware('tenant.role:owner,admin,manager,waiter,cashier')->group(function (): void {
                Route::get('/orders', [TenantPortalController::class, 'orders'])->name('tenant.web.orders');
                Route::post('/orders/take', [TenantWebOrderController::class, 'store'])->name('tenant.web.orders.take');
            });

            Route::middleware('tenant.role:owner,admin,manager,kitchen')->group(function (): void {
                Route::get('/kitchen', [TenantPortalController::class, 'kitchen'])->name('tenant.web.kitchen');
                Route::post('/kitchen/items/{kitchenTicketItem}/start', [TenantWebKitchenController::class, 'start'])
                    ->name('tenant.web.kitchen.items.start');
                Route::post('/kitchen/items/{kitchenTicketItem}/ready', [TenantWebKitchenController::class, 'ready'])
                    ->name('tenant.web.kitchen.items.ready');
                Route::post('/kitchen/items/{kitchenTicketItem}/void', [TenantWebKitchenController::class, 'void'])
                    ->name('tenant.web.kitchen.items.void');
                Route::post('/kitchen/items/{kitchenTicketItem}/recall', [TenantWebKitchenController::class, 'recall'])
                    ->name('tenant.web.kitchen.items.recall');
                Route::post('/kitchen/items/{kitchenTicketItem}/refire', [TenantWebKitchenController::class, 'refire'])
                    ->name('tenant.web.kitchen.items.refire');
                Route::post('/kitchen/items/{kitchenTicketItem}/waste', [TenantWebKitchenController::class, 'waste'])
                    ->name('tenant.web.kitchen.items.waste');
            });

            Route::middleware('tenant.role:owner,admin,manager,cashier')->group(function (): void {
                Route::get('/pos', [TenantPortalController::class, 'pos'])->name('tenant.web.pos');
                Route::get('/daily-closing', [TenantPortalController::class, 'closing'])->name('tenant.web.closing');
            });

            Route::middleware('tenant.role:owner,admin,manager,inventory')->group(function (): void {
                Route::get('/inventory', [TenantPortalController::class, 'inventory'])->name('tenant.web.inventory');
                Route::get('/purchasing', [TenantPortalController::class, 'purchasing'])->name('tenant.web.purchasing');
                Route::post('/purchasing/orders', [PurchaseOrderController::class, 'store'])->name('tenant.web.purchasing.orders.store');
                Route::post('/purchasing/orders/{purchaseOrder}/receive', [PurchaseOrderController::class, 'receive'])->name('tenant.web.purchasing.orders.receive');
            });

            Route::middleware('tenant.role:owner,admin,manager')->group(function (): void {
                Route::get('/accounting', [TenantPortalController::class, 'accounting'])->name('tenant.web.accounting');
            });

            Route::middleware('tenant.role:owner,admin')->group(function (): void {
                Route::get('/users', [TenantPortalController::class, 'users'])->name('tenant.web.users');
                Route::get('/settings', [TenantPortalController::class, 'settings'])->name('tenant.web.settings');
                Route::post('/settings/devices/{deviceActivation}/revoke', [TenantDeviceController::class, 'revoke'])
                    ->name('tenant.web.devices.revoke');
                Route::post('/settings/restaurant', [RestaurantSettingsController::class, 'update'])
                    ->name('tenant.web.settings.restaurant.update');

                Route::post('/setup/branch', [TenantPortalSetupController::class, 'branch']);
                Route::post('/setup/area', [TenantPortalSetupController::class, 'area']);
                Route::post('/setup/station', [TenantPortalSetupController::class, 'station']);
                Route::post('/setup/user', [TenantPortalSetupController::class, 'user']);
                Route::patch('/users/{user}', [TenantPortalSetupController::class, 'updateUser'])->name('tenant.web.users.update');
            });

            Route::middleware('tenant.role:owner,admin,manager')->group(function (): void {
                Route::post('/setup/table', [TenantPortalSetupController::class, 'table']);
                Route::post('/setup/menu/category', [TenantPortalSetupController::class, 'category']);
                Route::post('/setup/menu/item', [TenantPortalSetupController::class, 'menuItem']);
                Route::post('/setup/menu/item/{menuItem}/image', [TenantPortalSetupController::class, 'menuItemImage']);
            });

            Route::middleware('tenant.role:owner,admin,manager,inventory')->group(function (): void {
                Route::post('/setup/inventory/item', [TenantPortalSetupController::class, 'inventoryItem']);
                Route::post('/setup/supplier', [TenantPortalSetupController::class, 'supplier']);
            });
        });
    });
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

        Route::post('/pairing-tokens/redeem', [WaiterPairingController::class, 'redeem'])
            ->middleware('throttle:10,1')
            ->name('tenant.api.pairing.redeem');

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

            Route::middleware('tenant.role:owner,admin,manager')->group(function (): void {
                Route::get('/desktop/management', [TenantDesktopManagementController::class, 'index']);
                Route::post('/desktop/management/categories', [TenantDesktopManagementController::class, 'category']);
                Route::patch('/desktop/management/categories/{menuCategory}', [TenantDesktopManagementController::class, 'category']);
                Route::post('/desktop/management/menu-items', [TenantDesktopManagementController::class, 'menuItem']);
                Route::patch('/desktop/management/menu-items/{menuItem}', [TenantDesktopManagementController::class, 'menuItem']);
                Route::post('/desktop/management/menu-items/{menuItem}/image', [TenantDesktopManagementController::class, 'image']);
                Route::post('/desktop/management/areas', [TenantDesktopManagementController::class, 'area']);
                Route::patch('/desktop/management/areas/{diningArea}', [TenantDesktopManagementController::class, 'area']);
                Route::post('/desktop/management/tables', [TenantDesktopManagementController::class, 'table']);
                Route::patch('/desktop/management/tables/{diningTable}', [TenantDesktopManagementController::class, 'table']);
                Route::post('/desktop/management/inventory', [TenantDesktopManagementController::class, 'inventory']);
                Route::patch('/desktop/management/inventory/{inventoryItem}', [TenantDesktopManagementController::class, 'inventory']);
            });

            // Staff identity is cloud-owned: Windows Desktop uses authenticated API calls,
            // never writes passwords or roles to its local SQLite projection.
            Route::middleware('tenant.role:owner,admin')->group(function (): void {
                Route::get('/desktop/users', [TenantDesktopUsersController::class, 'index']);
                Route::post('/desktop/users', [TenantDesktopUsersController::class, 'store']);
                Route::patch('/desktop/users/{user}', [TenantDesktopUsersController::class, 'update']);
            });

            Route::middleware('tenant.role:owner,admin,manager')->group(function (): void {
                Route::post('/pairing-tokens', [WaiterPairingController::class, 'store'])
                    ->middleware('throttle:20,1')
                    ->name('tenant.api.pairing.store');
            });

            Route::middleware('tenant.role:owner,admin,manager,cashier,inventory,kitchen,waiter')->group(function (): void {
                Route::post('/desktop/reconcile/push', [DesktopReconciliationController::class, 'push'])
                    ->name('tenant.api.desktop.reconcile.push');

                Route::get('/desktop/reconcile/pull', [DesktopReconciliationController::class, 'pull'])
                    ->name('tenant.api.desktop.reconcile.pull');
            });

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

                Route::post('/kitchen/items/{kitchenTicketItem}/start', StartKitchenTicketItemController::class)
                    ->name('tenant.api.kitchen.items.start');

                Route::post('/kitchen/items/{kitchenTicketItem}/ready', ReadyKitchenTicketItemController::class)
                    ->name('tenant.api.kitchen.items.ready');

                Route::post('/kitchen/items/{kitchenTicketItem}/void', VoidKitchenTicketItemController::class)
                    ->name('tenant.api.kitchen.items.void');

                Route::post('/kitchen/items/{kitchenTicketItem}/refire', RefireKitchenTicketItemController::class)
                    ->name('tenant.api.kitchen.items.refire');

                Route::post('/kitchen/items/{kitchenTicketItem}/recall', RecallKitchenTicketItemController::class)
                    ->name('tenant.api.kitchen.items.recall');

                Route::post('/kitchen/items/{kitchenTicketItem}/waste', RecordProductionWasteController::class)
                    ->name('tenant.api.kitchen.items.waste');

                Route::get('/kitchen/expo', ExpoController::class)
                    ->name('tenant.api.kitchen.expo');

                Route::get('/kitchen/reports/performance', KitchenPerformanceReportController::class)
                    ->name('tenant.api.kitchen.reports.performance');
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

                Route::post('/orders/{order}/courses/{courseNumber}/fire', FireOrderCourseController::class)
                    ->name('tenant.api.orders.courses.fire');

                Route::post('/orders/{order}/transfer-table', TransferOrderTableController::class)
                    ->name('tenant.api.orders.transfer-table');

                Route::post('/orders/{order}/items/{orderItem}/move', MoveOrderItemController::class)
                    ->name('tenant.api.orders.items.move');

                Route::post('/orders/{order}/merge', MergeOrdersController::class)
                    ->name('tenant.api.orders.merge');

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
