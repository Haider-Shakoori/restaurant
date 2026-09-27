<?php

use App\Http\Controllers\Api\V1\HealthController;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Route;

Route::prefix(config('restaurant.api.version', 'v1'))->group(function (): void {
    Route::get('/health', HealthController::class)->name('api.health');

    Route::get('/user', function (Request $request) {
        return $request->user();
    })->middleware('auth:sanctum');
});
