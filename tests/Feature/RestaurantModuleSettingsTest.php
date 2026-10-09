<?php

namespace Tests\Feature;

use App\Http\Controllers\Tenant\RestaurantModulesController;
use App\Models\Tenant;
use App\Services\Tenant\RestaurantSettingsService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\File;
use Illuminate\Support\Facades\Route;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;
use Tests\TestCase;

class RestaurantModuleSettingsTest extends TestCase
{
    use RefreshDatabase;

    private array $tenantDatabases = [];

    private array $tenantStoragePaths = [];

    protected function tearDown(): void
    {
        if (tenancy()->initialized) {
            tenancy()->end();
        }

        foreach ($this->tenantDatabases as $database) {
            @unlink(database_path($database));
        }

        foreach ($this->tenantStoragePaths as $path) {
            File::deleteDirectory($path);
        }

        parent::tearDown();
    }

    public function test_existing_restaurants_preserve_their_enabled_modules_by_default(): void
    {
        $this->makeTenant();

        $settings = app(RestaurantSettingsService::class)->all();
        $this->assertTrue($settings['recipes_enabled']);
        $this->assertTrue($settings['inventory_enabled']);
        $this->assertTrue($settings['purchasing_enabled']);
        $this->assertTrue($settings['automatic_recipe_consumption_enabled']);
    }

    public function test_module_preferences_persist_and_do_not_modify_kitchen_settings(): void
    {
        $this->makeTenant();
        $settings = app(RestaurantSettingsService::class);

        $settings->put([
            'recipes_enabled' => true,
            'inventory_enabled' => false,
            'purchasing_enabled' => false,
            'automatic_recipe_consumption_enabled' => false,
        ]);

        $fresh = $settings->all();
        $this->assertTrue($fresh['recipes_enabled']);
        $this->assertFalse($fresh['inventory_enabled']);
        $this->assertFalse($fresh['purchasing_enabled']);
        $this->assertFalse($fresh['automatic_recipe_consumption_enabled']);
        $this->assertTrue($fresh['kitchen_queue_enabled']);
    }

    public function test_purchasing_cannot_be_enabled_without_inventory(): void
    {
        $this->makeTenant();
        $controller = app(RestaurantModulesController::class);
        $request = Request::create('/settings/modules', 'POST', [
            'recipes_enabled' => '1',
            'inventory_enabled' => '0',
            'purchasing_enabled' => '1',
            'automatic_recipe_consumption_enabled' => '0',
        ]);

        $this->expectException(ValidationException::class);
        $controller->update($request, app(RestaurantSettingsService::class));
    }

    public function test_automatic_consumption_requires_recipe_and_inventory(): void
    {
        $this->makeTenant();
        $controller = app(RestaurantModulesController::class);
        $request = Request::create('/settings/modules', 'POST', [
            'recipes_enabled' => '0',
            'inventory_enabled' => '1',
            'purchasing_enabled' => '0',
            'automatic_recipe_consumption_enabled' => '1',
        ]);

        $this->expectException(ValidationException::class);
        $controller->update($request, app(RestaurantSettingsService::class));
    }

    public function test_web_and_api_module_endpoints_have_permissions_and_guards(): void
    {
        $routes = collect(Route::getRoutes()->getRoutes());
        $targets = [
            ['POST', 'settings/modules', 'tenant.role:owner,admin'],
            ['POST', 'api/v1/desktop/modules', 'tenant.role:owner,admin'],
            ['GET', 'api/v1/desktop/modules', 'auth:sanctum'],
            ['GET', 'inventory', 'restaurant.module:inventory'],
            ['GET', 'purchasing', 'restaurant.module:purchasing'],
            ['GET', 'api/v1/recipes', 'restaurant.module:recipes'],
        ];

        foreach ($targets as [$method, $uri, $middleware]) {
            $route = $routes->first(
                fn ($route) => $route->uri() === $uri && in_array($method, $route->methods(), true)
            );

            $this->assertNotNull($route, 'Missing route '.$method.' '.$uri);
            $this->assertContains($middleware, $route->gatherMiddleware());
        }
    }

    private function makeTenant(): void
    {
        $id = 'module-'.Str::lower(Str::random(10));
        $storagePath = storage_path(config('tenancy.filesystem.suffix_base').$id);
        File::deleteDirectory($storagePath);
        $this->tenantStoragePaths[] = $storagePath;

        if (config('database.connections.'.config('tenancy.database.central_connection').'.driver') === 'sqlite') {
            @unlink(database_path(config('tenancy.database.prefix').$id.config('tenancy.database.suffix')));
        }

        $tenant = Tenant::create(['id' => $id, 'provisioning_state' => 'ready']);
        $tenant->domains()->create(['domain' => $id.'.test']);
        $this->tenantDatabases[] = $tenant->database()->getName();
        tenancy()->initialize($tenant);
    }
}
