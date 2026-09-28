<?php

declare(strict_types=1);

use App\Http\Controllers\Tenant\Auth\LoginController;
use App\Http\Controllers\Tenant\BranchController;
use App\Http\Controllers\Tenant\LicenseActivationController;
use App\Http\Controllers\Tenant\LicensePublicKeyController;
use App\Http\Controllers\Tenant\OfflineLeaseController;
use App\Http\Controllers\Tenant\OnboardingController;
use App\Http\Controllers\Tenant\SubscriptionStatusController;
use App\Http\Middleware\ApplyTenantLocale;
use Illuminate\Support\Facades\Route;
use Stancl\Tenancy\Middleware\InitializeTenancyByDomain;
use Stancl\Tenancy\Middleware\PreventAccessFromCentralDomains;

$tenantMiddleware = [
    InitializeTenancyByDomain::class,
    PreventAccessFromCentralDomains::class,
];

Route::middleware(['web', ...$tenantMiddleware, ApplyTenantLocale::class])->group(function (): void {
    Route::middleware('guest:tenant')->group(function (): void {
        Route::get('/login', [LoginController::class, 'create'])->name('tenant.login');
        Route::post('/login', [LoginController::class, 'store']);
    });

    Route::get('/', function () {
        return auth('tenant')->check() ? redirect('/onboarding') : redirect('/login');
    })->middleware('subscription.active')->name('tenant.home');

    Route::middleware('auth:tenant')->group(function (): void {
        Route::post('/logout', [LoginController::class, 'destroy'])->name('tenant.logout');

        Route::middleware('subscription.active')->group(function (): void {
            Route::get('/onboarding', [OnboardingController::class, 'show'])->name('tenant.onboarding');
            Route::put('/onboarding/restaurant', [OnboardingController::class, 'restaurant']);
            Route::put('/onboarding/branch', [OnboardingController::class, 'branch']);

            Route::get('/branches', [BranchController::class, 'index'])->name('tenant.branches.index');
            Route::get('/branches/create', [BranchController::class, 'create']);
            Route::post('/branches', [BranchController::class, 'store']);
            Route::get('/branches/{branch}/edit', [BranchController::class, 'edit']);
            Route::put('/branches/{branch}', [BranchController::class, 'update']);
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

        Route::post('/license/lease', OfflineLeaseController::class)
            ->name('tenant.api.license.lease');
    });
