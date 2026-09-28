<!DOCTYPE html>
<html lang="en" dir="ltr">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="theme-color" content="#020617">
    <title>Restaurant Login · BusinessOS</title>
    @if (file_exists(public_path('build/manifest.json')) || file_exists(public_path('hot')))
        @vite(['resources/css/app.css', 'resources/js/app.js'])
    @endif
</head>
<body class="min-h-screen bg-slate-950 text-slate-100 antialiased">
    <main class="relative flex min-h-screen items-center justify-center overflow-hidden px-5 py-10">
        <div class="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_20%_10%,rgba(52,211,153,0.15),transparent_35%),radial-gradient(circle_at_80%_30%,rgba(56,189,248,0.08),transparent_28%)]"></div>

        <div class="relative w-full max-w-md">
            <div class="mb-7 flex items-center gap-3">
                <span class="inline-flex h-11 w-11 items-center justify-center rounded-xl bg-emerald-400 text-lg font-black text-slate-950">B</span>
                <div>
                    <p class="text-xs font-black uppercase tracking-[0.18em] text-emerald-300">BusinessOS</p>
                    <h1 class="font-black text-white">Restaurant</h1>
                </div>
            </div>

            <section class="rounded-[1.75rem] border border-slate-800 bg-slate-900/90 p-6 shadow-2xl shadow-black/30 sm:p-8">
                <p class="text-xs font-black uppercase tracking-[0.16em] text-emerald-300">Restaurant workspace</p>
                <h2 class="mt-2 text-3xl font-black tracking-tight text-white">Sign in to your restaurant</h2>
                <p class="mt-3 text-sm leading-6 text-slate-400">Use the owner or staff credentials issued for this restaurant.</p>

                @if ($errors->any())
                    <div class="mt-5 rounded-xl border border-red-400/20 bg-red-400/10 px-4 py-3 text-sm text-red-100">
                        {{ $errors->first() }}
                    </div>
                @endif

                <form method="POST" action="/login" class="mt-7 space-y-5">
                    @csrf
                    <label class="block">
                        <span class="text-sm font-bold text-slate-200">Email</span>
                        <input name="email" type="email" value="{{ old('email') }}" required autocomplete="email"
                               class="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950 px-4 py-3 text-sm text-white outline-none focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400"
                               placeholder="owner@example.com">
                    </label>

                    <label class="block">
                        <span class="text-sm font-bold text-slate-200">Password</span>
                        <input name="password" type="password" required autocomplete="current-password"
                               class="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950 px-4 py-3 text-sm text-white outline-none focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400"
                               placeholder="••••••••">
                    </label>

                    <label class="flex items-center gap-3 text-sm text-slate-400">
                        <input name="remember" value="1" type="checkbox" class="h-4 w-4 rounded border-slate-600 bg-slate-900">
                        Keep me signed in on this device
                    </label>

                    <button class="w-full rounded-xl bg-emerald-400 px-5 py-3.5 text-sm font-black text-slate-950 hover:bg-emerald-300">
                        Sign in
                    </button>
                </form>
            </section>

            <p class="mt-5 text-center text-xs text-slate-600">BusinessOS Restaurant · {{ request()->getHost() }}</p>
        </div>
    </main>
</body>
</html>
