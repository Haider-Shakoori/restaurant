@extends('platform.layouts.app')

@section('title', 'Restaurants')
@section('heading', 'Restaurants')
@section('subheading', 'Central commercial records. Operational orders, payments and inventory stay in tenant databases.')

@section('content')
    <div class="mb-5 flex flex-col gap-3 lg:flex-row lg:items-end lg:justify-between">
        <form method="GET" action="/platform/restaurants" class="grid flex-1 gap-3 sm:grid-cols-3">
            <input name="q" value="{{ request('q') }}" placeholder="Search restaurant, contact, phone…"
                   class="rounded-xl border border-slate-300 bg-white px-4 py-2.5 text-sm">
            <select name="status" class="rounded-xl border border-slate-300 bg-white px-4 py-2.5 text-sm">
                <option value="">All statuses</option>
                @foreach ($statuses as $status)
                    <option value="{{ $status->value }}" @selected(request('status') === $status->value)>{{ $status->label() }}</option>
                @endforeach
            </select>
            <select name="provisioning_state" class="rounded-xl border border-slate-300 bg-white px-4 py-2.5 text-sm">
                <option value="">All provisioning states</option>
                @foreach ($provisioningStates as $state)
                    <option value="{{ $state->value }}" @selected(request('provisioning_state') === $state->value)>{{ $state->label() }}</option>
                @endforeach
            </select>
            <div class="sm:col-span-3 flex gap-2">
                <button class="rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-semibold text-white">Filter</button>
                <a href="/platform/restaurants" class="rounded-xl border border-slate-300 bg-white px-4 py-2.5 text-sm font-semibold">Reset</a>
            </div>
        </form>
        @can('manage-platform')
            <a href="/platform/restaurants/create" class="rounded-xl bg-emerald-500 px-4 py-2.5 text-center text-sm font-bold text-slate-950">Add restaurant</a>
        @endcan
    </div>

    <div class="overflow-hidden rounded-2xl border border-slate-200 bg-white">
        <div class="overflow-x-auto">
            <table class="min-w-full text-left text-sm">
                <thead class="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                    <tr>
                        <th class="px-5 py-3">Restaurant</th>
                        <th class="px-5 py-3">Contact</th>
                        <th class="px-5 py-3">Status</th>
                        <th class="px-5 py-3">Provisioning</th>
                        <th class="px-5 py-3">Tenant / domain</th>
                        <th class="px-5 py-3">Plan</th>
                    </tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                    @forelse ($businesses as $business)
                        <tr class="align-top">
                            <td class="px-5 py-4">
                                <a href="/platform/restaurants/{{ $business->id }}" class="font-semibold hover:text-emerald-700">{{ $business->name }}</a>
                                <div class="mt-1 text-xs text-slate-500">{{ $business->location ?: 'Location not set' }}</div>
                            </td>
                            <td class="px-5 py-4">
                                <div>{{ $business->contact_name }}</div>
                                <div class="text-xs text-slate-500">{{ $business->phone }}</div>
                            </td>
                            <td class="px-5 py-4 font-medium">{{ $business->status->label() }}</td>
                            <td class="px-5 py-4">{{ $business->provisioning_state->label() }}</td>
                            <td class="px-5 py-4">
                                <div class="font-mono text-xs">{{ $business->tenant_id ?: 'Not provisioned' }}</div>
                                <div class="mt-1 text-xs text-slate-500">{{ $business->tenant?->domains->first()?->domain ?? $business->requested_subdomain ?? '—' }}</div>
                            </td>
                            <td class="px-5 py-4">{{ $business->plan?->name ?? 'Unassigned' }}</td>
                        </tr>
                    @empty
                        <tr><td colspan="6" class="px-5 py-10 text-center text-slate-500">No restaurants match the current filters.</td></tr>
                    @endforelse
                </tbody>
            </table>
        </div>
        @if ($businesses->hasPages())
            <div class="border-t border-slate-200 px-5 py-4">{{ $businesses->links() }}</div>
        @endif
    </div>
@endsection
