@extends('platform.layouts.app')

@section('title', 'Plans & Features')
@section('heading', 'Plans & Features')
@section('subheading', 'Configurable entitlement bundles only. Billing prices and subscription periods are added in Batch 4.')

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
                        <div>
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
            </section>
        @empty
            <div class="rounded-2xl border border-dashed border-slate-300 bg-white p-8 text-slate-500">No plans configured yet.</div>
        @endforelse
    </div>
@endsection
