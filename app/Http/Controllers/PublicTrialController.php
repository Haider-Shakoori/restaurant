<?php

namespace App\Http\Controllers;

use App\Enums\BusinessStatus;
use App\Enums\ProvisioningState;
use App\Http\Requests\Public\StoreTrialRequest;
use App\Models\Business;
use App\Models\Plan;
use Illuminate\Http\RedirectResponse;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Illuminate\View\View;

class PublicTrialController extends Controller
{
    public function create(): View
    {
        return view('trial.create', [
            'plans' => Plan::query()
                ->where('is_active', true)
                ->with(['prices' => fn ($query) => $query->where('is_active', true)])
                ->orderBy('sort_order')
                ->orderBy('name')
                ->get(),
        ]);
    }

    public function store(StoreTrialRequest $request): RedirectResponse
    {
        $business = DB::transaction(function () use ($request): Business {
            $data = $request->validated();
            $data['requested_subdomain'] = Str::lower($data['requested_subdomain']);
            $data['status'] = BusinessStatus::Provisioning;
            $data['provisioning_state'] = ProvisioningState::Pending;

            $business = Business::create($data);

            $business->provisioningEvents()->create([
                'event' => 'trial.requested',
                'state' => ProvisioningState::Pending->value,
                'message' => 'Public 7-day trial requested. Trial clock will start only after restaurant provisioning is ready.',
                'occurred_at' => now(),
            ]);

            return $business;
        });

        return redirect('/start-trial?submitted=1')
            ->with('status', 'Trial request received. We will provision your restaurant before the 7-day trial begins.')
            ->with('trial_request_id', $business->id);
    }
}
