<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\Business;
use App\Models\DeviceActivation;
use App\Models\TenantUser;
use App\Services\Platform\LicenseService;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;

class TenantDeviceController extends Controller
{
    public function revoke(
        Request $request,
        DeviceActivation $deviceActivation,
        LicenseService $licenses,
    ): RedirectResponse {
        /** @var TenantUser $user */
        $user = $request->user('tenant');
        $business = Business::query()
            ->where('tenant_id', tenant('id'))
            ->firstOrFail();

        abort_unless($deviceActivation->business_id === $business->id, 404);

        $licenses->revokeMobileDeviceForTenant($deviceActivation, $user);

        return back()->with(
            'status',
            'Waiter mobile activation revoked. Its existing offline lease expires at the signed expiry.',
        );
    }
}
