<!DOCTYPE html>
@php
    $language = request()->query('lang', 'en');
    $language = in_array($language, ['en', 'fa', 'ps'], true) ? $language : 'en';
    $direction = $language === 'en' ? 'ltr' : 'rtl';

    $copy = [
        'en' => [
            'features' => 'Features',
            'connectivity' => 'Connectivity',
            'trial' => 'Free trial',
            'login' => 'Platform login',
            'eyebrow' => 'Restaurant operations built for Afghanistan',
            'headline' => 'Run every table, order, kitchen ticket and closing from one system.',
            'intro' => 'BusinessOS Restaurant connects waiters, kitchen, cashier, inventory and management — with offline-first workflows designed for unreliable internet.',
            'start' => 'Start 7-day free trial',
            'explore' => 'Explore features',
            'trial_note' => 'Your seven days start after provisioning, so setup time never consumes your trial.',
            'flow' => 'Live restaurant flow',
            'table' => 'Table A12 · Dine-in',
            'kitchen' => 'Sent to kitchen',
            'offline' => 'Offline-ready',
            'feature_title' => 'Everything your restaurant team needs',
            'feature_text' => 'One operating system from the dining floor to the back office.',
            'connect_title' => 'Works the way your connection works',
            'connect_text' => 'Use cloud, local network or automatic local-first operation without changing the restaurant workflow.',
            'cta_title' => 'Try BusinessOS Restaurant with your own team.',
            'cta_text' => 'Reserve your restaurant subdomain and request a full seven-day hosted trial.',
            'request' => 'Request my trial',
            'footer' => 'Built for restaurant operations — not public food delivery.',
        ],
        'fa' => [
            'features' => 'امکانات',
            'connectivity' => 'اتصال',
            'trial' => 'آزمایش رایگان',
            'login' => 'ورود پلتفرم',
            'eyebrow' => 'مدیریت رستورانت برای افغانستان',
            'headline' => 'میزها، سفارش‌ها، آشپزخانه و بستن روزانه را از یک سیستم مدیریت کنید.',
            'intro' => 'BusinessOS Restaurant گارسون، آشپزخانه، صندوق، موجودی و مدیریت را با روند آفلاین‌محور برای اینترنت ناپایدار یکجا می‌کند.',
            'start' => 'شروع آزمایش رایگان ۷ روزه',
            'explore' => 'مشاهده امکانات',
            'trial_note' => 'آزمایش هفت‌روزه بعد از آماده‌سازی شروع می‌شود؛ زمان تنظیمات از دوره آزمایشی کم نمی‌شود.',
            'flow' => 'جریان زنده رستورانت',
            'table' => 'میز A12 · داخل رستورانت',
            'kitchen' => 'ارسال به آشپزخانه',
            'offline' => 'آماده آفلاین',
            'feature_title' => 'تمام ابزارهای مورد نیاز تیم رستورانت',
            'feature_text' => 'یک سیستم عملیاتی از سالن تا مدیریت.',
            'connect_title' => 'متناسب با وضعیت اینترنت شما',
            'connect_text' => 'بدون تغییر روند کاری از کلاود، شبکه محلی یا حالت خودکار استفاده کنید.',
            'cta_title' => 'BusinessOS Restaurant را با تیم خود امتحان کنید.',
            'cta_text' => 'ساب‌دامین رستورانت خود را رزرو و آزمایش کامل هفت‌روزه را درخواست کنید.',
            'request' => 'درخواست آزمایش',
            'footer' => 'برای عملیات داخلی رستورانت — نه تحویل غذای عمومی.',
        ],
        'ps' => [
            'features' => 'ځانګړتیاوې',
            'connectivity' => 'نښلون',
            'trial' => 'وړیا آزموینه',
            'login' => 'پلېټفارم ته ننوتل',
            'eyebrow' => 'د افغانستان لپاره د رستورانت عملیات',
            'headline' => 'مېزونه، فرمایشونه، پخلنځی او ورځنی تړل له یوه سیسټم څخه اداره کړئ.',
            'intro' => 'BusinessOS Restaurant ویټر، پخلنځی، کاشیر، موجودي او مدیریت سره نښلوي او د کمزوري انټرنېټ لپاره آفلاین-لومړی کاري جریان لري.',
            'start' => '۷ ورځنۍ وړیا آزموینه پیل کړئ',
            'explore' => 'ځانګړتیاوې وګورئ',
            'trial_note' => 'اووه ورځنۍ آزموینه د چمتو کولو وروسته پیلېږي، نو د تنظیم وخت له آزموینې نه کمېږي.',
            'flow' => 'ژوندی رستورانت جریان',
            'table' => 'مېز A12 · رستورانت',
            'kitchen' => 'پخلنځي ته ولېږل شو',
            'offline' => 'آفلاین چمتو',
            'feature_title' => 'هر څه چې ستاسو د رستورانت ټیم ورته اړتیا لري',
            'feature_text' => 'له تالار څخه تر مدیریت پورې یو عملیاتي سیسټم.',
            'connect_title' => 'ستاسو د شبکې له حالت سره سم کار کوي',
            'connect_text' => 'کلاوډ، محلي شبکه یا اتومات local-first حالت وکاروئ.',
            'cta_title' => 'BusinessOS Restaurant له خپل ټیم سره وازمویئ.',
            'cta_text' => 'د خپل رستورانت subdomain خوندي او بشپړه اووه ورځنۍ آزموینه وغواړئ.',
            'request' => 'آزموینه وغواړئ',
            'footer' => 'د رستورانت داخلي عملیاتو لپاره — نه د عامه خوراک رسولو لپاره.',
        ],
    ];

    $t = $copy[$language];
