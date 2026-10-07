<?php

use App\Http\Controllers\Platform\Auth\LoginController;
use App\Http\Controllers\Platform\BusinessController;
use App\Http\Controllers\Platform\BusinessProvisioningController;
use App\Http\Controllers\Platform\DashboardController;
use App\Http\Controllers\Platform\LicenseController;
use App\Http\Controllers\Platform\OperatorController;
use App\Http\Controllers\Platform\PlanController;
use App\Http\Controllers\Platform\PlanPriceController;
use App\Http\Controllers\Platform\SubscriptionController;
use App\Http\Controllers\Platform\TenantController;
use App\Http\Controllers\Platform\TenantOwnerAccessController;
use App\Http\Controllers\PublicTrialController;
use Illuminate\Support\Facades\Route;

foreach (config('tenancy.central_domains', []) as $domain) {
    Route::domain($domain)->group(function (): void {
        Route::get('/', function () {
            if (auth()->check()) {
                return redirect('/platform');
            }

            return view('welcome');
        });

        Route::get('/start-trial', [PublicTrialController::class, 'create']);
        Route::post('/start-trial', [PublicTrialController::class, 'store'])
            ->middleware('throttle:5,1');

        Route::middleware('guest')->group(function (): void {
            Route::get('/platform/login', [LoginController::class, 'create']);
            Route::post('/platform/login', [LoginController::class, 'store']);
        });

        Route::middleware(['auth', 'platform.active'])->prefix('platform')->group(function (): void {
            Route::get('/', DashboardController::class);
            Route::post('/logout', [LoginController::class, 'destroy']);
            Route::get('/system-health', fn () => view('platform.system-health'));

            Route::get('/restaurants', [BusinessController::class, 'index']);
            Route::get('/tenants', [TenantController::class, 'index']);
            Route::get('/licenses', [LicenseController::class, 'index']);

            Route::middleware('can:manage-platform')->group(function (): void {
                Route::get('/restaurants/create', [BusinessController::class, 'create']);
                Route::post('/restaurants', [BusinessController::class, 'store']);
                Route::put('/restaurants/{business}', [BusinessController::class, 'update']);
                Route::post('/restaurants/{business}/provision', BusinessProvisioningController::class);
                Route::post('/restaurants/{business}/owner-access/reset', TenantOwnerAccessController::class);

                Route::get('/plans', [PlanController::class, 'index']);
                Route::get('/plans/create', [PlanController::class, 'create']);
                Route::post('/plans', [PlanController::class, 'store']);
                Route::put('/plans/{plan}', [PlanController::class, 'update']);
                Route::post('/plans/{plan}/prices', [PlanPriceController::class, 'store']);
                Route::put('/plans/{plan}/prices/{planPrice}', [PlanPriceController::class, 'update']);

                Route::post('/restaurants/{business}/trial/start', [SubscriptionController::class, 'startTrial']);
                Route::post('/restaurants/{business}/subscription/renew', [SubscriptionController::class, 'renew']);
                Route::post('/restaurants/{business}/subscription/cancel', [SubscriptionController::class, 'cancel']);
                Route::post('/restaurants/{business}/license/generate', [LicenseController::class, 'generate']);
                Route::post('/restaurants/{business}/licenses/{licenseKey}/revoke', [LicenseController::class, 'revoke']);
                Route::post('/restaurants/{business}/devices/{deviceActivation}/revoke', [LicenseController::class, 'revokeDevice']);
            });

            Route::get('/restaurants/{business}/license', [LicenseController::class, 'show']);
            Route::get('/restaurants/{business}', [BusinessController::class, 'show']);

            Route::middleware('can:manage-operators')->group(function (): void {
                Route::get('/operators', [OperatorController::class, 'index']);
                Route::get('/operators/create', [OperatorController::class, 'create']);
                Route::post('/operators', [OperatorController::class, 'store']);
                Route::patch('/operators/{adminUser}/toggle', [OperatorController::class, 'toggle']);
            });
        });
    });
}
