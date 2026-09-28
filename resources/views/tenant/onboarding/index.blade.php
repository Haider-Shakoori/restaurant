@extends('tenant.layouts.app')

@section('title', 'Onboarding')
@section('heading', 'Restaurant Onboarding')
@section('subheading', 'Complete the setup in stages. Batch 6 enables Restaurant and Branch; later modules attach to the same progress record.')

@section('content')
    <div class="mb-6 overflow-x-auto">
        <div class="flex min-w-max gap-2">
            @foreach ($steps as $step)
                @php
                    $completed = $progress->hasCompleted($step);
                    $current = $progress->current_step === $step;
                @endphp
                <div class="rounded-xl border px-4 py-3 text-sm
                    {{ $completed ? 'border-emerald-200 bg-emerald-50 text-emerald-900' : ($current ? 'border-slate-900 bg-slate-900 text-white' : 'border-slate-200 bg-white text-slate-500') }}">
                    <div class="font-semibold">{{ $loop->iteration }}. {{ $step->label() }}</div>
                    <div class="mt-1 text-xs opacity-70">
                        {{ $completed ? 'Completed' : ($step->isImplemented() ? ($current ? 'Current' : 'Available') : 'Future batch') }}
                    </div>
                </div>
            @endforeach
        </div>
    </div>

    <div class="grid gap-6 xl:grid-cols-2">
        <section class="rounded-2xl border border-slate-200 bg-white p-6">
            <div class="mb-5">
                <h2 class="text-lg font-black">1. Restaurant information</h2>
                <p class="mt-1 text-sm text-slate-500">Tenant-level identity and Afghanistan defaults.</p>
            </div>

            <form method="POST" action="/onboarding/restaurant" class="grid gap-4 sm:grid-cols-2">
                @csrf
                @method('PUT')
                <label class="sm:col-span-2">
                    <span class="mb-2 block text-sm font-semibold">Restaurant name</span>
                    <input name="name" required value="{{ old('name', $restaurant?->name) }}" class="w-full rounded-xl border border-slate-300 px-4 py-3">
                </label>
                <label>
                    <span class="mb-2 block text-sm font-semibold">Phone</span>
                    <input name="phone" value="{{ old('phone', $restaurant?->phone) }}" class="w-full rounded-xl border border-slate-300 px-4 py-3">
                </label>
                <label>
                    <span class="mb-2 block text-sm font-semibold">Email</span>
                    <input name="email" type="email" value="{{ old('email', $restaurant?->email) }}" class="w-full rounded-xl border border-slate-300 px-4 py-3">
                </label>
                <label class="sm:col-span-2">
                    <span class="mb-2 block text-sm font-semibold">Address</span>
                    <input name="address" value="{{ old('address', $restaurant?->address) }}" class="w-full rounded-xl border border-slate-300 px-4 py-3">
                </label>
                <label>
                    <span class="mb-2 block text-sm font-semibold">Country</span>
                    <input name="country_code" maxlength="2" value="{{ old('country_code', $restaurant?->country_code ?? 'AF') }}" class="w-full rounded-xl border border-slate-300 px-4 py-3 uppercase">
                </label>
                <label>
                    <span class="mb-2 block text-sm font-semibold">Currency</span>
                    <input name="currency" maxlength="3" value="{{ old('currency', $restaurant?->currency ?? 'AFN') }}" class="w-full rounded-xl border border-slate-300 px-4 py-3 uppercase">
                </label>
                <label>
                    <span class="mb-2 block text-sm font-semibold">Timezone</span>
                    <input name="timezone" value="{{ old('timezone', $restaurant?->timezone ?? 'Asia/Kabul') }}" class="w-full rounded-xl border border-slate-300 px-4 py-3">
                </label>
                <label>
                    <span class="mb-2 block text-sm font-semibold">Language</span>
                    <select name="locale" class="w-full rounded-xl border border-slate-300 px-4 py-3">
                        @foreach (config('restaurant.locales') as $code => $locale)
                            <option value="{{ $code }}" @selected(old('locale', $restaurant?->locale ?? 'en') === $code)>{{ $locale['name'] }}</option>
                        @endforeach
                    </select>
                </label>
                <div class="sm:col-span-2">
                    <button class="rounded-xl bg-slate-900 px-5 py-3 font-semibold text-white">Save restaurant</button>
                </div>
            </form>
        </section>

        <section class="rounded-2xl border border-slate-200 bg-white p-6">
            <div class="mb-5">
                <h2 class="text-lg font-black">2. Primary branch</h2>
                <p class="mt-1 text-sm text-slate-500">
                    The first branch becomes primary. Plan limit: {{ $maxBranches ?: 'unlimited / not configured' }}.
                </p>
            </div>

            @if (! $restaurant)
                <div class="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
                    Save restaurant information before creating the primary branch.
                </div>
            @else
                <form method="POST" action="/onboarding/branch" class="grid gap-4">
                    @csrf
                    @method('PUT')
                    <label>
                        <span class="mb-2 block text-sm font-semibold">Branch code</span>
                        <input name="code" required value="{{ old('code', $primaryBranch?->code ?? 'MAIN') }}" class="w-full rounded-xl border border-slate-300 px-4 py-3 uppercase">
                    </label>
                    <label>
                        <span class="mb-2 block text-sm font-semibold">Branch name</span>
                        <input name="name" required value="{{ old('name', $primaryBranch?->name ?? 'Main Branch') }}" class="w-full rounded-xl border border-slate-300 px-4 py-3">
                    </label>
                    <label>
                        <span class="mb-2 block text-sm font-semibold">Phone</span>
                        <input name="phone" value="{{ old('phone', $primaryBranch?->phone) }}" class="w-full rounded-xl border border-slate-300 px-4 py-3">
                    </label>
                    <label>
                        <span class="mb-2 block text-sm font-semibold">Address</span>
                        <input name="address" value="{{ old('address', $primaryBranch?->address) }}" class="w-full rounded-xl border border-slate-300 px-4 py-3">
                    </label>
                    <button class="rounded-xl bg-emerald-500 px-5 py-3 font-bold text-slate-950">Save primary branch</button>
                </form>
            @endif
        </section>
    </div>

    @if ($progress->hasCompleted($steps[1]))
        <div class="mt-6 rounded-2xl border border-blue-200 bg-blue-50 p-5 text-sm text-blue-900">
            Restaurant and branch setup are complete. The next onboarding step is <strong>{{ $progress->current_step->label() }}</strong>, which will be implemented in its scheduled batch.
        </div>
    @endif
@endsection
