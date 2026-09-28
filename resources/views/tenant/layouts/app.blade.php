@php
    $locale = app()->getLocale();
    $direction = data_get(config('restaurant.locales'), $locale.'.direction', 'ltr');
@endphp
<!DOCTYPE html>
<html lang="{{ str_replace('_', '-', $locale) }}" dir="{{ $direction }}">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>@yield('title', 'Restaurant') · BusinessOS</title>
    @if (file_exists(public_path('build/manifest.json')) || file_exists(public_path('hot')))
        @vite(['resources/css/app.css', 'resources/js/app.js'])
    @endif
</head>
<body class="min-h-screen bg-slate-100 text-slate-900">
    <div class="min-h-screen lg:grid lg:grid-cols-[240px_1fr]">
        <aside class="bg-slate-950 px-5 py-5 text-slate-200">
            <a href="/onboarding" class="flex items-center gap-3">
                <span class="inline-flex h-10 w-10 items-center justify-center rounded-xl bg-emerald-400 font-black text-slate-950">B</span>
                <span>
                    <span class="block text-xs font-semibold uppercase tracking-[0.18em] text-emerald-300">BusinessOS</span>
                    <span class="block font-bold">Restaurant</span>
                </span>
            </a>

            <nav class="mt-8 grid gap-1 text-sm">
                <a href="/onboarding" class="rounded-lg px-3 py-2 hover:bg-slate-900">Onboarding</a>
                <a href="/branches" class="rounded-lg px-3 py-2 hover:bg-slate-900">Branches</a>
            </nav>

            <div class="mt-8 border-t border-slate-800 pt-5 text-xs text-slate-400">
                <p class="font-semibold text-slate-200">{{ auth('tenant')->user()?->name }}</p>
                <p>{{ tenant('id') }}</p>
                <form method="POST" action="/logout" class="mt-4">
                    @csrf
                    <button class="rounded-lg border border-slate-700 px-3 py-2 text-slate-200">Sign out</button>
                </form>
            </div>
        </aside>

        <main class="min-w-0">
            <header class="border-b border-slate-200 bg-white px-6 py-5">
                <div class="mx-auto max-w-7xl">
                    <h1 class="text-2xl font-black tracking-tight">@yield('heading', 'Restaurant')</h1>
                    @hasSection('subheading')
                        <p class="mt-1 text-sm text-slate-500">@yield('subheading')</p>
                    @endif
                </div>
            </header>

            <div class="mx-auto max-w-7xl px-6 py-6">
                @if (session('status'))
                    <div class="mb-5 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-900">{{ session('status') }}</div>
                @endif

                @if ($errors->any())
                    <div class="mb-5 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-900">
                        <ul class="list-disc space-y-1 ps-5">
                            @foreach ($errors->all() as $error)
                                <li>{{ $error }}</li>
                            @endforeach
                        </ul>
                    </div>
                @endif

                @yield('content')
            </div>
        </main>
    </div>
</body>
</html>
