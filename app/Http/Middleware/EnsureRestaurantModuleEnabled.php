<?php

namespace App\Http\Middleware;

use App\Services\Tenant\RestaurantSettingsService;
use Closure;
use Illuminate\Http\Request;
use Symfony\Component\HttpFoundation\Response;

class EnsureRestaurantModuleEnabled
{
    public function handle(Request $request, Closure $next, string $module): Response
    {
        $key = match ($module) {
            'recipes' => 'recipes_enabled',
            'inventory' => 'inventory_enabled',
            'purchasing' => 'purchasing_enabled',
            default => abort(404),
        };

        $settings = app(RestaurantSettingsService::class)->all();

        abort_unless($settings[$key] ?? false, 403, 'This restaurant module is disabled in Settings.');

        if ($module === 'purchasing') {
            abort_unless($settings['inventory_enabled'], 403, 'Inventory must be enabled for purchasing.');
        }

        return $next($request);
    }
}
