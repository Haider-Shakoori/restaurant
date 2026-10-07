<!DOCTYPE html>
<html lang="en" dir="ltr">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>Platform Login · BusinessOS Restaurant</title>
    @if (file_exists(public_path('build/manifest.json')) || file_exists(public_path('hot')))
        @vite(['resources/css/app.css', 'resources/js/app.js'])
    @endif
</head>
<body class="min-h-screen bg-stone-50 text-stone-900">
    <main class="mx-auto flex min-h-screen max-w-6xl items-center justify-center px-6 py-16">
        <section class="w-full max-w-md rounded-2xl border border-stone-200 bg-white p-7 shadow-2xl">
            <div class="mb-7 flex items-center gap-3">
                <span class="inline-flex h-11 w-11 items-center justify-center rounded-xl bg-amber-500 font-black text-stone-950">B</span>
                <div>
                    <p class="text-xs font-semibold uppercase tracking-[0.18em] text-amber-700">BusinessOS Restaurant</p>
                    <h1 class="text-xl font-black">Platform Admin</h1>
                </div>
            </div>

            @if ($errors->any())
                <div class="mb-5 rounded-xl border border-red-800 bg-red-950/40 px-4 py-3 text-sm text-red-200">
                    {{ $errors->first() }}
                </div>
            @endif

            <form method="POST" action="/platform/login" class="space-y-5">
                @csrf
                <label class="block">
                    <span class="mb-2 block text-sm font-semibold text-stone-600">Email</span>
                    <input name="email" type="email" value="{{ old('email') }}" required autofocus
                           class="w-full rounded-xl border border-stone-300 bg-stone-50 px-4 py-3 outline-none focus:border-amber-500">
                </label>
                <label class="block">
                    <span class="mb-2 block text-sm font-semibold text-stone-600">Password</span>
                    <input name="password" type="password" required
                           class="w-full rounded-xl border border-stone-300 bg-stone-50 px-4 py-3 outline-none focus:border-amber-500">
                </label>
                <label class="flex items-center gap-2 text-sm text-stone-600">
                    <input name="remember" type="checkbox" value="1" class="rounded border-stone-300 bg-stone-50">
                    Remember me
                </label>
                <button class="w-full rounded-xl bg-amber-500 px-4 py-3 font-bold text-stone-950 hover:bg-amber-600">Sign in</button>
            </form>
        </section>
    </main>
</body>
</html>
