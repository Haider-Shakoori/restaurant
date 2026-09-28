<?php

namespace App\Http\Controllers\Platform;

use App\Enums\BusinessStatus;
use App\Enums\ProvisioningState;
use App\Http\Controllers\Controller;
use App\Models\AdminUser;
use App\Models\Business;
use App\Models\Plan;
use App\Models\ProvisioningEvent;
use App\Models\Tenant;
use Illuminate\View\View;

class DashboardController extends Controller
{
    public function __invoke(): View
    {
        $metrics = [
            'restaurants' => Business::count(),
            'provisioning' => Business::where('status', BusinessStatus::Provisioning)->count(),
            'trial' => Business::where('status', BusinessStatus::Trial)->count(),
            'active' => Business::where('status', BusinessStatus::Active)->count(),
            'expired' => Business::where('status', BusinessStatus::Expired)->count(),
            'provisioning_failed' => Business::where('provisioning_state', ProvisioningState::Failed)->count(),
            'tenants' => Tenant::count(),
            'active_plans' => Plan::where('is_active', true)->count(),
            'operators' => AdminUser::where('is_active', true)->count(),
        ];

        $recentBusinesses = Business::query()
            ->with(['plan', 'assignedOperator', 'tenant.domains'])
            ->latest()
            ->limit(8)
            ->get();

        $recentEvents = ProvisioningEvent::query()
            ->with('business:id,name')
            ->latest('occurred_at')
            ->limit(8)
            ->get();

        return view('platform.dashboard', compact('metrics', 'recentBusinesses', 'recentEvents'));
    }
}
