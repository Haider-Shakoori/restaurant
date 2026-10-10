<!DOCTYPE html>
<html lang="en" dir="ltr">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="theme-color" content="#020617">
    <title>@yield('title', 'Restaurant') · BusinessOS</title>
    @if (file_exists(public_path('build/manifest.json')) || file_exists(public_path('hot')))
        @vite(['resources/css/app.css', 'resources/js/app.js'])
    @endif
</head>
<body class="min-h-screen bg-slate-100 text-slate-900 antialiased @hasSection('pos_fullscreen') overflow-hidden @endif">
@php
    $role = $currentUser?->role;
    $managerRoles = ['owner', 'admin', 'manager'];
    $financeRoles = ['owner', 'admin', 'manager', 'cashier'];
    $inventoryRoles = ['owner', 'admin', 'manager', 'inventory'];
@endphp

<div class="min-h-screen lg:grid @hasSection('pos_fullscreen') lg:grid-cols-1 @else lg:grid-cols-[270px_1fr] @endif">
    <aside class="border-b border-slate-800 bg-slate-950 text-slate-200 lg:min-h-screen lg:border-b-0 lg:border-r @hasSection('pos_fullscreen') hidden @endif">
        <div class="px-5 py-5">
            <a href="/dashboard" class="flex items-center gap-3">
                <span class="inline-flex h-10 w-10 items-center justify-center rounded-xl bg-emerald-400 font-black text-slate-950">B</span>
                <span class="min-w-0">
                    <span class="block text-xs font-black uppercase tracking-[0.16em] text-emerald-300">BusinessOS</span>
                    <span class="block truncate font-black text-white">{{ $restaurant?->name ?? 'Restaurant' }}</span>
                </span>
            </a>
        </div>

        <details class="group border-t border-slate-800 lg:hidden">
            <summary class="flex cursor-pointer list-none items-center justify-between px-5 py-4 text-sm font-bold">
                <span>Restaurant menu</span><span class="text-slate-500 group-open:rotate-180">⌄</span>
            </summary>
            <div class="px-3 pb-5">@include('tenant.partials.nav')</div>
        </details>

        <div class="hidden px-3 pb-6 lg:block">
            @include('tenant.partials.nav')
        </div>

        <div class="hidden border-t border-slate-800 px-5 py-5 text-xs text-slate-400 lg:block">
            <p class="font-bold text-slate-200">{{ $currentUser?->name }}</p>
            <p class="mt-1 capitalize">{{ $role }}</p>
            <form method="POST" action="/logout" class="mt-4">
                @csrf
                <button class="rounded-lg border border-slate-700 px-3 py-2 text-slate-200 hover:bg-slate-900">Sign out</button>
            </form>
        </div>
    </aside>

    <main class="min-w-0 @hasSection('pos_fullscreen') h-[100dvh] overflow-y-auto @endif">
        <header class="border-b border-slate-200 bg-white px-5 py-5 sm:px-6 @hasSection('pos_fullscreen') hidden @endif">
            <div class="mx-auto flex max-w-7xl items-start justify-between gap-4">
                <div>
                    <h1 class="text-2xl font-black tracking-tight text-slate-950">@yield('heading', 'Restaurant')</h1>
                    @hasSection('subheading')
                        <p class="mt-1 text-sm leading-6 text-slate-500">@yield('subheading')</p>
                    @endif
                </div>
                <div class="hidden rounded-full bg-emerald-50 px-3 py-1.5 text-xs font-black text-emerald-700 sm:block">
                    {{ strtoupper(config('restaurant.currency', 'AFN')) }} · {{ ucfirst($role ?? 'user') }}
                </div>
            </div>
        </header>

        <div class="@hasSection('pos_fullscreen') w-full max-w-none p-0 @else mx-auto max-w-7xl px-5 py-6 sm:px-6 @endif">
            @if (session('status'))
                <div class="mb-5 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm font-semibold text-emerald-900">{{ session('status') }}</div>
            @endif

            @if ($errors->any())
                <div class="mb-5 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-900">
                    <ul class="list-disc space-y-1 pl-5">
                        @foreach ($errors->all() as $error)<li>{{ $error }}</li>@endforeach
                    </ul>
                </div>
            @endif

            @yield('content')
        </div>
    </main>
</div>
</body>
</html>
