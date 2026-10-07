<!DOCTYPE html>
<html lang="en" dir="ltr">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="theme-color" content="#fffbeb">
    <title>Start your trial · BusinessOS Restaurant</title>
    @if (file_exists(public_path('build/manifest.json')) || file_exists(public_path('hot')))
        @vite(['resources/css/app.css', 'resources/js/app.js'])
    @endif
</head>
<body class="min-h-screen bg-stone-50 text-stone-900 antialiased">
    <main class="relative min-h-screen overflow-hidden">
        <div class="pointer-events-none absolute inset-x-0 top-0 h-[36rem] bg-[radial-gradient(circle_at_25%_10%,rgba(245,158,11,0.16),transparent_35%),radial-gradient(circle_at_80%_20%,rgba(251,146,60,0.10),transparent_28%)]"></div>

        <div class="relative mx-auto max-w-6xl px-5 py-8 sm:px-6 lg:px-8 lg:py-12">
            <div class="flex items-center justify-between gap-4">
                <a href="/" class="flex items-center gap-3">
                    <span class="inline-flex h-10 w-10 items-center justify-center rounded-xl bg-amber-500 font-black text-stone-950">B</span>
                    <span>
                        <span class="block text-xs font-black uppercase tracking-[0.18em] text-amber-700">BusinessOS</span>
                        <span class="block font-black text-stone-950">Restaurant</span>
                    </span>
                </a>
                <a href="/platform/login" class="rounded-xl border border-stone-200 px-3 py-2 text-sm font-bold text-stone-600 hover:bg-white hover:text-stone-950">Platform login</a>
            </div>

            <div class="mt-12 grid gap-10 lg:grid-cols-[0.8fr_1.2fr] lg:items-start">
                <section>
                    <span class="inline-flex rounded-full border border-amber-200 bg-amber-100 px-3 py-1.5 text-xs font-black text-amber-800">7-day hosted trial</span>
                    <h1 class="mt-5 text-4xl font-black tracking-tight text-stone-950 sm:text-5xl">Create your restaurant trial request.</h1>
                    <p class="mt-5 max-w-xl text-base leading-7 text-stone-500">
                        Reserve your restaurant subdomain and tell us where you operate. We provision the isolated workspace first; the full seven-day trial begins only after it is ready.
                    </p>

                    <div class="mt-8 space-y-3 text-sm text-stone-600">
                        @foreach ([
                            'No trial time is lost during provisioning.',
                            'Waiter, kitchen/KOT, cashier, inventory and daily closing included in the architecture.',
                            'Cloud, local-LAN and offline-first workflows supported.',
                            'Designed for AFN and low-bandwidth restaurant operations.',
                        ] as $item)
                            <div class="flex gap-3 rounded-xl border border-stone-200 bg-white/90 p-4">
                                <span class="text-amber-700">✓</span>
                                <span>{{ $item }}</span>
                            </div>
                        @endforeach
                    </div>
                </section>

                <section class="rounded-[1.75rem] border border-stone-200 bg-white p-5 shadow-2xl shadow-stone-900/10 sm:p-7">
                    @if (session('status'))
                        <div class="rounded-2xl border border-amber-200 bg-amber-100 p-5">
                            <p class="font-black text-amber-800">Trial request received</p>
                            <p class="mt-2 text-sm leading-6 text-amber-900">{{ session('status') }}</p>
                            @if (session('trial_request_id'))
                                <p class="mt-3 text-xs text-amber-700/70">Reference: {{ session('trial_request_id') }}</p>
                            @endif
                            <a href="/" class="mt-5 inline-flex rounded-xl bg-amber-500 px-4 py-2.5 text-sm font-black text-stone-950">Back to home</a>
                        </div>
                    @else
                        <div class="mb-6">
                            <p class="text-xs font-black uppercase tracking-[0.18em] text-amber-700">Restaurant details</p>
                            <h2 class="mt-2 text-2xl font-black text-stone-950">Request your workspace</h2>
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
                                <span class="text-sm font-bold text-stone-700">Restaurant name</span>
                                <input name="name" value="{{ old('name') }}" required maxlength="255" autocomplete="organization"
                                       class="mt-2 w-full rounded-xl border border-stone-300 bg-stone-50 px-4 py-3 text-sm text-stone-950 outline-none focus:border-amber-500 focus:ring-2 focus:ring-amber-500"
                                       placeholder="Example: Kabul Grill">
                            </label>

                            <label>
                                <span class="text-sm font-bold text-stone-700">Owner / contact name</span>
                                <input name="contact_name" value="{{ old('contact_name') }}" required maxlength="255" autocomplete="name"
                                       class="mt-2 w-full rounded-xl border border-stone-300 bg-stone-50 px-4 py-3 text-sm text-stone-950 outline-none focus:border-amber-500 focus:ring-2 focus:ring-amber-500"
                                       placeholder="Full name">
                            </label>

                            <label>
                                <span class="text-sm font-bold text-stone-700">Location</span>
                                <input name="location" value="{{ old('location') }}" required maxlength="255"
                                       class="mt-2 w-full rounded-xl border border-stone-300 bg-stone-50 px-4 py-3 text-sm text-stone-950 outline-none focus:border-amber-500 focus:ring-2 focus:ring-amber-500"
                                       placeholder="Kabul">
                            </label>

                            <label>
                                <span class="text-sm font-bold text-stone-700">Phone</span>
                                <input name="phone" value="{{ old('phone') }}" required maxlength="50" inputmode="tel" autocomplete="tel"
                                       class="mt-2 w-full rounded-xl border border-stone-300 bg-stone-50 px-4 py-3 text-sm text-stone-950 outline-none focus:border-amber-500 focus:ring-2 focus:ring-amber-500"
                                       placeholder="+93...">
                            </label>

                            <label>
                                <span class="text-sm font-bold text-stone-700">WhatsApp <span class="font-normal text-stone-500">(optional)</span></span>
                                <input name="whatsapp" value="{{ old('whatsapp') }}" maxlength="50" inputmode="tel"
                                       class="mt-2 w-full rounded-xl border border-stone-300 bg-stone-50 px-4 py-3 text-sm text-stone-950 outline-none focus:border-amber-500 focus:ring-2 focus:ring-amber-500"
                                       placeholder="+93...">
                            </label>

                            <label class="sm:col-span-2">
                                <span class="text-sm font-bold text-stone-700">Email <span class="font-normal text-stone-500">(optional)</span></span>
                                <input name="email" value="{{ old('email') }}" maxlength="255" type="email" autocomplete="email"
                                       class="mt-2 w-full rounded-xl border border-stone-300 bg-stone-50 px-4 py-3 text-sm text-stone-950 outline-none focus:border-amber-500 focus:ring-2 focus:ring-amber-500"
                                       placeholder="owner@example.com">
                            </label>

                            <label class="sm:col-span-2">
                                <span class="text-sm font-bold text-stone-700">Preferred restaurant address</span>
                                <div class="mt-2 flex overflow-hidden rounded-xl border border-stone-300 bg-stone-50 focus-within:border-emerald-400 focus-within:ring-2 focus-within:ring-emerald-400">
                                    <input name="requested_subdomain" value="{{ old('requested_subdomain') }}" required maxlength="100"
                                           pattern="[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?"
                                           class="min-w-0 flex-1 bg-transparent px-4 py-3 text-sm text-stone-950 outline-none"
                                           placeholder="kabul-grill">
                                    <span class="flex items-center border-l border-stone-200 bg-white px-3 text-xs font-bold text-stone-500">.restaurant.businessos.af</span>
                                </div>
                                <span class="mt-2 block text-xs text-stone-500">Lowercase letters, numbers and hyphens only.</span>
                            </label>

                            @if ($plans->isNotEmpty())
                                <label class="sm:col-span-2">
                                    <span class="text-sm font-bold text-stone-700">Plan <span class="font-normal text-stone-500">(optional — choose later if unsure)</span></span>
                                    <select name="plan_id" class="mt-2 w-full rounded-xl border border-stone-300 bg-stone-50 px-4 py-3 text-sm text-stone-950 outline-none focus:border-amber-500 focus:ring-2 focus:ring-amber-500">
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
                                <button class="inline-flex w-full items-center justify-center rounded-xl bg-amber-500 px-5 py-3.5 text-sm font-black text-stone-950 hover:bg-amber-600">
                                    Submit 7-day trial request
                                </button>
                                <p class="mt-3 text-center text-xs leading-5 text-stone-500">Submitting creates a pending restaurant record. The trial starts only after provisioning is complete.</p>
                            </div>
                        </form>
                    @endif
                </section>
            </div>
        </div>
    </main>
</body>
</html>
