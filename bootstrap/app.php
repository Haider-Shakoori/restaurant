<?php

use App\Http\Middleware\EnsureActivePlatformAdmin;
use App\Http\Middleware\EnsureTenantRole;
use App\Http\Middleware\EnsureTenantSubscriptionActive;
use App\Http\Middleware\RequirePlanFeature;
use App\Http\Middleware\UseTenantWebGuard;
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
            'tenant.role' => EnsureTenantRole::class,
            'tenant.web.guard' => UseTenantWebGuard::class,
        ]);

        $middleware->redirectGuestsTo(function (Request $request): string {
            $centralDomains = array_map(
                static fn (string $domain): string => strtolower(trim($domain)),
                config('tenancy.central_domains', []),
            );

            return in_array(strtolower($request->getHost()), $centralDomains, true)
                ? '/platform/login'
                : '/login';
        });
    })
    ->withExceptions(function (Exceptions $exceptions): void {
        $exceptions->shouldRenderJsonWhen(
            fn (Request $request) => $request->is('api/*') || $request->expectsJson(),
        );
    })->create();
