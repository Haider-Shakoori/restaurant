<?php

use Illuminate\Support\Facades\Route;

foreach (config('tenancy.central_domains', []) as $domain) {
    Route::domain($domain)->group(function (): void {
        Route::get('/', function () {
            return view('welcome');
        });
    });
}
