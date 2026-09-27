@extends('platform.layouts.app')

@section('title', 'Plans & Pricing')
@section('heading', 'Plans, Features & Pricing')
@section('subheading', 'Entitlements and billing prices are data-driven; subscription periods keep immutable snapshots.')

@section('content')
    <div class="mb-5 flex justify-end">
        <a href="/platform/plans/create" class="rounded-xl bg-emerald-500 px-4 py-2.5 text-sm font-bold text-slate-950">Create plan</a>
    </div>

    <div class="grid gap-6">
        @forelse ($plans as $plan)
            <section class="rounded-2xl border border-slate-200 bg-white p-6">
                <form method="POST" action="/platform/plans/{{ $plan->id }}">
                    @csrf
                    @method('PUT')
                    <div class="flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between">
                        <div class="flex-1">
                            <div class="flex items-center gap-3">
                                <h2 class="text-lg font-black">{{ $plan->name }}</h2>
                                <span class="rounded-full px-3 py-1 text-xs font-bold {{ $plan->is_active ? 'bg-emerald-100 text-emerald-800' : 'bg-slate-100 text-slate-600' }}">
                                    {{ $plan->is_active ? 'Active' : 'Inactive' }}
                                </span>
                            </div>
                            <p class="mt-1 font-mono text-xs text-slate-500">{{ $plan->code }}</p>
                            <p class="mt-2 text-xs text-slate-400">{{ $plan->businesses_count }} restaurant(s) assigned</p>
                        </div>
                    </div>

                    <div class="mt-5 grid gap-3 lg:grid-cols-2">
                        <input name="name" value="{{ $plan->name }}" class="rounded-xl border border-slate-300 px-4 py-2.5">
                        <input name="code" value="{{ $plan->code }}" class="rounded-xl border border-slate-300 px-4 py-2.5 font-mono text-sm">
                        <textarea name="description" rows="2" class="rounded-xl border border-slate-300 px-4 py-2.5 lg:col-span-2">{{ $plan->description }}</textarea>
                        <input name="sort_order" type="number" min="0" value="{{ $plan->sort_order }}" class="rounded-xl border border-slate-300 px-4 py-2.5">
                        <label class="flex items-center gap-2 text-sm">
                            <input type="hidden" name="is_active" value="0">
                            <input type="checkbox" name="is_active" value="1" @checked($plan->is_active)>
                            Active
                        </label>
                    </div>

                    <div class="mt-5">
                        <p class="mb-2 text-xs font-bold uppercase tracking-wide text-slate-500">Feature entitlements</p>
                        <div class="grid gap-2 lg:grid-cols-2">
                            @foreach ($plan->features as $feature)
                                <div class="grid grid-cols-2 gap-2">
                                    <input name="features[{{ $loop->index }}][key]" value="{{ $feature->feature_key }}" class="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                                    <input name="features[{{ $loop->index }}][value]" value="{{ data_get($feature->value, 'value') }}" class="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                                </div>
                            @endforeach
                            @for ($i = $plan->features->count(); $i < $plan->features->count() + 2; $i++)
                                <div class="grid grid-cols-2 gap-2">
                                    <input name="features[{{ $i }}][key]" placeholder="feature_key" class="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                                    <input name="features[{{ $i }}][value]" placeholder="value" class="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                                </div>
                            @endfor
                        </div>
                    </div>

                    <button class="mt-5 rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-semibold text-white">Save plan</button>
                </form>

                <div class="mt-7 border-t border-slate-200 pt-6">
                    <div class="mb-4">
                        <h3 class="font-bold">Billing prices</h3>
                        <p class="text-sm text-slate-500">Monthly, quarterly, semiannual, annual or custom renewal terms.</p>
                    </div>

                    <div class="grid gap-3 xl:grid-cols-2">
                        @foreach ($plan->prices as $price)
                            <form method="POST" action="/platform/plans/{{ $plan->id }}/prices/{{ $price->id }}"
                                  class="grid gap-3 rounded-xl border border-slate-200 bg-slate-50 p-4 sm:grid-cols-2">
                                @csrf
                                @method('PUT')
                                <select name="billing_cycle" class="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm">
                                    @foreach ($billingCycles as $cycle)
                                        <option value="{{ $cycle->value }}" @selected($price->billing_cycle === $cycle)>{{ $cycle->label() }}</option>
                                    @endforeach
                                </select>
                                <div class="grid grid-cols-[1fr_90px] gap-2">
                                    <input name="price" value="{{ $price->price }}" inputmode="decimal" class="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm">
                                    <input name="currency" value="{{ $price->currency }}" maxlength="3" class="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm uppercase">
                                </div>
                                <input name="sort_order" type="number" min="0" value="{{ $price->sort_order }}" class="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm">
                                <label class="flex items-center gap-2 text-sm">
                                    <input type="hidden" name="is_active" value="0">
                                    <input type="checkbox" name="is_active" value="1" @checked($price->is_active)>
                                    Active
                                </label>
                                <button class="rounded-lg bg-white px-3 py-2 text-sm font-semibold ring-1 ring-slate-300 sm:col-span-2">Update price</button>
                            </form>
                        @endforeach
                    </div>

                    <form method="POST" action="/platform/plans/{{ $plan->id }}/prices"
                          class="mt-4 grid gap-3 rounded-xl border border-dashed border-slate-300 p-4 sm:grid-cols-2 lg:grid-cols-5">
                        @csrf
                        <select name="billing_cycle" class="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                            @foreach ($billingCycles as $cycle)
                                <option value="{{ $cycle->value }}">{{ $cycle->label() }}</option>
                            @endforeach
                        </select>
                        <input name="price" placeholder="Price" inputmode="decimal" required class="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                        <input name="currency" value="AFN" maxlength="3" required class="rounded-lg border border-slate-300 px-3 py-2 text-sm uppercase">
                        <input name="sort_order" type="number" min="0" value="0" class="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                        <div class="flex gap-3">
                            <input type="hidden" name="is_active" value="1">
                            <button class="w-full rounded-lg bg-emerald-500 px-3 py-2 text-sm font-bold text-slate-950">Add price</button>
                        </div>
                    </form>
                </div>
            </section>
        @empty
            <div class="rounded-2xl border border-dashed border-slate-300 bg-white p-8 text-slate-500">No plans configured yet.</div>
        @endforelse
    </div>
@endsection
