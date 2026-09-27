<?php

namespace App\Http\Controllers\Platform;

use App\Http\Controllers\Controller;
use App\Http\Requests\Platform\RenewSubscriptionRequest;
use App\Models\Business;
use App\Models\PlanPrice;
use App\Services\Platform\SubscriptionService;
use Illuminate\Http\RedirectResponse;

class SubscriptionController extends Controller
{
    public function startTrial(
        Business $business,
        SubscriptionService $subscriptions,
    ): RedirectResponse {
        $subscriptions->startTrial($business, request()->user());

        return back()->with('status', 'Seven-day trial started.');
    }

    public function renew(
        RenewSubscriptionRequest $request,
        Business $business,
        SubscriptionService $subscriptions,
    ): RedirectResponse {
        $price = PlanPrice::query()->findOrFail($request->integer('plan_price_id'));

        $subscriptions->renew(
            $business,
            $price,
            $request->user(),
            $request->integer('custom_days') ?: null,
        );

        return back()->with('status', 'Subscription renewal recorded.');
    }

    public function cancel(
        Business $business,
        SubscriptionService $subscriptions,
    ): RedirectResponse {
        $subscriptions->cancelAccess($business, request()->user());

        return back()->with('status', 'Current and scheduled access periods cancelled without deleting tenant data.');
    }
}
