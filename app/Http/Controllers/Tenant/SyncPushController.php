<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\SyncPushRequest;
use App\Models\TenantUser;
use App\Services\Tenant\MobileSyncService;
use App\Services\Tenant\SyncDeviceService;
use Illuminate\Http\JsonResponse;

class SyncPushController extends Controller
{
    public function __invoke(
        SyncPushRequest $request,
        SyncDeviceService $devices,
        MobileSyncService $sync,
    ): JsonResponse {
        /** @var TenantUser $user */
        $user = $request->user();
        $device = $devices->authenticate($request, $user);

        return response()->json([
            'data' => $sync->push(
                $user,
                $device,
                $request->validated('mutations'),
            ),
        ]);
    }
}
