@extends('platform.layouts.app')

@section('title', 'Dashboard')
@section('heading', 'Platform Dashboard')
@section('subheading', 'Central SaaS health, restaurant lifecycle and provisioning visibility.')

@section('content')
    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        @foreach ([
            ['Restaurants', $metrics['restaurants']],
            ['Active', $metrics['active']],
            ['Trial', $metrics['trial']],
            ['Expired', $metrics['expired']],
            ['Tenants', $metrics['tenants']],
            ['Failed provisioning', $metrics['provisioning_failed']],
            ['Active plans', $metrics['active_plans']],
            ['Operators', $metrics['operators']],
        ] as [$label, $value])
            <div class="rounded-2xl border border-slate-200 bg-white p-5">
                <p class="text-sm font-medium text-slate-500">{{ $label }}</p>
                <p class="mt-2 text-3xl font-black tracking-tight">{{ number_format($value) }}</p>
            </div>
        @endforeach
    </div>

    <div class="mt-6 grid gap-6 xl:grid-cols-[1.3fr_0.7fr]">
        <section class="overflow-hidden rounded-2xl border border-slate-200 bg-white">
            <div class="flex items-center justify-between border-b border-slate-200 px-5 py-4">
                <div>
                    <h2 class="font-bold">Recent restaurants</h2>
                    <p class="text-sm text-slate-500">Commercial records and infrastructure state.</p>
                </div>
                <a href="/platform/restaurants" class="text-sm font-semibold text-emerald-700">View all</a>
            </div>
            <div class="overflow-x-auto">
                <table class="min-w-full text-left text-sm">
                    <thead class="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                        <tr>
                            <th class="px-5 py-3">Restaurant</th>
                            <th class="px-5 py-3">Status</th>
                            <th class="px-5 py-3">Provisioning</th>
                            <th class="px-5 py-3">Plan</th>
                        </tr>
                    </thead>
                    <tbody class="divide-y divide-slate-100">
                        @forelse ($recentBusinesses as $business)
                            <tr>
                                <td class="px-5 py-4">
                                    <a href="/platform/restaurants/{{ $business->id }}" class="font-semibold hover:text-emerald-700">{{ $business->name }}</a>
                                    <div class="text-xs text-slate-500">{{ $business->requested_subdomain ?: 'No subdomain reserved' }}</div>
                                </td>
                                <td class="px-5 py-4">{{ $business->status->label() }}</td>
                                <td class="px-5 py-4">{{ $business->provisioning_state->label() }}</td>
                                <td class="px-5 py-4">{{ $business->plan?->name ?? 'Unassigned' }}</td>
                            </tr>
                        @empty
                            <tr><td colspan="4" class="px-5 py-8 text-center text-slate-500">No restaurant records yet.</td></tr>
                        @endforelse
                    </tbody>
                </table>
            </div>
        </section>

        <section class="rounded-2xl border border-slate-200 bg-white">
            <div class="border-b border-slate-200 px-5 py-4">
                <h2 class="font-bold">Provisioning activity</h2>
                <p class="text-sm text-slate-500">Central event history only.</p>
            </div>
            <div class="divide-y divide-slate-100">
                @forelse ($recentEvents as $event)
                    <div class="px-5 py-4">
                        <div class="flex items-start justify-between gap-3">
                            <p class="font-semibold">{{ $event->business?->name ?? 'Unknown restaurant' }}</p>
                            <span class="text-xs text-slate-400">{{ $event->occurred_at?->diffForHumans() }}</span>
                        </div>
                        <p class="mt-1 text-sm text-slate-600">{{ $event->message ?: $event->event }}</p>
                        <p class="mt-1 text-xs font-semibold uppercase tracking-wide text-slate-400">{{ $event->state }}</p>
                    </div>
                @empty
                    <p class="px-5 py-8 text-sm text-slate-500">No provisioning events yet.</p>
                @endforelse
            </div>
        </section>
    </div>
@endsection
