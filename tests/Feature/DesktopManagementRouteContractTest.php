<?php

namespace Tests\Feature;

use Illuminate\Support\Facades\Route;
use Tests\TestCase;

class DesktopManagementRouteContractTest extends TestCase
{
    public function test_desktop_catalog_management_endpoints_are_registered_and_protected(): void
    {
        $base = 'api/'.config('restaurant.api.version', 'v1').'/desktop/management';
        $routes = collect(Route::getRoutes()->getRoutes());

        $expected = [
            ['GET', $base],
            ['POST', $base.'/categories'],
            ['PATCH', $base.'/categories/{menuCategory}'],
            ['POST', $base.'/menu-items'],
            ['PATCH', $base.'/menu-items/{menuItem}'],
            ['POST', $base.'/menu-items/{menuItem}/image'],
            ['POST', $base.'/areas'],
            ['PATCH', $base.'/areas/{diningArea}'],
            ['POST', $base.'/tables'],
            ['PATCH', $base.'/tables/{diningTable}'],
            ['POST', $base.'/inventory'],
            ['PATCH', $base.'/inventory/{inventoryItem}'],
        ];

        foreach ($expected as [$method, $path]) {
            $route = $routes->first(fn ($route) =>
                $route->uri() === $path && in_array($method, $route->methods(), true));

            $this->assertNotNull($route, "Missing {$method} {$path} in deployed tenant API contract");
            $middleware = $route->gatherMiddleware();
            $this->assertContains('auth:sanctum', $middleware);
            $this->assertContains('subscription.active', $middleware);
            $this->assertContains('tenant.role:owner,admin,manager', $middleware);
        }
    }
}
