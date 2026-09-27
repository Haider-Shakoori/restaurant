<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\Business;
use App\Services\Platform\SubscriptionService;
use Illuminate\Http\JsonResponse;

class SubscriptionStatusController extends Controller
{
    public function __invoke(SubscriptionService $subscriptions): JsonResponse
    {
        $business = Business::query()->where('tenant_id', tenant('id'))->first();

        if (! $business) {
            return response()->json([
                'access_allowed' => false,
                'status' => 'unavailable',
                'code' => 'subscription_unavailable',
            ], 404);
        }

        $access = $subscriptions->access($business);

        return response()->json([
            'access_allowed' => $access->allowed,
            'status' => $access->status,
            'code' => $access->code,
            'ends_at' => $access->endsAt?->toIso8601String(),
            'plan' => $access->subscription ? [
                'code' => $access->subscription->plan_code_snapshot,
                'name' => $access->subscription->plan_name_snapshot,
            ] : null,
            'features' => $access->features,
        ]);
    }
}
