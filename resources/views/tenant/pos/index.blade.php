@extends('tenant.layouts.app')

@section('title', 'POS & Cashier')
@section('heading', 'POS & Cashier')
@section('pos_fullscreen', 'true')
@section('subheading', 'Serve ready food, issue bills, record payments and release tables securely.')

@section('content')
    @php
        $openSessions = $sessions->where('status', 'open');
        $openBills = $bills->where('status', 'open');
    @endphp
    @if ($errors->any())
        <div role="alert" class="mb-5 rounded-xl border border-rose-300 bg-rose-50 p-4 text-sm text-rose-900">
            <strong>Please review the order or payment:</strong>
            <ul class="mt-2 list-inside list-disc">@foreach ($errors->all() as $error)<li>{{ $error }}</li>@endforeach</ul>
        </div>
    @endif
    @include('tenant.pos.partials.photo-order')
    <section id="pos-cashier" class="px-4 pb-4 pt-6 sm:px-6">
        <div class="mb-4 flex flex-wrap items-center justify-between gap-3">
            <div>
                <h2 class="text-xl font-black text-slate-950">Payments, service & cashier</h2>
                <p class="text-sm text-slate-500">Use the same authoritative billing and payment flows; partial payment never frees an occupied table.</p>
            </div>
            <a href="#pos-order-entry" class="rounded-xl border border-violet-300 bg-white px-4 py-2 text-sm font-bold text-violet-700">↑ Back to POS</a>
        </div>
    <div class="mb-6 grid gap-4 sm:grid-cols-3">
        <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <p class="text-xs font-black uppercase tracking-widest text-slate-500">Ready to serve</p>
            <p class="mt-2 text-3xl font-black text-violet-700">{{ $readyOrders->where('status', 'ready')->count() }}</p>
            <p class="mt-1 text-xs text-slate-500">Kitchen completed orders awaiting service</p>
        </div>
        <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <p class="text-xs font-black uppercase tracking-widest text-slate-500">Outstanding bills</p>
            <p class="mt-2 text-3xl font-black text-amber-700">{{ $openBills->count() }}</p>
            <p class="mt-1 text-xs text-slate-500">Orders remain open until fully paid</p>
        </div>
        <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <p class="text-xs font-black uppercase tracking-widest text-slate-500">Outstanding AFN</p>
            <p class="mt-2 text-3xl font-black text-slate-950">{{ number_format($openBills->sum('balance_due'), 0) }}</p>
            <p class="mt-1 text-xs text-slate-500">Based on currently open bills</p>
        </div>
    </div>

    <div class="grid gap-6 xl:grid-cols-[minmax(0,1fr)_minmax(350px,0.85fr)]">
        <div class="space-y-6">
            <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                <div class="flex items-center justify-between gap-3">
                    <div><h2 class="font-black text-lg">Restaurant service queue</h2><p class="mt-1 text-xs text-slate-500">Serve only when the kitchen says Ready. Create a bill after service.</p></div>
                    <a href="/orders" class="rounded-xl bg-violet-100 px-4 py-2 text-xs font-black text-violet-800">View all orders</a>
                </div>
                <div class="mt-4 space-y-3">
                    @forelse ($readyOrders as $order)
                        <div class="rounded-xl border border-slate-200 bg-slate-50 p-4">
                            <div class="flex flex-wrap items-center justify-between gap-2">
                                <span class="font-black">#{{ substr($order->id, -8) }} · {{ $order->table?->name ?? ucfirst(str_replace('_', ' ', $order->service_type)) }}</span>
                                <span class="rounded-full px-3 py-1 text-xs font-black {{ $order->status === 'ready' ? 'bg-green-100 text-green-800' : 'bg-violet-100 text-violet-800' }}">{{ strtoupper($order->status) }}</span>
                            </div>
                            <p class="mt-2 text-xs text-slate-500">Waiter: {{ $order->waiter?->name ?? 'Unassigned' }} · {{ number_format((float) $order->total, 2) }} AFN</p>
                            <div class="mt-3 flex flex-wrap gap-2">
                                @if ($order->status === 'ready')
                                    <form method="POST" action="/orders/{{ $order->id }}/serve">@csrf
                                        <button class="rounded-lg bg-emerald-600 px-4 py-2 text-xs font-black text-white">Mark served</button>
                                    </form>
                                @elseif ($order->status === 'served')
                                    <form method="POST" action="/orders/{{ $order->id }}/bill">@csrf
                                        <button class="rounded-lg bg-violet-600 px-4 py-2 text-xs font-black text-white">Issue bill</button>
                                    </form>
                                @endif
                            </div>
                        </div>
                    @empty
                        <p class="rounded-xl bg-slate-50 px-4 py-6 text-sm text-slate-500">No ready or served orders. Orders appear when kitchen production finishes.</p>
                    @endforelse
                </div>
            </section>
        </div>

        <div class="space-y-6">
            <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                <h2 class="text-lg font-black">Cashier session</h2>
                <p class="mt-1 text-sm text-slate-500">Open a session before accepting any payment.</p>
                @if ($openSessions->isEmpty())
                    <form method="POST" action="/cashier/sessions/open" class="mt-4 grid gap-3">
                        @csrf
                        <label class="text-xs font-bold">Branch
                            <select name="branch_id" required class="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2.5">
                                @foreach ($branches as $branch)<option value="{{ $branch->id }}">{{ $branch->name }}</option>@endforeach
                            </select>
                        </label>
                        <label class="text-xs font-bold">Opening cash (AFN)
                            <input name="opening_cash" type="number" step="0.01" min="0" value="0" required class="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2.5">
                        </label>
                        <button class="rounded-xl bg-slate-950 px-4 py-3 font-black text-white">Open cashier session</button>
                    </form>
                @else
                    <div class="mt-3 space-y-2">
                        @foreach ($openSessions as $session)
                            <div class="rounded-lg border border-emerald-200 bg-emerald-50 p-3 text-sm">
                                <strong>{{ $session->branch?->name }}</strong> · {{ $session->cashier?->name }} <span class="text-emerald-700">OPEN</span>
                            </div>
                        @endforeach
                    </div>
                @endif
            </section>

            <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                <h2 class="text-lg font-black">Unpaid and partially paid bills</h2>
                <p class="mt-1 text-sm text-slate-500">Only the final confirmed payment closes the order and releases its table.</p>
                <div class="mt-4 space-y-4">
                    @forelse ($openBills as $bill)
                        <div class="rounded-xl border border-slate-200 p-4">
                            <div class="flex items-center justify-between gap-2">
                                <span class="font-black">{{ $bill->bill_number }}</span>
                                <span class="text-xs text-slate-500">{{ $bill->order?->table?->name ?? 'Counter / Takeaway' }}</span>
                            </div>
                            <dl class="mt-3 grid grid-cols-3 gap-2 text-xs">
                                <div><dt class="text-slate-500">Total</dt><dd class="font-black">{{ number_format((float) $bill->total, 2) }}</dd></div>
                                <div><dt class="text-slate-500">Paid</dt><dd class="font-black text-emerald-700">{{ number_format((float) $bill->paid_amount, 2) }}</dd></div>
                                <div><dt class="text-slate-500">Balance</dt><dd class="font-black text-rose-700">{{ number_format((float) $bill->balance_due, 2) }}</dd></div>
                            </dl>
                            @if ($openSessions->where('branch_id', $bill->branch_id)->isNotEmpty())
                                <form method="POST" action="/bills/{{ $bill->id }}/payments" class="mt-4 grid gap-2">
                                    @csrf
                                    <input type="hidden" name="client_payment_id" value="{{ (string) \Illuminate\Support\Str::uuid() }}">
                                    <select name="cashier_session_id" required class="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                                        @foreach ($openSessions->where('branch_id', $bill->branch_id) as $session)
                                            <option value="{{ $session->id }}">{{ $session->cashier?->name }} · {{ $session->branch?->name }}</option>
                                        @endforeach
                                    </select>
                                    <div class="grid grid-cols-2 gap-2">
                                        <select name="method" required class="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                                            @foreach (['cash'=>'Cash','card'=>'Card','bank'=>'Bank','mobile_money'=>'Mobile money','other'=>'Other'] as $value => $label)
                                                <option value="{{ $value }}">{{ $label }}</option>
                                            @endforeach
                                        </select>
                                        <input name="amount" type="number" min="0.01" max="{{ $bill->balance_due }}" step="0.01" value="{{ $bill->balance_due }}" required class="rounded-lg border border-slate-300 px-3 py-2 text-sm" aria-label="Payment amount AFN">
                                    </div>
                                    <input name="reference" maxlength="255" placeholder="Reference (optional)" class="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                                    <button class="rounded-xl bg-emerald-600 px-4 py-2.5 text-sm font-black text-white">Post payment</button>
                                </form>
                            @else
                                <p class="mt-3 text-xs text-amber-700">Open a cashier session for this bill's branch to accept payment.</p>
                            @endif
                        </div>
                    @empty
                        <p class="rounded-xl bg-slate-50 px-4 py-6 text-sm text-slate-500">All visible bills are settled.</p>
                    @endforelse
                </div>
            </section>
        </div>
    </div>
    </section>
@endsection
