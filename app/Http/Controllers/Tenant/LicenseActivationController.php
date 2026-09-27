<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\ActivateLicenseRequest;
use App\Models\Business;
use App\Services\Platform\LicenseService;
use Illuminate\Http\JsonResponse;

class LicenseActivationController extends Controller
{
    public function __invoke(
        ActivateLicenseRequest $request,
        LicenseService $licenses,
    ): JsonResponse {
        $business = Business::query()->where('tenant_id', tenant('id'))->firstOrFail();

        $result = $licenses->activateDevice(
            $business,
            $request->string('license_key')->toString(),
            $request->string('device_uid')->toString(),
            $request->input('device_name'),
            $request->string('platform')->toString(),
            $request->input('app_version'),
        );

        return response()->json([
            'device' => [
                'id' => $result['device']->id,
                'uid' => $result['device']->device_uid,
                'name' => $result['device']->device_name,
                'platform' => $result['device']->platform,
                'activated_at' => $result['device']->activated_at?->toIso8601String(),
            ],
            'device_secret' => $result['device_secret'],
            'lease' => $result['lease'],
        ], 201);
    }
}
