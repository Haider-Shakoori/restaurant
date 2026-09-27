@extends('platform.layouts.app')

@section('title', $business->name)
@section('heading', $business->name)
@section('subheading', 'Commercial relationship, provisioning and subscription truth. Tenant operational data is not queried here.')

@section('content')
    <div class="grid gap-6 xl:grid-cols-[1.15fr_0.85fr]">
        <div class="space-y-6">
            <section class="rounded-2xl border border-slate-200 bg-white p-6">
                <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
                    <div><p class="text-xs uppercase tracking-wide text-slate-500">Business status</p><p class="mt-1 font-bold">{{ $business->status->label() }}</p></div>
                    <div><p class="text-xs uppercase tracking-wide text-slate-500">Access status</p><p class="mt-1 font-bold">{{ ucfirst($subscriptionAccess->status) }}</p></div>
                    <div><p class="text-xs uppercase tracking-wide text-slate-500">Provisioning</p><p class="mt-1 font-bold">{{ $business->provisioning_state->label() }}</p></div>
                    <div><p class="text-xs uppercase tracking-wide text-slate-500">Plan</p><p class="mt-1 font-bold">{{ $business->plan?->name ?? 'Unassigned' }}</p></div>
                    <div><p class="text-xs uppercase tracking-wide text-slate-500">Tenant ID</p><p class="mt-1 break-all font-mono text-xs">{{ $business->tenant_id ?: 'Not provisioned' }}</p></div>
                    <div><p class="text-xs uppercase tracking-wide text-slate-500">Access ends</p><p class="mt-1">{{ $subscriptionAccess->endsAt?->format('Y-m-d H:i') ?? '—' }}</p></div>
                </div>

                @if ($business->provisioning_error)
                    <div class="mt-5 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-900">{{ $business->provisioning_error }}</div>
                @endif

                <div class="mt-6 rounded-xl border {{ $subscriptionAccess->allowed ? 'border-emerald-200 bg-emerald-50' : 'border-amber-200 bg-amber-50' }} p-4">
                    <p class="font-bold {{ $subscriptionAccess->allowed ? 'text-emerald-900' : 'text-amber-900' }}">
                        {{ $subscriptionAccess->allowed ? 'Tenant transactions are allowed.' : 'Protected tenant transactions are locked.' }}
                    </p>
                    <p class="mt-1 text-sm {{ $subscriptionAccess->allowed ? 'text-emerald-800' : 'text-amber-800' }}">
                        Server code: {{ $subscriptionAccess->code }}
                    </p>
                </div>
            </section>

            @can('manage-platform')
                <section class="rounded-2xl border border-slate-200 bg-white p-6">
                    <h2 class="font-bold">Subscription actions</h2>
                    <p class="mt-1 text-sm text-slate-500">Trial starts only after successful provisioning. Renewal history is never overwritten.</p>

                    <div class="mt-5 grid gap-4 lg:grid-cols-2">
                        <div class="rounded-xl border border-slate-200 p-4">
                            <h3 class="font-semibold">Hosted trial</h3>
                            <p class="mt-1 text-sm text-slate-500">
                                {{ $trialUsed ? 'This restaurant has already used its hosted trial.' : 'Eligible for one 7-day trial after provisioning is ready.' }}
                            </p>
                            <form method="POST" action="/platform/restaurants/{{ $business->id }}/trial/start" class="mt-4">
                                @csrf
                                <button @disabled($trialUsed || ! $business->tenant_id || $business->provisioning_state->value !== 'ready' || ! $business->plan_id)
                                        class="rounded-xl bg-emerald-500 px-4 py-2.5 text-sm font-bold text-slate-950 disabled:cursor-not-allowed disabled:opacity-40">
                                    Start 7-day trial
                                </button>
                            </form>
                        </div>

                        <div class="rounded-xl border border-slate-200 p-4">
                            <h3 class="font-semibold">Renew subscription</h3>
                            @if ($renewalPrices->isEmpty())
                                <p class="mt-2 text-sm text-slate-500">Add an active price to the assigned plan before renewal.</p>
                            @else
                                <form method="POST" action="/platform/restaurants/{{ $business->id }}/subscription/renew" class="mt-4 space-y-3">
                                    @csrf
                                    <select name="plan_price_id" required class="w-full rounded-xl border border-slate-300 px-4 py-2.5 text-sm">
                                        @foreach ($renewalPrices as $price)
                                            <option value="{{ $price->id }}">
                                                {{ $price->billing_cycle->label() }} · {{ $price->price }} {{ $price->currency }}
                                            </option>
                                        @endforeach
                                    </select>
                                    <input name="custom_days" type="number" min="1" max="3650" placeholder="Custom days (only for Custom cycle)"
                                           class="w-full rounded-xl border border-slate-300 px-4 py-2.5 text-sm">
                                    <button class="rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-semibold text-white">Record renewal</button>
                                </form>
                            @endif
                        </div>
                    </div>

                    @if ($subscriptionAccess->allowed || $subscriptionAccess->status === 'scheduled')
                        <form method="POST" action="/platform/restaurants/{{ $business->id }}/subscription/cancel" class="mt-4"
                              onsubmit="return confirm('Cancel current and scheduled access without deleting restaurant data?')">
                            @csrf
                            <button class="rounded-xl border border-red-300 bg-white px-4 py-2.5 text-sm font-semibold text-red-700">Cancel access periods</button>
                        </form>
                    @endif
                </section>
            @endcan

            <section class="overflow-hidden rounded-2xl border border-slate-200 bg-white">
                <div class="border-b border-slate-200 px-5 py-4">
                    <h2 class="font-bold">Subscription periods</h2>
                    <p class="text-sm text-slate-500">Immutable commercial snapshots used for server-side access checks.</p>
                </div>
                <div class="overflow-x-auto">
                    <table class="min-w-full text-left text-sm">
                        <thead class="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                            <tr>
                                <th class="px-5 py-3">Source</th>
                                <th class="px-5 py-3">Plan</th>
                                <th class="px-5 py-3">Period</th>
                                <th class="px-5 py-3">Price</th>
                                <th class="px-5 py-3">Recorded status</th>
                            </tr>
                        </thead>
                        <tbody class="divide-y divide-slate-100">
                            @forelse ($business->subscriptions as $subscription)
                                <tr>
                                    <td class="px-5 py-4">{{ ucfirst(str_replace('_', ' ', $subscription->source->value)) }}</td>
                                    <td class="px-5 py-4">
                                        <div class="font-semibold">{{ $subscription->plan_name_snapshot }}</div>
                                        <div class="font-mono text-xs text-slate-500">{{ $subscription->plan_code_snapshot }}</div>
                                    </td>
                                    <td class="px-5 py-4">
                                        <div>{{ $subscription->starts_at->format('Y-m-d H:i') }}</div>
                                        <div class="text-xs text-slate-500">to {{ $subscription->ends_at->format('Y-m-d H:i') }}</div>
                                    </td>
                                    <td class="px-5 py-4">{{ $subscription->price_snapshot }} {{ $subscription->currency }}</td>
                                    <td class="px-5 py-4">{{ $subscription->status->label() }}</td>
                                </tr>
                            @empty
                                <tr><td colspan="5" class="px-5 py-8 text-center text-slate-500">No subscription periods yet.</td></tr>
                            @endforelse
                        </tbody>
                    </table>
                </div>
            </section>

            @can('manage-platform')
                <section class="rounded-2xl border border-slate-200 bg-white p-6">
                    <h2 class="font-bold">Commercial details</h2>
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

        <div class="space-y-6">
            <section class="rounded-2xl border border-slate-200 bg-white">
                <div class="border-b border-slate-200 px-5 py-4">
                    <h2 class="font-bold">Subscription audit</h2>
                    <p class="text-sm text-slate-500">Trial, renewal and cancellation events.</p>
                </div>
                <div class="divide-y divide-slate-100">
                    @forelse ($business->subscriptionEvents as $event)
                        <div class="px-5 py-4">
                            <div class="flex justify-between gap-3">
                                <span class="font-semibold">{{ $event->event }}</span>
                                <span class="text-xs text-slate-400">{{ $event->occurred_at?->format('Y-m-d H:i') }}</span>
                            </div>
                            <p class="mt-1 text-sm text-slate-600">{{ $event->message ?: 'No message' }}</p>
                            <p class="mt-1 text-xs font-semibold uppercase text-slate-400">{{ $event->status ?: '—' }}</p>
                        </div>
                    @empty
                        <p class="px-5 py-8 text-sm text-slate-500">No subscription events recorded.</p>
                    @endforelse
                </div>
            </section>

            <section class="rounded-2xl border border-slate-200 bg-white">
                <div class="border-b border-slate-200 px-5 py-4">
                    <h2 class="font-bold">Provisioning history</h2>
                    <p class="text-sm text-slate-500">Infrastructure audit trail from the central control plane.</p>
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
    </div>
@endsection
