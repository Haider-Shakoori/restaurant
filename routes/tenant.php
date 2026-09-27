<?php

declare(strict_types=1);

use App\Http\Controllers\Tenant\LicenseActivationController;
use App\Http\Controllers\Tenant\LicensePublicKeyController;
use App\Http\Controllers\Tenant\OfflineLeaseController;
use App\Http\Controllers\Tenant\SubscriptionStatusController;
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
    });
