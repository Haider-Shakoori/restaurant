<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\Business;
use App\Models\TenantUser;
use App\Services\Platform\LicenseService;
use App\Services\Tenant\SyncDeviceService;
use App\Services\Tenant\WaiterPairingService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class WaiterPairingController extends Controller
{
    public function store(
        Request $request,
        SyncDeviceService $devices,
        WaiterPairingService $pairing,
    ): JsonResponse {
        /** @var TenantUser $user */
        $user = $request->user();
        $business = Business::query()->where('tenant_id', tenant('id'))->firstOrFail();
        $desktop = $devices->authenticate($request, $user);
        $result = $pairing->issue($business, $desktop, $user);

        return response()->json([
            'data' => [
                'pairing_token' => $result['token'],
                'expires_at' => $result['expires_at'],
                'tenant_id' => $business->tenant_id,
            ],
        ], 201);
    }

    public function redeem(
        Request $request,
        WaiterPairingService $pairing,
        LicenseService $licenses,
    ): JsonResponse {
        $data = $request->validate([
            'pairing_token' => ['required', 'string', 'max:256'],
            'device_uid' => ['required', 'string', 'max:191'],
            'device_name' => ['nullable', 'string', 'max:255'],
            'platform' => ['required', 'in:android,ios'],
            'app_version' => ['nullable', 'string', 'max:50'],
        ]);

        $business = Business::query()->where('tenant_id', tenant('id'))->firstOrFail();
        $pairing->redeem($business, $data['pairing_token']);

        $result = $licenses->activatePairedMobile(
            $business,
            $data['device_uid'],
            $data['device_name'] ?? null,
            $data['platform'],
            $data['app_version'] ?? null,
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
