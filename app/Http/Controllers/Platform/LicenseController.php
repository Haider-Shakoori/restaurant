<?php

namespace App\Http\Controllers\Platform;

use App\Enums\DeviceStatus;
use App\Http\Controllers\Controller;
use App\Models\Business;
use App\Models\DeviceActivation;
use App\Models\LicenseEvent;
use App\Models\LicenseKey;
use App\Services\Platform\LicenseService;
use App\Services\Platform\SubscriptionService;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Response;
use Illuminate\View\View;

class LicenseController extends Controller
{
    public function index(): View
    {
        $search = trim((string) request('q', ''));

        $businesses = Business::query()
            ->with(['tenant.domains', 'plan', 'licenseKeys'])
            ->withCount([
                'devices as active_devices_count' => fn ($query) => $query
                    ->where('status', DeviceStatus::Active),
                'devices as active_mobile_devices_count' => fn ($query) => $query
                    ->where('status', DeviceStatus::Active)
                    ->whereIn('platform', ['android', 'ios']),
            ])
            ->when($search !== '', function ($query) use ($search): void {
                $query->where(function ($query) use ($search): void {
                    $query
                        ->where('name', 'like', '%'.$search.'%')
                        ->orWhere('contact_name', 'like', '%'.$search.'%')
                        ->orWhere('phone', 'like', '%'.$search.'%')
                        ->orWhere('requested_subdomain', 'like', '%'.$search.'%')
                        ->orWhere('tenant_id', 'like', '%'.$search.'%');
                });
            })
            ->orderBy('name')
            ->paginate(20)
            ->withQueryString();

        return view('platform.licenses.index', compact('businesses', 'search'));
    }

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
        $rawMobileLimit = $access->feature(
            'max_mobile_devices',
            $access->feature('max_devices', config('license.default_max_devices', 5)),
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

    public function desktopMode(Business $business): RedirectResponse
    {
        $mode = request()->validate([
            'desktop_mode' => ['required', 'in:cloud_sync,standalone_offline'],
        ])['desktop_mode'];

        $previous = $business->desktop_mode ?? 'cloud_sync';
        if ($previous === $mode) {
            return back()->with('status', 'Desktop operating mode is already '.$mode.'.');
        }

        $business->update(['desktop_mode' => $mode]);

        LicenseEvent::create([
            'business_id' => $business->id,
            'admin_user_id' => request()->user()->id,
            'event' => 'desktop.mode.updated',
            'message' => 'Desktop operating mode changed from '.$previous.' to '.$mode.'.',
            'context' => ['previous' => $previous, 'next' => $mode],
            'occurred_at' => now(),
        ]);

        return back()->with('status',
            'Desktop mode updated. The Windows desktop must refresh its signed license while online to apply the change. Existing offline licenses cannot be remotely changed until refreshed or expired.');
    }

    /**
     * Issue a signed offline renewal file for an already activated device.
     * This download contains only signed public lease claims, never device
     * credentials, signing keys or server secrets.
     */
    public function downloadOfflineLease(
        Business $business,
        DeviceActivation $deviceActivation,
        LicenseService $licenses,
    ): Response {
        abort_unless($deviceActivation->business_id === $business->id, 404);
        abort_unless($business->desktop_mode === 'standalone_offline', 422);
        abort_unless($deviceActivation->platform === 'windows', 422);

        $lease = $licenses->issueLease($deviceActivation->load(['licenseKey', 'business']));

        LicenseEvent::create([
            'business_id' => $business->id,
            'admin_user_id' => request()->user()->id,
            'event' => 'desktop.offline_lease.exported',
            'message' => 'A device-bound signed offline lease renewal was exported.',
            'context' => ['device_id' => $deviceActivation->id],
            'occurred_at' => now(),
        ]);

        $filename = 'restaurant-offline-lease-'.preg_replace(
            '/[^a-zA-Z0-9_-]/', '', (string) $deviceActivation->id,
        ).'.json';

        return response(
            json_encode($lease, JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES),
            200,
            [
                'Content-Type' => 'application/json',
                'Content-Disposition' => 'attachment; filename="'.$filename.'"',
                'Cache-Control' => 'private, no-store, max-age=0',
                'X-Content-Type-Options' => 'nosniff',
            ],
        );
    }

    public function generate(
        Business $business,
        LicenseService $licenses,
    ): RedirectResponse {
        $hadActiveLicense = $business->licenseKeys()
            ->where('status', 'active')
            ->exists();

        $result = $licenses->generate($business, request()->user());

        return back()
            ->with(
                'status',
                $hadActiveLicense
                    ? 'License regenerated. Previous license and active device credentials were revoked. Copy the new raw key now; it will not be shown again.'
                    : 'License generated. Copy the raw key now; it will not be shown again.',
            )
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
