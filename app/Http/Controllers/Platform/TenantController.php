<?php

namespace App\Http\Controllers\Platform;

use App\Http\Controllers\Controller;
use App\Models\Business;
use App\Models\Tenant;
use Illuminate\View\View;

class TenantController extends Controller
{
    public function index(): View
    {
        $tenants = Tenant::query()
            ->with('domains')
            ->latest()
            ->paginate(20);

        $businesses = Business::query()
            ->whereNotNull('tenant_id')
            ->get()
            ->keyBy('tenant_id');

        return view('platform.tenants.index', compact('tenants', 'businesses'));
    }
}
