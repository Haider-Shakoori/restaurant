<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\AssignKitchenRouteRequest;
use App\Models\KitchenStation;
use App\Models\MenuItem;
use App\Models\MenuItemKitchenRoute;
use Illuminate\Http\JsonResponse;
use Illuminate\Validation\ValidationException;

class KitchenRouteController extends Controller
{
    public function store(
        AssignKitchenRouteRequest $request,
        MenuItem $menuItem,
    ): JsonResponse {
        $data = $request->validated();

        $station = KitchenStation::query()
            ->whereKey($data['kitchen_station_id'])
            ->where('branch_id', $data['branch_id'])
            ->where('is_active', true)
            ->first();

        if (! $station) {
            throw ValidationException::withMessages([
                'kitchen_station_id' => 'The selected kitchen station does not belong to this branch or is inactive.',
            ]);
        }

        $route = MenuItemKitchenRoute::query()->updateOrCreate(
            [
                'menu_item_id' => $menuItem->id,
                'branch_id' => $data['branch_id'],
            ],
            [
                'kitchen_station_id' => $station->id,
            ],
        );

        return response()->json(['data' => $route->load(['menuItem', 'branch', 'station'])]);
    }
}
