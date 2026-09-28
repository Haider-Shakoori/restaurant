<?php

namespace App\Http\Controllers\Tenant;

use App\Enums\OnboardingStep;
use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\StoreBranchOnboardingRequest;
use App\Http\Requests\Tenant\StoreRestaurantOnboardingRequest;
use App\Services\Tenant\BranchService;
use App\Services\Tenant\OnboardingService;
use Illuminate\Http\RedirectResponse;
use Illuminate\View\View;

class OnboardingController extends Controller
{
    public function show(OnboardingService $onboarding, BranchService $branches): View
    {
        $restaurant = $onboarding->restaurant();
        $progress = $onboarding->progress();

        return view('tenant.onboarding.index', [
            'restaurant' => $restaurant,
            'primaryBranch' => $restaurant?->branches()->where('is_primary', true)->first(),
            'progress' => $progress,
            'steps' => OnboardingStep::cases(),
            'maxBranches' => $branches->maxBranches(),
        ]);
    }

    public function restaurant(
        StoreRestaurantOnboardingRequest $request,
        OnboardingService $onboarding,
    ): RedirectResponse {
        $onboarding->saveRestaurant($request->validated());

        return back()->with('status', 'Restaurant information saved.');
    }

    public function branch(
        StoreBranchOnboardingRequest $request,
        OnboardingService $onboarding,
    ): RedirectResponse {
        $onboarding->savePrimaryBranch($request->validated());

        return back()->with('status', 'Primary branch saved.');
    }
}
