<?php

namespace App\Http\Controllers\Platform;

use App\Enums\DeviceStatus;
use App\Http\Controllers\Controller;
use App\Models\Business;
use App\Models\DeviceActivation;
use App\Models\LicenseKey;
use App\Services\Platform\LicenseService;
use App\Services\Platform\SubscriptionService;
use Illuminate\Http\RedirectResponse;
use Illuminate\View\View;

class LicenseController extends Controller
{
    public function show(
        Business $business,
        SubscriptionService $subscriptions,
    ): View {

        $business->load([
            'licenseKeys' => fn ($query) => $query->withCount(['devices', 'leases'])->limit(20),
            'devices.licenseKey',
            'licenseEvents' => fn ($query) => $query->limit(50),
        ]);

        $access = $subscriptions->access($business);
        $rawMobileLimit = data_get(
            $access->features,
            'max_mobile_devices',
            data_get($access->features, 'max_devices', config('license.default_max_devices', 5)),
        );
        $mobileDeviceLimit = is_string($rawMobileLimit) && strtolower(trim($rawMobileLimit)) === 'unlimited'
            ? null
            : (is_numeric($rawMobileLimit) && (int) $rawMobileLimit > 0 ? (int) $rawMobileLimit : null);
        $activeMobileDevices = $business->devices()
            ->where('status', DeviceStatus::Active)
            ->whereIn('platform', ['android', 'ios'])
            ->count();

        return view('platform.licenses.show', compact(
            'business',
            'mobileDeviceLimit',
            'activeMobileDevices',
        ));
    }

    public function generate(
        Business $business,
        LicenseService $licenses,
    ): RedirectResponse {
        $result = $licenses->generate($business, request()->user());

        return back()
            ->with('status', 'License generated. Copy the raw key now; it will not be shown again.')
            ->with('generated_license_key', $result['raw_key']);
    }

    public function revoke(
        Business $business,
        LicenseKey $licenseKey,
        LicenseService $licenses,
    ): RedirectResponse {
        abort_unless($licenseKey->business_id === $business->id, 404);

        $licenses->revoke($licenseKey, request()->user());

        return back()->with('status', 'License revoked. Active device credentials under it were also revoked.');
    }

    public function revokeDevice(
        Business $business,
        DeviceActivation $deviceActivation,
        LicenseService $licenses,
    ): RedirectResponse {
        abort_unless($deviceActivation->business_id === $business->id, 404);

        $licenses->revokeDevice($deviceActivation, request()->user());

        return back()->with('status', 'Device activation revoked.');
    }
}
