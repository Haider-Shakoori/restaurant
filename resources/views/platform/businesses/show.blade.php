@extends('platform.layouts.app')

@section('title', $business->name)
@section('heading', $business->name)
@section('subheading', 'Central commercial and provisioning metadata only. Restaurant operational data stays in the tenant database.')

@section('content')
    <div class="grid gap-6 xl:grid-cols-[1.15fr_0.85fr]">
        <div class="space-y-6">
            <section class="rounded-2xl border border-slate-200 bg-white p-6">
                <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
                    <div>
                        <p class="text-xs uppercase tracking-wide text-slate-500">Business status</p>
                        <p class="mt-1 font-bold">{{ $business->status->label() }}</p>
                    </div>
                    <div>
                        <p class="text-xs uppercase tracking-wide text-slate-500">Provisioning</p>
                        <p class="mt-1 font-bold">{{ $business->provisioning_state->label() }}</p>
                    </div>
                    <div>
                        <p class="text-xs uppercase tracking-wide text-slate-500">Plan</p>
                        <p class="mt-1 font-bold">{{ $business->plan?->name ?? 'Unassigned' }}</p>
                    </div>
                    <div>
                        <p class="text-xs uppercase tracking-wide text-slate-500">Tenant ID</p>
                        <p class="mt-1 break-all font-mono text-xs">{{ $business->tenant_id ?: 'Not provisioned' }}</p>
                    </div>
                    <div>
                        <p class="text-xs uppercase tracking-wide text-slate-500">Domain</p>
                        <p class="mt-1 text-sm">{{ $business->tenant?->domains->first()?->domain ?? $business->requested_subdomain ?? '—' }}</p>
                    </div>
                    <div>
                        <p class="text-xs uppercase tracking-wide text-slate-500">Last health</p>
                        <p class="mt-1 text-sm">{{ $business->last_health_at?->format('Y-m-d H:i') ?? 'Not checked' }}</p>
                    </div>
                </div>

                @if ($business->provisioning_error)
                    <div class="mt-5 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-900">
                        {{ $business->provisioning_error }}
                    </div>
                @endif
            </section>

            <section class="rounded-2xl border border-slate-200 bg-white p-6">
                <h2 class="font-bold">Commercial contact</h2>
                <div class="mt-4 grid gap-4 sm:grid-cols-2">
                    <div><p class="text-xs uppercase tracking-wide text-slate-500">Contact</p><p class="mt-1">{{ $business->contact_name }}</p></div>
                    <div><p class="text-xs uppercase tracking-wide text-slate-500">Operator</p><p class="mt-1">{{ $business->assignedOperator?->name ?? 'Unassigned' }}</p></div>
                    <div><p class="text-xs uppercase tracking-wide text-slate-500">Phone</p><p class="mt-1">{{ $business->phone }}</p></div>
                    <div><p class="text-xs uppercase tracking-wide text-slate-500">WhatsApp</p><p class="mt-1">{{ $business->whatsapp ?: '—' }}</p></div>
                    <div><p class="text-xs uppercase tracking-wide text-slate-500">Email</p><p class="mt-1">{{ $business->email ?: '—' }}</p></div>
                    <div><p class="text-xs uppercase tracking-wide text-slate-500">Location</p><p class="mt-1">{{ $business->location ?: '—' }}</p></div>
                </div>
            </section>

            @can('manage-platform')
                <section class="rounded-2xl border border-slate-200 bg-white p-6">
                    <h2 class="font-bold">Edit commercial record</h2>
                    <form method="POST" action="/platform/restaurants/{{ $business->id }}" class="mt-4">
                        @csrf
                        @method('PUT')
                        <div class="grid gap-4 sm:grid-cols-2">
                            <input name="name" value="{{ old('name', $business->name) }}" required class="rounded-xl border border-slate-300 px-4 py-3">
                            <input name="requested_subdomain" value="{{ old('requested_subdomain', $business->requested_subdomain) }}" placeholder="subdomain" class="rounded-xl border border-slate-300 px-4 py-3">
                            <input name="contact_name" value="{{ old('contact_name', $business->contact_name) }}" required class="rounded-xl border border-slate-300 px-4 py-3">
                            <input name="phone" value="{{ old('phone', $business->phone) }}" required class="rounded-xl border border-slate-300 px-4 py-3">
                            <input name="whatsapp" value="{{ old('whatsapp', $business->whatsapp) }}" placeholder="WhatsApp" class="rounded-xl border border-slate-300 px-4 py-3">
                            <input name="email" type="email" value="{{ old('email', $business->email) }}" placeholder="Email" class="rounded-xl border border-slate-300 px-4 py-3">
                            <input name="location" value="{{ old('location', $business->location) }}" placeholder="Location" class="rounded-xl border border-slate-300 px-4 py-3">
                            <select name="plan_id" class="rounded-xl border border-slate-300 px-4 py-3">
                                <option value="">Unassigned plan</option>
                                @foreach ($plans as $plan)
                                    <option value="{{ $plan->id }}" @selected((string) old('plan_id', $business->plan_id) === (string) $plan->id)>{{ $plan->name }}</option>
                                @endforeach
                            </select>
                            <select name="assigned_operator_id" class="rounded-xl border border-slate-300 px-4 py-3">
                                <option value="">Unassigned operator</option>
                                @foreach ($operators as $operator)
                                    <option value="{{ $operator->id }}" @selected((string) old('assigned_operator_id', $business->assigned_operator_id) === (string) $operator->id)>{{ $operator->name }}</option>
                                @endforeach
                            </select>
                        </div>
                        <button class="mt-4 rounded-xl bg-slate-900 px-5 py-3 font-semibold text-white">Save details</button>
                    </form>
                </section>
            @endcan
        </div>

        <section class="rounded-2xl border border-slate-200 bg-white">
            <div class="border-b border-slate-200 px-5 py-4">
                <h2 class="font-bold">Provisioning history</h2>
                <p class="text-sm text-slate-500">Infrastructure events recorded in the central control plane.</p>
            </div>
            <div class="divide-y divide-slate-100">
                @forelse ($business->provisioningEvents as $event)
                    <div class="px-5 py-4">
                        <div class="flex justify-between gap-3">
                            <span class="font-semibold">{{ $event->event }}</span>
                            <span class="text-xs text-slate-400">{{ $event->occurred_at?->format('Y-m-d H:i') }}</span>
                        </div>
                        <p class="mt-1 text-sm text-slate-600">{{ $event->message ?: 'No message' }}</p>
                        <p class="mt-1 text-xs font-semibold uppercase text-slate-400">{{ $event->state }}</p>
                    </div>
                @empty
                    <p class="px-5 py-8 text-sm text-slate-500">No provisioning events recorded.</p>
                @endforelse
            </div>
        </section>
    </div>
@endsection
