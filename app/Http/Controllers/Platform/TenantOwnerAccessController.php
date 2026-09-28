<?php

namespace App\Http\Controllers\Platform;

use App\Http\Controllers\Controller;
use App\Models\Business;
use App\Services\Platform\TenantOwnerAccessService;
use Illuminate\Http\RedirectResponse;
use Throwable;

class TenantOwnerAccessController extends Controller
{
    public function __invoke(Business $business, TenantOwnerAccessService $access): RedirectResponse
    {
        try {
            $credentials = $access->reset($business);
        } catch (Throwable $e) {
            report($e);

            return back()->withErrors([
                'owner_access' => 'Owner access could not be created: '.$e->getMessage(),
            ]);
        }

        return back()
            ->with('status', 'Restaurant owner access is ready.')
            ->with('owner_credentials', $credentials);
    }
}
