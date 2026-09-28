<?php

declare(strict_types=1);

use App\Http\Controllers\Tenant\BootstrapController;
use App\Http\Controllers\Tenant\DiningTableController;
use App\Http\Controllers\Tenant\KitchenRouteController;
use App\Http\Controllers\Tenant\KitchenStationController;
use App\Http\Controllers\Tenant\KitchenTicketController;
use App\Http\Controllers\Tenant\LicenseActivationController;
use App\Http\Controllers\Tenant\LicensePublicKeyController;
use App\Http\Controllers\Tenant\MenuController;
use App\Http\Controllers\Tenant\OfflineLeaseController;
use App\Http\Controllers\Tenant\OrderController;
use App\Http\Controllers\Tenant\OrderItemController;
use App\Http\Controllers\Tenant\ReadyKitchenTicketController;
use App\Http\Controllers\Tenant\ServeOrderController;
use App\Http\Controllers\Tenant\StartKitchenTicketController;
use App\Http\Controllers\Tenant\SubmitOrderController;
use App\Http\Controllers\Tenant\SubscriptionStatusController;
use App\Http\Controllers\Tenant\TenantAuthController;
use Illuminate\Support\Facades\Route;
use Stancl\Tenancy\Middleware\InitializeTenancyByDomain;
use Stancl\Tenancy\Middleware\PreventAccessFromCentralDomains;

$tenantMiddleware = [
    InitializeTenancyByDomain::class,
    PreventAccessFromCentralDomains::class,
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
        });
    });
