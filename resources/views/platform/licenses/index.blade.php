@extends('platform.layouts.app')

@section('title', 'License Management')
@section('heading', 'License Management')
@section('subheading', 'Generate and rotate tenant activation licenses, review device usage, and open full license history.')

@section('content')
    @if (session('generated_license_key'))
        <section class="mb-6 rounded-2xl border border-amber-300 bg-amber-50 p-6">
            <div class="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                <div class="min-w-0">
                    <p class="text-sm font-bold uppercase tracking-wide text-amber-800">New license key — copy it now</p>
                    <div class="mt-3 break-all rounded-xl border border-amber-300 bg-white px-4 py-3 font-mono text-lg font-black text-slate-950">
                        {{ session('generated_license_key') }}
                    </div>
                    <p class="mt-3 text-sm text-amber-900">The raw key is shown only once and is not stored in readable form.</p>
                </div>
            </div>
        </section>
    @endif

    <section class="mb-6 rounded-2xl border border-slate-200 bg-white p-5">
        <form method="GET" action="/platform/licenses" class="flex flex-col gap-3 sm:flex-row">
            <div class="min-w-0 flex-1">
                <label for="q" class="sr-only">Search tenants</label>
                <input
                    id="q"
                    name="q"
                    value="{{ $search }}"
                    placeholder="Search restaurant, tenant ID, subdomain, contact, or phone"
                    class="w-full rounded-xl border border-slate-300 px-4 py-2.5 text-sm outline-none ring-emerald-400 focus:ring-2"
                >
            </div>
            <button class="rounded-xl bg-slate-950 px-5 py-2.5 text-sm font-bold text-white">Search</button>
            @if ($search !== '')
                <a href="/platform/licenses" class="rounded-xl border border-slate-300 px-5 py-2.5 text-center text-sm font-semibold text-slate-700">Clear</a>
            @endif
        </form>
    </section>

    <section class="overflow-hidden rounded-2xl border border-slate-200 bg-white">
        <div class="border-b border-slate-200 px-5 py-4">
            <h2 class="font-bold">Tenant licenses</h2>
            <p class="mt-1 text-sm text-slate-500">
                Regenerating a license immediately revokes the previous active license and its active device credentials. Tenant data is not deleted.
            </p>
        </div>

        <div class="overflow-x-auto">
            <table class="min-w-full text-left text-sm">
                <thead class="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                    <tr>
                        <th class="px-5 py-3">Tenant</th>
                        <th class="px-5 py-3">Subscription</th>
                        <th class="px-5 py-3">Current license</th>
                        <th class="px-5 py-3">Devices</th>
                        <th class="px-5 py-3 text-right">Actions</th>
                    </tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                    @forelse ($businesses as $business)
                        @php
                            $license = $business->licenseKeys->first();
                            $domain = $business->tenant?->domains?->first()?->domain;
                        @endphp
                        <tr class="align-top">
                            <td class="px-5 py-4">
                                <div class="font-bold text-slate-950">{{ $business->name }}</div>
                                <div class="mt-1 text-xs text-slate-500">
                                    {{ $domain ?: ($business->requested_subdomain ? $business->requested_subdomain.'.'.config('app.url') : 'No tenant domain') }}
                                </div>
                                <div class="mt-1 font-mono text-[11px] text-slate-400">{{ $business->tenant_id ?: 'Not provisioned' }}</div>
                            </td>
                            <td class="px-5 py-4">
                                <div class="font-semibold">{{ ucfirst($business->status->value) }}</div>
                                <div class="mt-1 text-xs text-slate-500">{{ $business->plan?->name ?: 'No plan' }}</div>
                                <div class="mt-1 text-xs text-slate-500">
                                    Ends: {{ $business->subscription_ends_at?->format('Y-m-d H:i') ?? '—' }}
                                </div>
                            </td>
                            <td class="px-5 py-4">
                                @if ($license)
                                    <div class="font-mono text-xs font-bold">{{ $license->masked() }}</div>
                                    <div class="mt-1 text-xs text-slate-500">
                                        v{{ $license->version }} · {{ ucfirst($license->status->value) }}
                                    </div>
                                    <div class="mt-1 text-xs text-slate-500">
                                        Generated {{ $license->generated_at?->format('Y-m-d H:i') ?? '—' }}
                                    </div>
                                @else
                                    <span class="text-slate-500">No license yet</span>
                                @endif
                            </td>
                            <td class="px-5 py-4">
                                <div class="font-semibold">{{ $business->active_devices_count }} active</div>
                                <div class="mt-1 text-xs text-slate-500">{{ $business->active_mobile_devices_count }} waiter mobile</div>
                            </td>
                            <td class="px-5 py-4">
                                <div class="flex flex-col items-stretch gap-2 sm:items-end">
                                    <a
                                        href="/platform/restaurants/{{ $business->id }}/license"
                                        class="rounded-lg border border-slate-300 px-3 py-2 text-center text-xs font-semibold text-slate-700"
                                    >
                                        View details
                                    </a>

                                    @can('manage-platform')
                                        <form
                                            method="POST"
                                            action="/platform/restaurants/{{ $business->id }}/license/generate"
                                            onsubmit="return confirm('{{ $license ? 'Regenerate this tenant license? The previous license and all active device credentials will be revoked, so devices must activate again.' : 'Generate an activation license for this tenant?' }}')"
                                        >
                                            @csrf
                                            <button
                                                class="w-full rounded-lg bg-emerald-500 px-3 py-2 text-xs font-bold text-slate-950 disabled:cursor-not-allowed disabled:opacity-50"
                                                {{ ! $business->tenant_id ? 'disabled' : '' }}
                                            >
                                                {{ $license ? 'Regenerate license' : 'Generate license' }}
                                            </button>
                                        </form>
                                    @endcan
                                </div>
                            </td>
                        </tr>
                    @empty
                        <tr>
                            <td colspan="5" class="px-5 py-10 text-center text-slate-500">
                                No tenants matched your search.
                            </td>
                        </tr>
                    @endforelse
                </tbody>
            </table>
        </div>

        @if ($businesses->hasPages())
            <div class="border-t border-slate-200 px-5 py-4">
                {{ $businesses->links() }}
            </div>
        @endif
    </section>
@endsection
