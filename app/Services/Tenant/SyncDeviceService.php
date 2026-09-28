<?php

namespace App\Services\Tenant;

use App\Models\Business;
use App\Models\DeviceActivation;
use App\Models\SyncDeviceState;
use App\Models\TenantUser;
use App\Services\Platform\LicenseService;
use Illuminate\Http\Request;
use Illuminate\Validation\ValidationException;

class SyncDeviceService
{
    public function __construct(
        private readonly LicenseService $licenses,
    ) {}

    public function authenticate(Request $request, TenantUser $user): DeviceActivation
    {
        $deviceId = trim((string) $request->header('X-Device-Id'));
        $deviceSecret = trim((string) $request->header('X-Device-Secret'));

        if ($deviceId === '' || $deviceSecret === '') {
            throw ValidationException::withMessages([
                'device' => 'An activated device credential is required for mobile synchronization.',
            ]);
        }

        $business = Business::query()
            ->where('tenant_id', tenant('id'))
            ->firstOrFail();

        $device = $this->licenses->authenticateDevice(
            $business,
            $deviceId,
            $deviceSecret,
            $request->header('X-App-Version'),
        );

        SyncDeviceState::query()->updateOrCreate(
            [
                'central_device_id' => $device->id,
                'tenant_user_id' => $user->getKey(),
            ],
            [
                'device_uid' => $device->device_uid,
                'last_seen_at' => now(),
            ],
        );

        return $device;
    }

    public function state(DeviceActivation $device, TenantUser $user): SyncDeviceState
    {
        return SyncDeviceState::query()->firstOrCreate(
            [
                'central_device_id' => $device->id,
                'tenant_user_id' => $user->getKey(),
            ],
            [
                'device_uid' => $device->device_uid,
                'last_pull_cursor' => 0,
                'last_seen_at' => now(),
            ],
        );
    }
}
