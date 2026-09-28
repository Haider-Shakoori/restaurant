<!DOCTYPE html>
<html lang="en" dir="ltr">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="theme-color" content="#020617">
    <title>Start your trial · BusinessOS Restaurant</title>
    @if (file_exists(public_path('build/manifest.json')) || file_exists(public_path('hot')))
        @vite(['resources/css/app.css', 'resources/js/app.js'])
    @endif
</head>
<body class="min-h-screen bg-slate-950 text-slate-100 antialiased">
    <main class="relative min-h-screen overflow-hidden">
        <div class="pointer-events-none absolute inset-x-0 top-0 h-[36rem] bg-[radial-gradient(circle_at_25%_10%,rgba(52,211,153,0.14),transparent_35%),radial-gradient(circle_at_80%_20%,rgba(56,189,248,0.08),transparent_28%)]"></div>

        <div class="relative mx-auto max-w-6xl px-5 py-8 sm:px-6 lg:px-8 lg:py-12">
            <div class="flex items-center justify-between gap-4">
                <a href="/" class="flex items-center gap-3">
                    <span class="inline-flex h-10 w-10 items-center justify-center rounded-xl bg-emerald-400 font-black text-slate-950">B</span>
                    <span>
                        <span class="block text-xs font-black uppercase tracking-[0.18em] text-emerald-300">BusinessOS</span>
                        <span class="block font-black text-white">Restaurant</span>
                    </span>
                </a>
                <a href="/platform/login" class="rounded-xl border border-slate-800 px-3 py-2 text-sm font-bold text-slate-300 hover:bg-slate-900 hover:text-white">Platform login</a>
            </div>

            <div class="mt-12 grid gap-10 lg:grid-cols-[0.8fr_1.2fr] lg:items-start">
                <section>
                    <span class="inline-flex rounded-full border border-emerald-400/20 bg-emerald-400/10 px-3 py-1.5 text-xs font-black text-emerald-200">7-day hosted trial</span>
                    <h1 class="mt-5 text-4xl font-black tracking-tight text-white sm:text-5xl">Create your restaurant trial request.</h1>
                    <p class="mt-5 max-w-xl text-base leading-7 text-slate-400">
                        Reserve your restaurant subdomain and tell us where you operate. We provision the isolated workspace first; the full seven-day trial begins only after it is ready.
                    </p>

                    <div class="mt-8 space-y-3 text-sm text-slate-300">
                        @foreach ([
                            'No trial time is lost during provisioning.',
                            'Waiter, kitchen/KOT, cashier, inventory and daily closing included in the architecture.',
                            'Cloud, local-LAN and offline-first workflows supported.',
                            'Designed for AFN and low-bandwidth restaurant operations.',
                        ] as $item)
                            <div class="flex gap-3 rounded-xl border border-slate-800 bg-slate-900/50 p-4">
                                <span class="text-emerald-300">✓</span>
                                <span>{{ $item }}</span>
                            </div>
                        @endforeach
                    </div>
                </section>

                <section class="rounded-[1.75rem] border border-slate-800 bg-slate-900/90 p-5 shadow-2xl shadow-black/20 sm:p-7">
                    @if (session('status'))
                        <div class="rounded-2xl border border-emerald-400/20 bg-emerald-400/10 p-5">
                            <p class="font-black text-emerald-200">Trial request received</p>
                            <p class="mt-2 text-sm leading-6 text-emerald-100">{{ session('status') }}</p>
                            @if (session('trial_request_id'))
                                <p class="mt-3 text-xs text-emerald-200/70">Reference: {{ session('trial_request_id') }}</p>
                            @endif
                            <a href="/" class="mt-5 inline-flex rounded-xl bg-emerald-400 px-4 py-2.5 text-sm font-black text-slate-950">Back to home</a>
                        </div>
                    @else
                        <div class="mb-6">
                            <p class="text-xs font-black uppercase tracking-[0.18em] text-emerald-300">Restaurant details</p>
                            <h2 class="mt-2 text-2xl font-black text-white">Request your workspace</h2>
                        </div>

                        @if ($errors->any())
                            <div class="mb-6 rounded-2xl border border-red-400/20 bg-red-400/10 p-4 text-sm text-red-100">
                                <p class="font-bold">Please fix the following:</p>
                                <ul class="mt-2 list-disc space-y-1 pl-5">
                                    @foreach ($errors->all() as $error)
                                        <li>{{ $error }}</li>
                                    @endforeach
                                </ul>
                            </div>
                        @endif

                        <form method="POST" action="/start-trial" class="grid gap-5 sm:grid-cols-2">
                            @csrf

                            <label class="sm:col-span-2">
                                <span class="text-sm font-bold text-slate-200">Restaurant name</span>
                                <input name="name" value="{{ old('name') }}" required maxlength="255" autocomplete="organization"
                                       class="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950 px-4 py-3 text-sm text-white outline-none focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400"
                                       placeholder="Example: Kabul Grill">
                            </label>

                            <label>
                                <span class="text-sm font-bold text-slate-200">Owner / contact name</span>
                                <input name="contact_name" value="{{ old('contact_name') }}" required maxlength="255" autocomplete="name"
                                       class="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950 px-4 py-3 text-sm text-white outline-none focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400"
                                       placeholder="Full name">
                            </label>

                            <label>
                                <span class="text-sm font-bold text-slate-200">Location</span>
                                <input name="location" value="{{ old('location') }}" required maxlength="255"
                                       class="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950 px-4 py-3 text-sm text-white outline-none focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400"
                                       placeholder="Kabul">
                            </label>

                            <label>
                                <span class="text-sm font-bold text-slate-200">Phone</span>
                                <input name="phone" value="{{ old('phone') }}" required maxlength="50" inputmode="tel" autocomplete="tel"
                                       class="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950 px-4 py-3 text-sm text-white outline-none focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400"
                                       placeholder="+93...">
                            </label>

                            <label>
                                <span class="text-sm font-bold text-slate-200">WhatsApp <span class="font-normal text-slate-500">(optional)</span></span>
                                <input name="whatsapp" value="{{ old('whatsapp') }}" maxlength="50" inputmode="tel"
                                       class="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950 px-4 py-3 text-sm text-white outline-none focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400"
                                       placeholder="+93...">
                            </label>

                            <label class="sm:col-span-2">
                                <span class="text-sm font-bold text-slate-200">Email <span class="font-normal text-slate-500">(optional)</span></span>
                                <input name="email" value="{{ old('email') }}" maxlength="255" type="email" autocomplete="email"
                                       class="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950 px-4 py-3 text-sm text-white outline-none focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400"
                                       placeholder="owner@example.com">
                            </label>

                            <label class="sm:col-span-2">
                                <span class="text-sm font-bold text-slate-200">Preferred restaurant address</span>
                                <div class="mt-2 flex overflow-hidden rounded-xl border border-slate-700 bg-slate-950 focus-within:border-emerald-400 focus-within:ring-2 focus-within:ring-emerald-400">
                                    <input name="requested_subdomain" value="{{ old('requested_subdomain') }}" required maxlength="100"
                                           pattern="[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?"
                                           class="min-w-0 flex-1 bg-transparent px-4 py-3 text-sm text-white outline-none"
                                           placeholder="kabul-grill">
                                    <span class="flex items-center border-l border-slate-800 bg-slate-900 px-3 text-xs font-bold text-slate-400">.restaurant.businessos.af</span>
                                </div>
                                <span class="mt-2 block text-xs text-slate-500">Lowercase letters, numbers and hyphens only.</span>
                            </label>

                            @if ($plans->isNotEmpty())
                                <label class="sm:col-span-2">
                                    <span class="text-sm font-bold text-slate-200">Plan <span class="font-normal text-slate-500">(optional — choose later if unsure)</span></span>
                                    <select name="plan_id" class="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950 px-4 py-3 text-sm text-white outline-none focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400">
                                        <option value="">Choose later</option>
                                        @foreach ($plans as $plan)
                                            <option value="{{ $plan->id }}" @selected((string) old('plan_id') === (string) $plan->id)>
                                                {{ $plan->name }}
                                                @if ($plan->prices->isNotEmpty())
                                                    — from {{ number_format((float) $plan->prices->first()->price, 0) }} {{ $plan->prices->first()->currency }}
                                                @endif
                                            </option>
                                        @endforeach
                                    </select>
                                </label>
                            @endif

                            <div class="sm:col-span-2 pt-2">
                                <button class="inline-flex w-full items-center justify-center rounded-xl bg-emerald-400 px-5 py-3.5 text-sm font-black text-slate-950 hover:bg-emerald-300">
                                    Submit 7-day trial request
                                </button>
                                <p class="mt-3 text-center text-xs leading-5 text-slate-500">Submitting creates a pending restaurant record. The trial starts only after provisioning is complete.</p>
                            </div>
                        </form>
                    @endif
                </section>
            </div>
        </div>
    </main>
</body>
</html>
