<?php

use App\Http\Middleware\EnsureActivePlatformAdmin;
use App\Http\Middleware\EnsureTenantSubscriptionActive;
use App\Http\Middleware\RequirePlanFeature;
use Illuminate\Foundation\Application;
use Illuminate\Foundation\Configuration\Exceptions;
use Illuminate\Foundation\Configuration\Middleware;
use Illuminate\Http\Request;

return Application::configure(basePath: dirname(__DIR__))
    ->withRouting(
        web: __DIR__.'/../routes/web.php',
        api: __DIR__.'/../routes/api.php',
        commands: __DIR__.'/../routes/console.php',
        health: '/up',
    )
    ->withMiddleware(function (Middleware $middleware): void {
        $middleware->alias([
            'platform.active' => EnsureActivePlatformAdmin::class,
            'subscription.active' => EnsureTenantSubscriptionActive::class,
            'plan.feature' => RequirePlanFeature::class,
        ]);

        $middleware->redirectGuestsTo(
            fn (Request $request): string => tenancy()->initialized ? '/login' : '/platform/login',
        );
    })
    ->withExceptions(function (Exceptions $exceptions): void {
        $exceptions->shouldRenderJsonWhen(
            fn (Request $request) => $request->is('api/*') || $request->expectsJson(),
        );
    })->create();
