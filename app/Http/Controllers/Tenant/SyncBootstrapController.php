<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\TenantUser;
use App\Services\Tenant\MobileSyncService;
use App\Services\Tenant\SyncDeviceService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class SyncBootstrapController extends Controller
{
    public function __invoke(
        Request $request,
        SyncDeviceService $devices,
        MobileSyncService $sync,
    ): JsonResponse {
        /** @var TenantUser $user */
        $user = $request->user();
        $device = $devices->authenticate($request, $user);

        return response()->json([
            'data' => $sync->bootstrap($user, $device),
        ]);
    }
}
