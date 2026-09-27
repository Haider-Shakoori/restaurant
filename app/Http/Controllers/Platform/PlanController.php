<?php

namespace App\Http\Controllers\Platform;

use App\Enums\BillingCycle;
use App\Http\Controllers\Controller;
use App\Http\Requests\Platform\StorePlanRequest;
use App\Models\Plan;
use Illuminate\Http\RedirectResponse;
use Illuminate\Support\Facades\DB;
use Illuminate\View\View;

class PlanController extends Controller
{
    public function index(): View
    {
        $plans = Plan::query()
            ->with(['features', 'prices'])
            ->withCount('businesses')
            ->orderBy('sort_order')
            ->orderBy('name')
            ->get();

        return view('platform.plans.index', [
            'plans' => $plans,
            'billingCycles' => BillingCycle::cases(),
        ]);
    }

    public function create(): View
    {
        return view('platform.plans.create');
    }

    public function store(StorePlanRequest $request): RedirectResponse
    {
        DB::transaction(function () use ($request): void {
            $data = $request->safe()->except('features');
            $data['is_active'] = $request->boolean('is_active', true);

            $plan = Plan::create($data);

            foreach ($request->input('features', []) as $feature) {
                $key = trim((string) ($feature['key'] ?? ''));

                if ($key === '') {
                    continue;
                }

                $plan->features()->create([
                    'feature_key' => $key,
                    'value' => ['value' => $feature['value'] ?? null],
                ]);
            }
        });

        return redirect('/platform/plans')->with('status', 'Plan created.');
    }

    public function update(StorePlanRequest $request, Plan $plan): RedirectResponse
    {
        DB::transaction(function () use ($request, $plan): void {
            $data = $request->safe()->except('features');
            $data['is_active'] = $request->boolean('is_active');
            $plan->update($data);

            $plan->features()->delete();

            foreach ($request->input('features', []) as $feature) {
                $key = trim((string) ($feature['key'] ?? ''));

                if ($key === '') {
                    continue;
                }

                $plan->features()->create([
                    'feature_key' => $key,
                    'value' => ['value' => $feature['value'] ?? null],
                ]);
            }
        });

        return back()->with('status', 'Plan updated.');
    }
}
