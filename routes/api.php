<?php

use App\Http\Controllers\Api\V1\DesktopLicenseResolverController;
use App\Http\Controllers\Api\V1\HealthController;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Route;

foreach (config('tenancy.central_domains', []) as $domain) {
    Route::domain($domain)
        ->prefix(config('restaurant.api.version', 'v1'))
        ->group(function (): void {
            Route::get('/health', HealthController::class);

            Route::post('/desktop/license/resolve', DesktopLicenseResolverController::class)
                ->middleware('throttle:10,1');

            Route::get('/user', function (Request $request) {
                return $request->user();
            })->middleware('auth:sanctum');
        });
}
