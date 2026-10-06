<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\TenantUser;
use App\Services\Tenant\DesktopReconciliationService;
use App\Services\Tenant\SyncDeviceService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class DesktopReconciliationController extends Controller
{
    public function push(
        Request $request,
        SyncDeviceService $devices,
        DesktopReconciliationService $sync,
    ): JsonResponse {
        $data = $request->validate([
            'mutations' => ['required', 'array', 'min:1', 'max:100'],
            'mutations.*.mutation_id' => ['required', 'string', 'max:80'],
            'mutations.*.operation' => ['required', 'string', 'max:64'],
            'mutations.*.entity_type' => ['required', 'string', 'max:64'],
            'mutations.*.local_entity_id' => ['required', 'string', 'max:100'],
            'mutations.*.actor_public_id' => ['required', 'string', 'max:80'],
            'mutations.*.payload' => ['required', 'array'],
            'mutations.*.occurred_at' => ['nullable', 'date'],
        ]);

        /** @var TenantUser $user */
        $user = $request->user();
        $device = $devices->authenticate($request, $user);

        return response()->json([
            'data' => $sync->push($user, $device, $data['mutations']),
        ]);
    }

    public function pull(
        Request $request,
        SyncDeviceService $devices,
        DesktopReconciliationService $sync,
    ): JsonResponse {
        $data = $request->validate([
            'cursor' => ['nullable', 'integer', 'min:0'],
            'limit' => ['nullable', 'integer', 'min:1', 'max:250'],
        ]);

        /** @var TenantUser $user */
        $user = $request->user();
        $device = $devices->authenticate($request, $user);

        return response()->json([
            'data' => $sync->pull(
                $device,
                (int) ($data['cursor'] ?? 0),
                (int) ($data['limit'] ?? 100),
            ),
        ]);
    }
}
