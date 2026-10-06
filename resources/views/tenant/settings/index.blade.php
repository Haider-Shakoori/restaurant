@extends('tenant.layouts.app')

@section('title', 'Settings')
@section('heading', 'Restaurant Settings')
@section('subheading', 'Configure branches, dining areas and kitchen stations.')

@section('content')
    <div class="grid gap-6 xl:grid-cols-3">
        <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 class="font-black">Add branch</h2>
            <form method="POST" action="/setup/branch" class="mt-4 space-y-3">
                @csrf
                <input name="code" required placeholder="Code e.g. MAIN" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                <input name="name" required placeholder="Branch name" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                <button class="w-full rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-bold text-white">Create branch</button>
            </form>
        </section>

        <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 class="font-black">Add dining area</h2>
            <form method="POST" action="/setup/area" class="mt-4 space-y-3">
                @csrf
                <select name="branch_id" required class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                    <option value="">Choose branch</option>
                    @foreach ($branches as $branch)<option value="{{ $branch->id }}">{{ $branch->name }}</option>@endforeach
                </select>
                <input name="name" required placeholder="Area name e.g. Main Hall" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                <button class="w-full rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-bold text-white">Create area</button>
            </form>
        </section>

        <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 class="font-black">Add kitchen station</h2>
            <form method="POST" action="/setup/station" class="mt-4 space-y-3">
                @csrf
                <select name="branch_id" required class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                    <option value="">Choose branch</option>
                    @foreach ($branches as $branch)<option value="{{ $branch->id }}">{{ $branch->name }}</option>@endforeach
                </select>
                <input name="code" required placeholder="Code e.g. HOT" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                <input name="name" required placeholder="Station name" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                <button class="w-full rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-bold text-white">Create station</button>
            </form>
        </section>
    </div>

    <section class="mt-6 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <div class="flex flex-wrap items-start justify-between gap-4">
            <div>
                <p class="text-xs font-black uppercase tracking-[0.18em] text-sky-600">BusinessOS connectivity</p>
                <h2 class="mt-1 text-lg font-black">Desktop & mobile synchronization</h2>
                <p class="mt-1 max-w-3xl text-sm text-slate-500">
                    The Windows desktop remains the local restaurant host while this Laravel tenant stays the cloud authority for subscription, staff, configuration and reconciliation.
                    Waiter phones can be paired from Desktop → Settings using a short-lived one-time QR code.
                </p>
            </div>
            <div class="rounded-xl bg-slate-950 px-4 py-3 text-white">
                <p class="text-xs font-bold uppercase tracking-wide text-slate-400">Mobile activations</p>
                <p class="mt-1 text-xl font-black">{{ $activeMobileCount }} / {{ $mobileDeviceLimit ?? 'Unlimited' }}</p>
            </div>
        </div>

        <div class="mt-5 overflow-hidden rounded-xl border border-slate-200">
            <div class="grid grid-cols-4 gap-3 bg-slate-50 px-4 py-2 text-xs font-black uppercase tracking-wide text-slate-500">
                <span>Device</span>
                <span>Platform</span>
                <span>Status</span>
                <span>Last seen</span>
            </div>
            @forelse ($mobileDevices as $device)
                <div class="grid grid-cols-4 gap-3 border-t border-slate-100 px-4 py-3 text-sm">
                    <span class="font-semibold">{{ $device->device_name ?: 'Unnamed mobile' }}</span>
                    <span class="uppercase text-slate-500">{{ $device->platform }}</span>
                    <span class="font-semibold {{ $device->status->value === 'active' ? 'text-emerald-600' : 'text-rose-600' }}">
                        {{ ucfirst($device->status->value) }}
                    </span>
                    <span class="text-slate-500">{{ $device->last_seen_at?->diffForHumans() ?: 'Never' }}</span>
                </div>
            @empty
                <p class="px-4 py-5 text-sm text-slate-500">No mobile devices have been activated yet.</p>
            @endforelse
        </div>
    </section>

    <section class="mt-6 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <h2 class="font-black">Current structure</h2>
        <div class="mt-4 grid gap-4 lg:grid-cols-2">
            @forelse ($branches as $branch)
                <div class="rounded-xl border border-slate-200 p-4">
                    <div class="flex items-center justify-between gap-3">
                        <div><p class="font-black">{{ $branch->name }}</p><p class="text-xs text-slate-500">{{ $branch->code }}</p></div>
                        <span class="text-xs font-bold text-emerald-700">{{ $branch->is_active ? 'Active' : 'Inactive' }}</span>
                    </div>
                    <div class="mt-4 grid gap-3 sm:grid-cols-2">
                        <div><p class="text-xs font-bold uppercase text-slate-400">Dining areas</p><p class="mt-1 text-sm">{{ $branch->diningAreas->pluck('name')->join(', ') ?: 'None' }}</p></div>
                        <div><p class="text-xs font-bold uppercase text-slate-400">Kitchen stations</p><p class="mt-1 text-sm">{{ $branch->kitchenStations->pluck('name')->join(', ') ?: 'None' }}</p></div>
                    </div>
                </div>
            @empty
                <p class="text-sm text-slate-500">No branches configured yet.</p>
            @endforelse
        </div>
    </section>
@endsection