@endphp
<html lang="{{ $language }}" dir="{{ $direction }}">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="theme-color" content="#020617">
    <title>BusinessOS Restaurant</title>
    <meta name="description" content="Restaurant operations for waiter ordering, kitchen/KOT, cashier, inventory, daily closing and offline synchronization.">
    @if (file_exists(public_path('build/manifest.json')) || file_exists(public_path('hot')))
        @vite(['resources/css/app.css', 'resources/js/app.js'])
    @endif
</head>
<body class="min-h-screen bg-slate-950 text-slate-100 antialiased">
    <div class="relative overflow-hidden">
        <div class="pointer-events-none absolute inset-x-0 top-0 h-[38rem] bg-[radial-gradient(circle_at_20%_10%,rgba(52,211,153,0.14),transparent_35%),radial-gradient(circle_at_80%_20%,rgba(56,189,248,0.10),transparent_28%)]"></div>

        <header class="relative z-20 border-b border-white/5 bg-slate-950/85 backdrop-blur">
            <div class="mx-auto flex max-w-7xl items-center justify-between gap-3 px-5 py-4 sm:px-6 lg:px-8">
                <a href="/?lang={{ $language }}" class="flex min-w-0 items-center gap-3">
                    <span class="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-emerald-400 text-lg font-black text-slate-950">B</span>
                    <span>
                        <span class="block text-xs font-black uppercase tracking-[0.18em] text-emerald-300">BusinessOS</span>
                        <span class="block font-black text-white">Restaurant</span>
                    </span>
                </a>

                <nav class="hidden items-center gap-7 text-sm font-semibold text-slate-300 lg:flex">
                    <a href="#features" class="hover:text-white">{{ $t['features'] }}</a>
                    <a href="#connectivity" class="hover:text-white">{{ $t['connectivity'] }}</a>
                    <a href="#trial" class="hover:text-white">{{ $t['trial'] }}</a>
                </nav>

                <div class="flex items-center gap-2">
                    <div class="hidden rounded-xl border border-slate-800 bg-slate-900/70 p-1 sm:flex" dir="ltr">
                        @foreach (['en' => 'EN', 'fa' => 'دری', 'ps' => 'پښتو'] as $code => $label)
                            <a href="/?lang={{ $code }}" class="rounded-lg px-2.5 py-1.5 text-xs font-black {{ $language === $code ? 'bg-slate-700 text-white' : 'text-slate-400 hover:text-white' }}">{{ $label }}</a>
                        @endforeach
                    </div>
                    <a href="/platform/login" class="hidden rounded-xl px-3 py-2 text-sm font-bold text-slate-200 hover:bg-white/5 md:inline-flex">{{ $t['login'] }}</a>
                    <a href="/start-trial" class="rounded-xl bg-emerald-400 px-3.5 py-2 text-sm font-black text-slate-950 hover:bg-emerald-300">{{ $t['trial'] }}</a>
                </div>
            </div>
        </header>

        <main class="relative">
            <section class="mx-auto grid max-w-7xl gap-12 px-5 pb-20 pt-14 sm:px-6 sm:pt-20 lg:grid-cols-[1.08fr_0.92fr] lg:items-center lg:px-8 lg:pb-28 lg:pt-24">
                <div>
                    <div class="inline-flex items-center gap-2 rounded-full border border-emerald-400/20 bg-emerald-400/10 px-3 py-1.5 text-xs font-black text-emerald-200">
                        <span class="h-2 w-2 rounded-full bg-emerald-400"></span>
                        {{ $t['eyebrow'] }}
                    </div>
                    <h1 class="mt-6 max-w-4xl text-4xl font-black leading-[1.04] tracking-[-0.04em] text-white sm:text-5xl lg:text-6xl">{{ $t['headline'] }}</h1>
                    <p class="mt-6 max-w-2xl text-base leading-7 text-slate-300 sm:text-lg sm:leading-8">{{ $t['intro'] }}</p>

                    <div class="mt-8 flex flex-col gap-3 sm:flex-row">
                        <a href="/start-trial" class="inline-flex items-center justify-center rounded-xl bg-emerald-400 px-5 py-3.5 text-sm font-black text-slate-950 shadow-xl shadow-emerald-500/10 hover:bg-emerald-300">{{ $t['start'] }}</a>
                        <a href="#features" class="inline-flex items-center justify-center rounded-xl border border-slate-700 bg-slate-900/70 px-5 py-3.5 text-sm font-bold text-white hover:bg-slate-900">{{ $t['explore'] }}</a>
                    </div>

                    <p class="mt-4 max-w-xl text-xs leading-5 text-slate-500">{{ $t['trial_note'] }}</p>

                    <div class="mt-8 flex flex-wrap gap-2 text-xs font-bold text-slate-300">
                        <span class="rounded-full border border-slate-800 bg-slate-900/60 px-3 py-2">AFN</span>
                        <span class="rounded-full border border-slate-800 bg-slate-900/60 px-3 py-2">English · دری · پښتو</span>
                        <span class="rounded-full border border-slate-800 bg-slate-900/60 px-3 py-2">Offline-first</span>
                        <span class="rounded-full border border-slate-800 bg-slate-900/60 px-3 py-2">Low-bandwidth optimized</span>
                    </div>
                </div>

                <div class="relative mx-auto w-full max-w-xl">
                    <div class="absolute -inset-4 rounded-[2rem] bg-emerald-400/5 blur-2xl"></div>
                    <div class="relative overflow-hidden rounded-[1.75rem] border border-slate-800 bg-slate-900/95 shadow-2xl shadow-black/30">
                        <div class="flex items-center justify-between border-b border-slate-800 px-5 py-4">
                            <div>
                                <p class="text-xs font-black uppercase tracking-[0.18em] text-emerald-300">{{ $t['flow'] }}</p>
                                <p class="mt-1 text-sm font-semibold text-white">{{ $t['table'] }}</p>
                            </div>
                            <span class="rounded-full bg-emerald-400/10 px-3 py-1 text-xs font-black text-emerald-300">{{ $t['offline'] }}</span>
                        </div>
                        <div class="grid gap-4 p-5 sm:grid-cols-[1fr_11rem]">
                            <div class="space-y-3">
                                @foreach ([['Chicken Karahi', '1 ×', '540 AFN'], ['Kabuli Pulao', '2 ×', '700 AFN'], ['Green Tea', '3 ×', '180 AFN']] as $item)
                                    <div class="flex items-center justify-between gap-4 rounded-2xl border border-slate-800 bg-slate-950/70 px-4 py-3">
                                        <div><p class="text-sm font-black text-white">{{ $item[0] }}</p><p class="mt-1 text-xs text-slate-500">{{ $item[1] }}</p></div>
                                        <span class="text-sm font-semibold text-slate-300">{{ $item[2] }}</span>
                                    </div>
                                @endforeach
                                <div class="flex items-center justify-between rounded-2xl bg-emerald-400 px-4 py-3 text-slate-950">
                                    <span class="text-sm font-black">{{ $t['kitchen'] }}</span><span class="text-lg">✓</span>
                                </div>
                            </div>
                            <div class="grid grid-cols-2 gap-2 sm:grid-cols-1">
                                @foreach ([['01', 'Waiter'], ['02', 'KOT'], ['03', 'Cashier'], ['04', 'Closing']] as $step)
                                    <div class="rounded-2xl border border-slate-800 bg-slate-950/60 p-3">
                                        <p class="text-[10px] font-black tracking-[0.2em] text-emerald-300">{{ $step[0] }}</p>
                                        <p class="mt-2 text-sm font-black text-white">{{ $step[1] }}</p>
                                    </div>
                                @endforeach
                            </div>
                        </div>
                    </div>
                </div>
            </section>

            <section id="features" class="border-y border-white/5 bg-slate-900/30">
                <div class="mx-auto max-w-7xl px-5 py-20 sm:px-6 lg:px-8 lg:py-24">
                    <p class="text-sm font-black uppercase tracking-[0.18em] text-emerald-300">{{ $t['features'] }}</p>
                    <h2 class="mt-3 text-3xl font-black tracking-tight text-white sm:text-4xl">{{ $t['feature_title'] }}</h2>
                    <p class="mt-4 text-base text-slate-400">{{ $t['feature_text'] }}</p>

                    <div class="mt-10 grid gap-4 md:grid-cols-2 lg:grid-cols-3">
                        @foreach ([
                            ['Waiter ordering', 'Fast table orders, item notes, modifiers and mobile-first order status.'],
                            ['Kitchen & KOT', 'Route items to stations and move tickets from new to preparing to ready.'],
                            ['Cashier & payments', 'Bills, discounts, payments, cashier sessions and daily closing.'],
                            ['Inventory & recipes', 'Ingredients, suppliers, purchasing, recipe usage and stock movements.'],
                            ['Management & accounting', 'Expenses, journals, ledgers and operational reports in one workspace.'],
                            ['Multi-tenant SaaS', 'Restaurant isolation with BusinessOS subscription, licensing and platform controls.'],
                        ] as $feature)
                            <article class="rounded-2xl border border-slate-800 bg-slate-950/70 p-6">
                                <h3 class="text-lg font-black text-white">{{ $feature[0] }}</h3>
                                <p class="mt-3 text-sm leading-6 text-slate-400">{{ $feature[1] }}</p>
                            </article>
                        @endforeach
                    </div>
                </div>
            </section>

            <section id="connectivity" class="mx-auto max-w-7xl px-5 py-20 sm:px-6 lg:px-8 lg:py-24">
                <div class="grid gap-10 lg:grid-cols-[0.8fr_1.2fr]">
                    <div>
                        <p class="text-sm font-black uppercase tracking-[0.18em] text-emerald-300">{{ $t['connectivity'] }}</p>
                        <h2 class="mt-3 text-3xl font-black tracking-tight text-white sm:text-4xl">{{ $t['connect_title'] }}</h2>
                        <p class="mt-4 text-base leading-7 text-slate-400">{{ $t['connect_text'] }}</p>
                        <div class="mt-6 rounded-2xl border border-emerald-400/20 bg-emerald-400/5 p-4 text-sm leading-6 text-emerald-100">
                            The waiter app keeps an offline outbox and synchronizes safely when connectivity returns.
                        </div>
                    </div>

                    <div class="grid gap-4">
                        @foreach ([
                            ['01', 'Cloud', 'Secure restaurant tenant endpoint over HTTPS.', 'HTTPS'],
                            ['02', 'Local LAN', 'Phones connect to the restaurant Apache/Laravel server on the same Wi‑Fi or LAN.', 'LAN / Wi‑Fi'],
                            ['03', 'Automatic', 'Prefer the local restaurant server when reachable and use cloud when needed.', 'Local-first'],
                        ] as $mode)
                            <div class="grid gap-4 rounded-2xl border border-slate-800 bg-slate-900/50 p-5 sm:grid-cols-[3rem_1fr_auto] sm:items-center">
                                <span class="inline-flex h-11 w-11 items-center justify-center rounded-xl bg-slate-800 text-xs font-black text-emerald-300">{{ $mode[0] }}</span>
                                <div><h3 class="font-black text-white">{{ $mode[1] }}</h3><p class="mt-1 text-sm leading-6 text-slate-400">{{ $mode[2] }}</p></div>
                                <span class="w-fit rounded-full border border-slate-700 px-3 py-1 text-xs font-bold text-slate-300">{{ $mode[3] }}</span>
                            </div>
                        @endforeach
                    </div>
                </div>
            </section>

            <section id="trial" class="border-t border-white/5 bg-slate-900/40">
                <div class="mx-auto max-w-7xl px-5 py-20 sm:px-6 lg:px-8">
                    <div class="rounded-[2rem] border border-emerald-400/20 bg-slate-900 p-7 sm:p-10 lg:flex lg:items-center lg:justify-between lg:gap-10">
                        <div class="max-w-2xl">
                            <h2 class="text-3xl font-black tracking-tight text-white sm:text-4xl">{{ $t['cta_title'] }}</h2>
                            <p class="mt-4 text-base leading-7 text-slate-300">{{ $t['cta_text'] }}</p>
                        </div>
                        <div class="mt-7 flex shrink-0 flex-col gap-3 sm:flex-row lg:mt-0">
                            <a href="/start-trial" class="inline-flex justify-center rounded-xl bg-emerald-400 px-5 py-3.5 text-sm font-black text-slate-950 hover:bg-emerald-300">{{ $t['request'] }}</a>
                            <a href="/platform/login" class="inline-flex justify-center rounded-xl border border-slate-700 bg-slate-950/70 px-5 py-3.5 text-sm font-bold text-white hover:bg-slate-950">{{ $t['login'] }}</a>
                        </div>
                    </div>
                </div>
            </section>
        </main>

        <footer class="border-t border-slate-900">
            <div class="mx-auto flex max-w-7xl flex-col gap-3 px-5 py-8 text-xs text-slate-500 sm:px-6 md:flex-row md:items-center md:justify-between lg:px-8">
                <p>BusinessOS Restaurant · {{ $t['footer'] }}</p>
                <p>© {{ now()->year }} BusinessOS.af</p>
            </div>
        </footer>
    </div>
</body>
</html>
