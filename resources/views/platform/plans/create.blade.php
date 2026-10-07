@extends('platform.layouts.app')

@section('title', 'Create Plan')
@section('heading', 'Create Plan')
@section('subheading', 'Create a configurable entitlement bundle. Pricing is intentionally deferred to the subscription/billing batch.')

@section('content')
    <form method="POST" action="/platform/plans" class="max-w-3xl rounded-2xl border border-slate-200 bg-white p-6">
        @csrf
        <div class="grid gap-5 sm:grid-cols-2">
            <label>
                <span class="mb-2 block text-sm font-semibold">Code</span>
                <input name="code" value="{{ old('code') }}" placeholder="starter" required class="w-full rounded-xl border border-slate-300 px-4 py-3 font-mono">
            </label>
            <label>
                <span class="mb-2 block text-sm font-semibold">Name</span>
                <input name="name" value="{{ old('name') }}" required class="w-full rounded-xl border border-slate-300 px-4 py-3">
            </label>
            <label class="sm:col-span-2">
                <span class="mb-2 block text-sm font-semibold">Description</span>
                <textarea name="description" rows="3" class="w-full rounded-xl border border-slate-300 px-4 py-3">{{ old('description') }}</textarea>
            </label>
            <label>
                <span class="mb-2 block text-sm font-semibold">Sort order</span>
                <input name="sort_order" type="number" min="0" value="{{ old('sort_order', 0) }}" class="w-full rounded-xl border border-slate-300 px-4 py-3">
            </label>
            <label class="flex items-center gap-2 pt-8 text-sm">
                <input type="hidden" name="is_active" value="0">
                <input name="is_active" type="checkbox" value="1" checked>
                Active
            </label>
        </div>

        @php
            $entitlementExamples = [
                ['key' => 'max_waiters', 'value' => '10'],
                ['key' => 'max_mobile_devices', 'value' => '5'],
                ['key' => 'max_branches', 'value' => '1'],
                ['key' => 'inventory', 'value' => 'true'],
            ];
        @endphp

        <div class="mt-6">
            <p class="mb-3 font-semibold">Initial feature entitlements</p>
            <div class="mb-2 grid grid-cols-2 gap-2 text-xs font-semibold uppercase tracking-wide text-slate-500">
                <span>Feature</span>
                <span>Value</span>
            </div>
            @foreach ($entitlementExamples as $i => $example)
                <div class="mb-2 grid grid-cols-2 gap-2">
                    <input
                        name="features[{{ $i }}][key]"
                        placeholder="{{ $example['key'] }}"
                        class="rounded-xl border border-slate-300 px-4 py-2.5"
                    >
                    <input
                        name="features[{{ $i }}][value]"
                        placeholder="{{ $example['value'] }}"
                        class="rounded-xl border border-slate-300 px-4 py-2.5"
                    >
                </div>
            @endforeach
        </div>

        <div class="mt-6 flex gap-3">
            <button class="rounded-xl bg-emerald-500 px-5 py-3 font-bold text-slate-950">Create plan</button>
            <a href="/platform/plans" class="rounded-xl border border-slate-300 px-5 py-3 font-semibold">Cancel</a>
        </div>
    </form>
@endsection
