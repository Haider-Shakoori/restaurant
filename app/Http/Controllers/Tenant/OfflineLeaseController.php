<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\RefreshLeaseRequest;
use App\Models\Business;
use App\Services\Platform\LicenseService;
use Illuminate\Http\JsonResponse;

class OfflineLeaseController extends Controller
{
    public function __invoke(
        RefreshLeaseRequest $request,
        LicenseService $licenses,
    ): JsonResponse {
        $business = Business::query()->where('tenant_id', tenant('id'))->firstOrFail();

        return response()->json([
            'lease' => $licenses->refreshLease(
                $business,
                $request->string('device_id')->toString(),
                $request->string('device_secret')->toString(),
                $request->input('app_version'),
            ),
        ]);
    }
}
