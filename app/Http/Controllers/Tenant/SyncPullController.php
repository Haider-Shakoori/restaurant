<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\SyncPullRequest;
use App\Models\TenantUser;
use App\Services\Tenant\MobileSyncService;
use App\Services\Tenant\SyncDeviceService;
use Illuminate\Http\JsonResponse;

class SyncPullController extends Controller
{
    public function __invoke(
        SyncPullRequest $request,
        SyncDeviceService $devices,
        MobileSyncService $sync,
    ): JsonResponse {
        /** @var TenantUser $user */
        $user = $request->user();
        $device = $devices->authenticate($request, $user);
        $data = $request->validated();

        return response()->json([
            'data' => $sync->pull(
                $user,
                $device,
                (int) ($data['cursor'] ?? 0),
                (int) ($data['limit'] ?? config('restaurant.performance.sync_batch_size', 100)),
            ),
        ]);
    }
}
