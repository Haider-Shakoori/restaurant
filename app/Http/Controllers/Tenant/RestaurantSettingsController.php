<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\UpdateRestaurantSettingsRequest;
use App\Services\Tenant\RestaurantSettingsService;
use Illuminate\Http\RedirectResponse;

class RestaurantSettingsController extends Controller
{
    public function update(
        UpdateRestaurantSettingsRequest $request,
        RestaurantSettingsService $settings,
    ): RedirectResponse {
        $data = $request->validated();
        $branchId = $data['branch_id'] ?? null;
        unset($data['branch_id']);

        $settings->put($data, $branchId);

        return back()->with('status', $branchId === null
            ? 'Restaurant workflow settings updated.'
            : 'Branch workflow settings updated.');
    }
}
