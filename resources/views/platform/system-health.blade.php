@extends('platform.layouts.app')

@section('title', 'System Health')
@section('heading', 'System Health')
@section('subheading', 'Internal foundation and runtime configuration for the Restaurant platform.')

@section('content')
    <div class="grid gap-5 lg:grid-cols-2">
        <section class="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
            <div class="flex items-center justify-between gap-4">
                <div>
                    <p class="text-xs font-bold uppercase tracking-[0.16em] text-slate-400">Foundation status</p>
                    <h2 class="mt-2 text-xl font-black text-slate-900">Restaurant platform</h2>
                </div>
                <span class="rounded-full bg-emerald-100 px-3 py-1 text-xs font-black text-emerald-700">READY</span>
            </div>

            <dl class="mt-6 divide-y divide-slate-100 text-sm">
                <div class="flex items-center justify-between gap-6 py-3">
                    <dt class="text-slate-500">API</dt>
                    <dd class="font-semibold text-slate-900">/api/{{ config('restaurant.api.version') }}</dd>
                </div>
                <div class="flex items-center justify-between gap-6 py-3">
                    <dt class="text-slate-500">Currency</dt>
                    <dd class="font-semibold text-slate-900">{{ config('restaurant.currency') }}</dd>
                </div>
                <div class="flex items-center justify-between gap-6 py-3">
                    <dt class="text-slate-500">Timezone</dt>
                    <dd class="font-semibold text-slate-900">{{ config('restaurant.timezone') }}</dd>
                </div>
                <div class="flex items-center justify-between gap-6 py-3">
                    <dt class="text-slate-500">Bandwidth mode</dt>
                    <dd class="font-semibold text-slate-900">{{ config('restaurant.performance.low_bandwidth_mode') ? 'Optimized' : 'Standard' }}</dd>
                </div>
            </dl>
        </section>

        <section class="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
            <p class="text-xs font-bold uppercase tracking-[0.16em] text-slate-400">Connectivity architecture</p>
            <h2 class="mt-2 text-xl font-black text-slate-900">Supported operating modes</h2>

            <div class="mt-6 grid gap-3">
                @foreach ([
                    ['Cloud HTTPS', 'Restaurant tenant endpoint over the public internet.'],
                    ['Local LAN', 'Restaurant-local Apache/Laravel endpoint on the same network.'],
                    ['Automatic local-first', 'Prefer the local health endpoint and use cloud when appropriate.'],
                    ['Offline outbox', 'Mobile mutations queue locally and synchronize idempotently after reconnection.'],
                ] as $mode)
                    <div class="rounded-xl border border-slate-200 bg-slate-50 p-4">
                        <p class="font-bold text-slate-900">{{ $mode[0] }}</p>
                        <p class="mt-1 text-sm leading-6 text-slate-500">{{ $mode[1] }}</p>
                    </div>
                @endforeach
            </div>
        </section>
    </div>
@endsection
