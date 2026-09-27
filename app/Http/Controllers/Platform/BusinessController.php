<?php

namespace App\Http\Controllers\Platform;

use App\Enums\BusinessStatus;
use App\Enums\ProvisioningState;
use App\Http\Controllers\Controller;
use App\Http\Requests\Platform\StoreBusinessRequest;
use App\Models\AdminUser;
use App\Models\Business;
use App\Models\Plan;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Illuminate\View\View;

class BusinessController extends Controller
{
    public function index(Request $request): View
    {
        $businesses = Business::query()
            ->with(['plan', 'assignedOperator', 'tenant.domains'])
            ->when($request->filled('q'), function ($query) use ($request): void {
                $q = '%'.$request->string('q')->trim().'%';
                $query->where(function ($query) use ($q): void {
                    $query->where('name', 'like', $q)
                        ->orWhere('contact_name', 'like', $q)
                        ->orWhere('phone', 'like', $q)
                        ->orWhere('email', 'like', $q)
                        ->orWhere('requested_subdomain', 'like', $q);
                });
            })
            ->when($request->filled('status'), fn ($query) => $query->where('status', $request->string('status')))
            ->when($request->filled('provisioning_state'), fn ($query) => $query->where('provisioning_state', $request->string('provisioning_state')))
            ->latest()
            ->paginate(20)
            ->withQueryString();

        return view('platform.businesses.index', [
            'businesses' => $businesses,
            'statuses' => BusinessStatus::cases(),
            'provisioningStates' => ProvisioningState::cases(),
        ]);
    }

    public function create(): View
    {
        return view('platform.businesses.create', [
            'plans' => Plan::where('is_active', true)->orderBy('sort_order')->orderBy('name')->get(),
            'operators' => AdminUser::where('is_active', true)->orderBy('name')->get(),
        ]);
    }

    public function store(StoreBusinessRequest $request): RedirectResponse
    {
        $business = DB::transaction(function () use ($request): Business {
            $data = $request->validated();
            $data['requested_subdomain'] = filled($data['requested_subdomain'] ?? null)
                ? Str::lower($data['requested_subdomain'])
                : null;
            $data['status'] = BusinessStatus::Provisioning;
            $data['provisioning_state'] = ProvisioningState::Pending;

            $business = Business::create($data);

            $business->provisioningEvents()->create([
                'event' => 'business.created',
                'state' => ProvisioningState::Pending->value,
                'message' => 'Central restaurant record created; infrastructure provisioning has not started.',
                'occurred_at' => now(),
            ]);

            return $business;
        });

        return redirect('/platform/restaurants/'.$business->id)
            ->with('status', 'Restaurant record created in pending provisioning state.');
    }

    public function show(Business $business): View
    {
        $business->load([
            'plan.features',
            'assignedOperator',
            'tenant.domains',
            'provisioningEvents' => fn ($query) => $query->limit(30),
        ]);

        return view('platform.businesses.show', [
            'business' => $business,
            'plans' => Plan::orderBy('sort_order')->orderBy('name')->get(),
            'operators' => AdminUser::where('is_active', true)->orderBy('name')->get(),
        ]);
    }

    public function update(StoreBusinessRequest $request, Business $business): RedirectResponse
    {
        $data = $request->validated();
        $data['requested_subdomain'] = filled($data['requested_subdomain'] ?? null)
            ? Str::lower($data['requested_subdomain'])
            : null;

        $business->update($data);

        return back()->with('status', 'Restaurant commercial record updated.');
    }
}
