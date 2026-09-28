<?php

namespace App\Http\Controllers\Platform;

use App\Http\Controllers\Controller;
use App\Models\Business;
use App\Services\Platform\BusinessProvisioningService;
use Illuminate\Http\RedirectResponse;
use Throwable;

class BusinessProvisioningController extends Controller
{
    public function __invoke(Business $business, BusinessProvisioningService $provisioning): RedirectResponse
    {
        try {
            $business = $provisioning->provision($business);
        } catch (Throwable $e) {
            report($e);

            return back()->withErrors([
                'provisioning' => 'Provisioning failed: '.$e->getMessage(),
            ]);
        }

        $domain = $business->tenant?->domains->first()?->domain;

        return back()->with(
            'status',
            'Restaurant provisioned successfully'.($domain ? ' at '.$domain : '').'. You can now start the 7-day trial.'
        );
    }
}
