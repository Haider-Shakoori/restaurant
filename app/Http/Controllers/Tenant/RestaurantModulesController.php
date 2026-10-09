<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Services\Tenant\RestaurantSettingsService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Validation\ValidationException;

class RestaurantModulesController extends Controller
{
    public const KEYS = [
        'recipes_enabled',
        'inventory_enabled',
        'purchasing_enabled',
        'automatic_recipe_consumption_enabled',
    ];

    public function show(RestaurantSettingsService $settings): JsonResponse
    {
        return response()->json(['data' => $this->modules($settings)]);
    }

    public function update(Request $request, RestaurantSettingsService $settings): JsonResponse|RedirectResponse
    {
        $data = $request->validate([
            'recipes_enabled' => ['required', 'boolean'],
            'inventory_enabled' => ['required', 'boolean'],
            'purchasing_enabled' => ['required', 'boolean'],
            'automatic_recipe_consumption_enabled' => ['required', 'boolean'],
        ]);

        foreach ($data as $key => $value) {
            $data[$key] = filter_var($value, FILTER_VALIDATE_BOOLEAN);
        }

        if ($data['purchasing_enabled'] && ! $data['inventory_enabled']) {
            throw ValidationException::withMessages([
                'purchasing_enabled' => 'Purchasing requires inventory to be enabled.',
            ]);
        }

        if ($data['automatic_recipe_consumption_enabled'] &&
            (! $data['recipes_enabled'] || ! $data['inventory_enabled'])) {
            throw ValidationException::withMessages([
                'automatic_recipe_consumption_enabled' =>
                    'Automatic consumption requires both recipes and inventory.',
            ]);
        }

        // Settings are restaurant-wide; no branch override is permitted.
        $settings->put($data);

        return $request->expectsJson()
            ? response()->json(['data' => $this->modules($settings)])
            : redirect('/settings#modules')->with('status', 'Restaurant modules updated. Existing records are preserved.');
    }

    private function modules(RestaurantSettingsService $settings): array
    {
        return array_intersect_key($settings->all(), array_flip(self::KEYS));
    }
}
