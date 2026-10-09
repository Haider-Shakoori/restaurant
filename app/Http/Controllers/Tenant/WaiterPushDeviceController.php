<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\TenantUser;
use App\Models\WaiterPushDevice;
use App\Services\Tenant\SyncDeviceService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Validation\Rule;

class WaiterPushDeviceController extends Controller
{
    public function store(Request $request, SyncDeviceService $devices): JsonResponse
    {
        /** @var TenantUser $user */
        $user = $request->user();
        $device = $devices->authenticate($request, $user);
        $values = $request->validate([
            'token' => ['required', 'string', 'min:20', 'max:4096'],
            'platform' => ['required', Rule::in(['ios', 'android'])],
        ]);

        // The central device credential and tenant user are authoritative.
        // Clients cannot enroll a push token for another waiter.
        WaiterPushDevice::query()->updateOrCreate(
            ['central_device_id' => $device->id],
            [
                'tenant_user_id' => $user->id,
                'fcm_token' => $values['token'],
                'platform' => $values['platform'],
                'enabled' => true,
            ],
        );

        return response()->json(['status' => 'registered']);
    }

    public function destroy(Request $request, SyncDeviceService $devices): JsonResponse
    {
        /** @var TenantUser $user */
        $user = $request->user();
        $device = $devices->authenticate($request, $user);

        WaiterPushDevice::query()
            ->where('central_device_id', $device->id)
            ->where('tenant_user_id', $user->id)
            ->update(['enabled' => false]);

        return response()->json(['status' => 'unregistered']);
    }
}
