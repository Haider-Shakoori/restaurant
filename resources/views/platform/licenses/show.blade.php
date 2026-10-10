@extends('platform.layouts.app')

@section('title', 'License · '.$business->name)
@section('heading', 'License & Devices')
@section('subheading', $business->name.' · desktop activation, waiter mobile limits, and signed offline lease control.')

@section('content')
    @if (session('generated_license_key'))
        <section class="mb-6 rounded-2xl border border-amber-300 bg-amber-50 p-6">
            <p class="text-sm font-bold uppercase tracking-wide text-amber-800">Copy this license key now</p>
            <div class="mt-3 break-all rounded-xl border border-amber-300 bg-white px-4 py-3 font-mono text-lg font-black text-slate-950">
                {{ session('generated_license_key') }}
            </div>
            <p class="mt-3 text-sm text-amber-900">The raw key is not stored and will not be displayed again.</p>
        </section>
    @endif

    <section class="mb-6 grid gap-4 sm:grid-cols-3">
        <div class="rounded-2xl border border-slate-200 bg-white p-5">
            <div class="text-xs font-bold uppercase tracking-wide text-slate-500">Waiter mobile allowance</div>
            <div class="mt-2 text-2xl font-black text-slate-950">
                {{ $mobileDeviceLimit === null ? 'Unlimited' : $mobileDeviceLimit }}
            </div>
            <div class="mt-1 text-sm text-slate-500">Android + iOS waiter apps per restaurant activation.</div>
        </div>
        <div class="rounded-2xl border border-slate-200 bg-white p-5">
            <div class="text-xs font-bold uppercase tracking-wide text-slate-500">Active waiter mobiles</div>
            <div class="mt-2 text-2xl font-black text-slate-950">{{ $activeMobileDevices }}</div>
            <div class="mt-1 text-sm text-slate-500">Revoked devices do not consume the allowance.</div>
        </div>
        <div class="rounded-2xl border border-slate-200 bg-white p-5">
            <div class="text-xs font-bold uppercase tracking-wide text-slate-500">Desktop vs mobile</div>
            <div class="mt-2 text-lg font-black text-slate-950">Separate enforcement</div>
            <div class="mt-1 text-sm text-slate-500">The Windows desktop does not consume a waiter-mobile slot.</div>
        </div>
    </section>


    <section class="mb-6 rounded-2xl border border-slate-200 bg-white p-6">
        <h2 class="text-lg font-black text-slate-950">Windows desktop operating mode</h2>
        <p class="mt-2 text-sm text-slate-600">Choose how the licensed Windows restaurant terminal operates. This choice is embedded in a signed lease and applies after the desktop refreshes its license. Other tenant web services are unaffected.</p>
        <form method="POST" action="/platform/restaurants/{{ $business->id }}/license/desktop-mode" class="mt-4 grid gap-4 sm:grid-cols-[1fr_auto] sm:items-end">
            @csrf
            <label class="text-sm font-bold text-slate-700">
                License operating mode
                <select name="desktop_mode" class="mt-2 block w-full rounded-xl border border-slate-300 bg-white px-4 py-3" @cannot('manage-platform') disabled @endcannot>
                    <option value="cloud_sync" @selected(($business->desktop_mode ?? 'cloud_sync') === 'cloud_sync')>Cloud Sync — Local SQLite + web synchronization</option>
                    <option value="standalone_offline" @selected(($business->desktop_mode ?? 'cloud_sync') === 'standalone_offline')>Standalone Offline — Local SQLite + LAN, no web sync</option>
                </select>
            </label>
            @can('manage-platform')
                <button class="rounded-xl bg-violet-600 px-5 py-3 text-sm font-black text-white">Save desktop mode</button>
            @endcan
        </form>
        <p class="mt-3 text-xs text-amber-800">Standalone Offline keeps the signed license valid through the currently paid subscription end date. Initial activation, first operator sign-in, and license renewal or mode changes require an online connection. Existing cloud data is not automatically copied to the standalone database. No cloud data will reconcile while standalone mode is active.</p>
        <p class="mt-2 text-xs text-slate-500">For fully isolated computers without even one-time Internet access, a separately signed offline activation/import and operator provisioning workflow is required.</p>
    </section>

    <div class="grid gap-6 xl:grid-cols-[1fr_0.85fr]">
        <div class="space-y-6">
            <section class="rounded-2xl border border-slate-200 bg-white p-6">
                <div class="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
                    <div>
                        <h2 class="font-bold">Restaurant activation license</h2>
                        <p class="mt-1 text-sm text-slate-500">Used only to activate devices. It is not an API bearer token.</p>
                    </div>
                    @can('manage-platform')
                        <form method="POST" action="/platform/restaurants/{{ $business->id }}/license/generate"
                              onsubmit="return confirm('Generate a new license? Existing license/device credentials will be revoked.')">
                            @csrf
                            <button class="rounded-xl bg-emerald-500 px-4 py-2.5 text-sm font-bold text-slate-950">
                                Generate / rotate license
                            </button>
                        </form>
                    @endcan
                </div>

                <div class="mt-6 grid gap-4">
                    @forelse ($business->licenseKeys as $license)
                        <div class="rounded-xl border border-slate-200 p-4">
                            <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                                <div>
                                    <div class="font-mono text-sm font-bold">{{ $license->masked() }}</div>
                                    <div class="mt-1 text-xs text-slate-500">
                                        Version {{ $license->version }} · {{ ucfirst($license->status->value) }} · generated {{ $license->generated_at?->format('Y-m-d H:i') }}
                                    </div>
                                    <div class="mt-1 text-xs text-slate-500">
                                        Device limit: {{ $license->max_devices_snapshot ?? 'Unlimited' }} ·
                                        {{ $license->devices_count }} device record(s) · {{ $license->leases_count }} lease(s)
                                    </div>
                                </div>
                                @can('manage-platform')
                                    @if ($license->status->value === 'active')
                                        <form method="POST" action="/platform/restaurants/{{ $business->id }}/licenses/{{ $license->id }}/revoke"
                                              onsubmit="return confirm('Revoke this license and its active device credentials?')">
                                            @csrf
                                            <button class="rounded-lg border border-red-300 px-3 py-2 text-xs font-semibold text-red-700">Revoke</button>
                                        </form>
                                    @endif
                                @endcan
                            </div>
                        </div>
                    @empty
                        <div class="rounded-xl border border-dashed border-slate-300 p-6 text-sm text-slate-500">
                            No license generated yet. An active trial or subscription is required before generation.
                        </div>
                    @endforelse
                </div>
            </section>

            <section class="overflow-hidden rounded-2xl border border-slate-200 bg-white">
                <div class="border-b border-slate-200 px-5 py-4">
                    <h2 class="font-bold">Activated devices</h2>
                    <p class="text-sm text-slate-500">Each device has a separate credential. Raw device secrets are never shown here.</p>
                </div>
                <div class="overflow-x-auto">
                    <table class="min-w-full text-left text-sm">
                        <thead class="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                            <tr>
                                <th class="px-5 py-3">Device</th>
                                <th class="px-5 py-3">License</th>
                                <th class="px-5 py-3">Status</th>
                                <th class="px-5 py-3">Last verified</th>
                                <th class="px-5 py-3"></th>
                            </tr>
                        </thead>
                        <tbody class="divide-y divide-slate-100">
                            @forelse ($business->devices as $device)
                                <tr>
                                    <td class="px-5 py-4">
                                        <div class="font-semibold">{{ $device->device_name ?: 'Unnamed device' }}</div>
                                        <div class="font-mono text-xs text-slate-500">{{ $device->device_uid }}</div>
                                        <div class="text-xs text-slate-500">{{ $device->platform }} {{ $device->app_version ?: '' }}</div>
                                    </td>
                                    <td class="px-5 py-4 font-mono text-xs">v{{ $device->licenseKey?->version ?? '—' }}</td>
                                    <td class="px-5 py-4">{{ ucfirst($device->status->value) }}</td>
                                    <td class="px-5 py-4 text-slate-500">{{ $device->last_verified_at?->format('Y-m-d H:i') ?? 'Never' }}</td>
                                    <td class="px-5 py-4 text-right">
                                        @can('manage-platform')
                                            @if ($device->status->value === 'active')
                                                <form method="POST" action="/platform/restaurants/{{ $business->id }}/devices/{{ $device->id }}/revoke"
                                                      onsubmit="return confirm('Revoke this device credential?')">
                                                    @csrf
                                                    <button class="rounded-lg border border-red-300 px-3 py-2 text-xs font-semibold text-red-700">Revoke</button>
                                                </form>
                                            @endif
                                        @endcan
                                    </td>
                                </tr>
                            @empty
                                <tr><td colspan="5" class="px-5 py-8 text-center text-slate-500">No devices activated yet.</td></tr>
                            @endforelse
                        </tbody>
                    </table>
                </div>
            </section>
        </div>

        <div class="space-y-6">
            <section class="rounded-2xl border border-slate-200 bg-white p-6">
                <h2 class="font-bold">Offline lease signing</h2>
                <dl class="mt-4 grid gap-4 text-sm">
                    <div>
                        <dt class="text-xs uppercase tracking-wide text-slate-500">Algorithm</dt>
                        <dd class="mt-1 font-semibold">Ed25519</dd>
                    </div>
                    <div>
                        <dt class="text-xs uppercase tracking-wide text-slate-500">Key ID</dt>
                        <dd class="mt-1 font-mono text-xs">{{ config('license.signing.key_id') }}</dd>
                    </div>
                    <div>
                        <dt class="text-xs uppercase tracking-wide text-slate-500">Offline grace</dt>
                        <dd class="mt-1 font-semibold">{{ config('license.offline_grace_days') }} day(s), capped by subscription expiry</dd>
                    </div>
                </dl>
                <p class="mt-5 text-sm text-slate-500">
                    The waiter app and Windows desktop verify signed lease claims with the public key, including the mobile-device allowance. The private signing key remains server-only.
                </p>
            </section>

            <section class="rounded-2xl border border-slate-200 bg-white">
                <div class="border-b border-slate-200 px-5 py-4">
                    <h2 class="font-bold">License audit</h2>
                    <p class="text-sm text-slate-500">Generation, activation, lease and revocation events.</p>
                </div>
                <div class="divide-y divide-slate-100">
                    @forelse ($business->licenseEvents as $event)
                        <div class="px-5 py-4">
                            <div class="flex justify-between gap-3">
                                <span class="font-semibold">{{ $event->event }}</span>
                                <span class="text-xs text-slate-400">{{ $event->occurred_at?->format('Y-m-d H:i') }}</span>
                            </div>
                            <p class="mt-1 text-sm text-slate-600">{{ $event->message ?: 'No message' }}</p>
                        </div>
                    @empty
                        <p class="px-5 py-8 text-sm text-slate-500">No license events yet.</p>
                    @endforelse
                </div>
            </section>
        </div>
    </div>
@endsection
