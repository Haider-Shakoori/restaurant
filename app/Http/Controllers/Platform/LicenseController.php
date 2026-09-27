<?php

namespace App\Http\Controllers\Platform;

use App\Http\Controllers\Controller;
use App\Models\Business;
use App\Models\DeviceActivation;
use App\Models\LicenseKey;
use App\Services\Platform\LicenseService;
use Illuminate\Http\RedirectResponse;
use Illuminate\View\View;

class LicenseController extends Controller
{
    public function show(Business $business): View
    {
        $business->load([
            'licenseKeys' => fn ($query) => $query->withCount(['devices', 'leases'])->limit(20),
            'devices.licenseKey',
            'licenseEvents' => fn ($query) => $query->limit(50),
        ]);

        return view('platform.licenses.show', compact('business'));
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
