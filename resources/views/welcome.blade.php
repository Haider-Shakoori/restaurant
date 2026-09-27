<!DOCTYPE html>
@php
    $locale = app()->getLocale();
    $direction = data_get(config('restaurant.locales'), $locale.'.direction', 'ltr');
@endphp
<html lang="{{ str_replace('_', '-', $locale) }}" dir="{{ $direction }}">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="color-scheme" content="light">
    <title>{{ config('app.name') }}</title>
    @if (file_exists(public_path('build/manifest.json')) || file_exists(public_path('hot')))
        @vite(['resources/css/app.css', 'resources/js/app.js'])
    @endif
</head>
<body class="min-h-screen bg-slate-950 text-slate-100 antialiased">
    <main class="mx-auto flex min-h-screen max-w-6xl items-center px-6 py-16">
        <section class="w-full">
            <div class="mb-8 flex items-center gap-3">
                <span class="inline-flex h-11 w-11 items-center justify-center rounded-xl bg-emerald-400 font-bold text-slate-950">B</span>
                <div>
                    <p class="text-sm font-semibold text-emerald-300">BusinessOS</p>
                    <h1 class="text-xl font-bold">Restaurant</h1>
                </div>
            </div>
            <div class="grid gap-8 lg:grid-cols-[1.35fr_0.65fr] lg:items-end">
                <div>
                    <p class="mb-3 text-sm font-semibold uppercase tracking-[0.2em] text-emerald-300">SaaS restaurant operating platform</p>
                    <h2 class="max-w-4xl text-4xl font-black tracking-tight sm:text-6xl">
                        Fast waiter ordering built for unreliable connectivity.
                    </h2>
                    <p class="mt-6 max-w-3xl text-base leading-7 text-slate-300 sm:text-lg">
                        Internal restaurant operations for waiters, tables, kitchen/KOT, cashier, inventory and offline synchronization.
                        This is not a public food-delivery or customer self-ordering application.
                    </p>
                    <div class="mt-8 flex flex-wrap gap-3 text-sm">
                        <span class="rounded-full border border-slate-700 px-4 py-2">AFN</span>
                        <span class="rounded-full border border-slate-700 px-4 py-2">English · Dari · Pashto</span>
                        <span class="rounded-full border border-slate-700 px-4 py-2">Offline-first waiter app</span>
                        <span class="rounded-full border border-slate-700 px-4 py-2">Multi-tenant SaaS</span>
                    </div>
                </div>
                <aside class="rounded-2xl border border-slate-800 bg-slate-900 p-6">
                    <div class="flex items-center justify-between">
                        <span class="text-sm text-slate-400">Foundation status</span>
                        <span class="rounded-full bg-emerald-400/15 px-3 py-1 text-xs font-bold text-emerald-300">READY</span>
                    </div>
                    <dl class="mt-6 space-y-4 text-sm">
                        <div class="flex justify-between gap-6"><dt class="text-slate-400">API</dt><dd>/api/{{ config('restaurant.api.version') }}</dd></div>
                        <div class="flex justify-between gap-6"><dt class="text-slate-400">Currency</dt><dd>{{ config('restaurant.currency') }}</dd></div>
                        <div class="flex justify-between gap-6"><dt class="text-slate-400">Timezone</dt><dd>{{ config('restaurant.timezone') }}</dd></div>
                        <div class="flex justify-between gap-6"><dt class="text-slate-400">Bandwidth mode</dt><dd>{{ config('restaurant.performance.low_bandwidth_mode') ? 'Optimized' : 'Standard' }}</dd></div>
                    </dl>
                </aside>
            </div>
        </section>
    </main>
</body>
</html>