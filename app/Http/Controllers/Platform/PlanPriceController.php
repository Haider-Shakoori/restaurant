<?php

namespace App\Http\Controllers\Platform;

use App\Enums\BillingCycle;
use App\Http\Controllers\Controller;
use App\Http\Requests\Platform\StorePlanPriceRequest;
use App\Models\Plan;
use App\Models\PlanPrice;
use Illuminate\Http\RedirectResponse;

class PlanPriceController extends Controller
{
    public function store(StorePlanPriceRequest $request, Plan $plan): RedirectResponse
    {
        $cycle = BillingCycle::from($request->string('billing_cycle')->toString());

        $plan->prices()->create([
            'billing_cycle' => $cycle,
            'interval_months' => $cycle->intervalMonths(),
            'price' => $request->input('price'),
            'currency' => $request->string('currency')->upper()->toString(),
            'is_active' => $request->boolean('is_active', true),
            'sort_order' => $request->integer('sort_order'),
        ]);

        return back()->with('status', 'Plan price added.');
    }

    public function update(
        StorePlanPriceRequest $request,
        Plan $plan,
        PlanPrice $planPrice,
    ): RedirectResponse {
        abort_unless($planPrice->plan_id === $plan->id, 404);

        $cycle = BillingCycle::from($request->string('billing_cycle')->toString());

        $planPrice->update([
            'billing_cycle' => $cycle,
            'interval_months' => $cycle->intervalMonths(),
            'price' => $request->input('price'),
            'currency' => $request->string('currency')->upper()->toString(),
            'is_active' => $request->boolean('is_active'),
            'sort_order' => $request->integer('sort_order'),
        ]);

        return back()->with('status', 'Plan price updated.');
    }
}
