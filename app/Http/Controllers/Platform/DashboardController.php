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
            'trial' => Business::where('status', BusinessStatus::Trial)->count(),
            'active' => Business::where('status', BusinessStatus::Active)->count(),
            'due' => Business::where('status', BusinessStatus::Due)->count(),
            'expired' => Business::where('status', BusinessStatus::Expired)->count(),
            'expiring_soon' => Business::whereNotNull('subscription_ends_at')
                ->whereBetween('subscription_ends_at', [now(), now()->addDays(7)])
                ->count(),
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
