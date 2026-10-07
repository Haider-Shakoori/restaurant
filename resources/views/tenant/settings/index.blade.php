@extends('tenant.layouts.app')

@section('title', 'Settings')
@section('heading', 'Restaurant Settings')
@section('subheading', 'Configure restaurant operations, mobile connectivity and kitchen structure.')

@section('content')
    <section class="mb-6 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <div class="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
            <div>
                <h2 class="font-black">Desktop & waiter connectivity</h2>
                <p class="mt-1 max-w-3xl text-sm text-slate-500">
                    Waiter apps use Automatic mode: restaurant LAN first, cloud fallback when LAN is unavailable,
                    offline queue when neither path is reachable, then automatic return to LAN.
                </p>
            </div>
            <div class="grid min-w-[280px] grid-cols-2 gap-3">
                <div class="rounded-xl bg-slate-50 p-3">
                    <div class="text-xs font-bold uppercase tracking-wide text-slate-400">Waiter mobiles</div>
                    <div class="mt-1 text-lg font-black">{{ $activeMobileDevices }} / {{ $mobileDeviceLimit ?? '∞' }}</div>
                </div>
                <div class="rounded-xl bg-slate-50 p-3">
                    <div class="text-xs font-bold uppercase tracking-wide text-slate-400">Cloud route</div>
                    <div class="mt-1 text-sm font-black text-emerald-700">Available via this tenant</div>
                </div>
            </div>
        </div>

        <div class="mt-5 overflow-x-auto">
            <table class="min-w-full text-left text-sm">
                <thead class="text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                        <th class="px-3 py-2">Device</th>
                        <th class="px-3 py-2">Platform</th>
                        <th class="px-3 py-2">Status</th>
                        <th class="px-3 py-2">Last seen</th>
                    </tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                    @forelse ($activatedDevices as $device)
                        <tr>
                            <td class="px-3 py-3 font-semibold">{{ $device->device_name ?: 'Unnamed device' }}</td>
                            <td class="px-3 py-3">{{ strtoupper($device->platform) }}</td>
                            <td class="px-3 py-3">{{ ucfirst($device->status->value) }}</td>
                            <td class="px-3 py-3 text-slate-500">{{ $device->last_seen_at?->diffForHumans() ?? 'Never' }}</td>
                        </tr>
                    @empty
                        <tr>
                            <td colspan="4" class="px-3 py-5 text-center text-slate-500">No activated devices yet.</td>
                        </tr>
                    @endforelse
                </tbody>
            </table>
        </div>
    </section>

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
