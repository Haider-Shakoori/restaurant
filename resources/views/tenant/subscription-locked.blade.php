<!DOCTYPE html>
<html lang="en" dir="ltr">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>Subscription Required · BusinessOS Restaurant</title>
    @if (file_exists(public_path('build/manifest.json')) || file_exists(public_path('hot')))
        @vite(['resources/css/app.css', 'resources/js/app.js'])
    @endif
</head>
<body class="min-h-screen bg-slate-950 text-slate-100">
    <main class="mx-auto flex min-h-screen max-w-3xl items-center px-6 py-16">
        <section class="w-full rounded-2xl border border-slate-800 bg-slate-900 p-7">
            <p class="text-sm font-semibold uppercase tracking-[0.18em] text-amber-300">Restaurant access limited</p>
            <h1 class="mt-3 text-3xl font-black">Subscription renewal is required.</h1>
            <p class="mt-4 text-slate-300">{{ $message }}</p>
            <dl class="mt-6 grid gap-4 sm:grid-cols-2">
                <div class="rounded-xl border border-slate-800 bg-slate-950 p-4">
                    <dt class="text-xs uppercase tracking-wide text-slate-500">Status</dt>
                    <dd class="mt-1 font-semibold">{{ $tenant_status ?: 'Unavailable' }}</dd>
                </div>
                <div class="rounded-xl border border-slate-800 bg-slate-950 p-4">
                    <dt class="text-xs uppercase tracking-wide text-slate-500">Last access end</dt>
                    <dd class="mt-1 font-semibold">{{ $ends_at ?: '—' }}</dd>
                </div>
            </dl>
            @if ($renewal_contact)
                <p class="mt-6 text-sm text-slate-300">Renewal contact: <span class="font-semibold text-white">{{ $renewal_contact }}</span></p>
            @endif
            <p class="mt-6 text-sm text-slate-400">
                Existing restaurant data remains stored. This lock prevents new protected transactions until access is renewed.
            </p>
        </section>
    </main>
</body>
</html>
