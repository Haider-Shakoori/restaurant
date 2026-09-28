@extends('tenant.layouts.app')

@section('title', 'Dashboard')
@section('heading', 'Dashboard')
@section('subheading', 'Live restaurant operations, today’s sales and team activity.')

@section('content')
    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        @foreach ($cards as $card)
            <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                <p class="text-xs font-bold uppercase tracking-[0.12em] text-slate-400">{{ $card['label'] }}</p>
                <p class="mt-3 text-3xl font-black tracking-tight text-slate-950">{{ $card['value'] }}</p>
            </div>
        @endforeach
    </div>

    <div class="mt-6 grid gap-6 xl:grid-cols-[1.25fr_0.75fr]">
        <section class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
            <div class="flex items-center justify-between border-b border-slate-200 px-5 py-4">
                <div>
                    <h2 class="font-black text-slate-950">Recent orders</h2>
                    <p class="text-sm text-slate-500">Latest restaurant activity across tables.</p>
                </div>
                <a href="/orders" class="text-sm font-bold text-emerald-700">View all</a>
            </div>
            <div class="overflow-x-auto">
                <table class="min-w-full text-left text-sm">
                    <thead class="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                        <tr><th class="px-5 py-3">Table</th><th class="px-5 py-3">Waiter</th><th class="px-5 py-3">Items</th><th class="px-5 py-3">Status</th><th class="px-5 py-3">Total</th></tr>
                    </thead>
                    <tbody class="divide-y divide-slate-100">
                        @forelse ($recentOrders as $order)
                            <tr>
                                <td class="px-5 py-4 font-semibold">{{ $order->table?->name ?? '—' }}</td>
                                <td class="px-5 py-4">{{ $order->waiter?->name ?? '—' }}</td>
                                <td class="px-5 py-4">{{ $order->items_count }}</td>
                                <td class="px-5 py-4"><span class="rounded-full bg-slate-100 px-2.5 py-1 text-xs font-bold capitalize">{{ str_replace('_', ' ', $order->status) }}</span></td>
                                <td class="px-5 py-4 font-semibold">{{ number_format((float) $order->total, 0) }} AFN</td>
                            </tr>
                        @empty
                            <tr><td colspan="5" class="px-5 py-10 text-center text-slate-500">No orders yet.</td></tr>
                        @endforelse
                    </tbody>
                </table>
            </div>
        </section>

        <div class="space-y-6">
            <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                <h2 class="font-black text-slate-950">Open cashier sessions</h2>
                <div class="mt-4 space-y-3">
                    @forelse ($openCashierSessions as $session)
                        <div class="rounded-xl border border-slate-200 p-4">
                            <p class="font-bold">{{ $session->cashier?->name ?? 'Cashier' }}</p>
                            <p class="mt-1 text-xs text-slate-500">{{ $session->branch?->name ?? 'Branch' }} · Opened {{ $session->opened_at?->diffForHumans() }}</p>
                        </div>
                    @empty
                        <p class="text-sm text-slate-500">No open cashier sessions.</p>
                    @endforelse
                </div>
            </section>

            <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                <h2 class="font-black text-slate-950">Quick links</h2>
                <div class="mt-4 grid gap-2">
                    <a href="/tables" class="rounded-xl border border-slate-200 px-4 py-3 text-sm font-bold hover:bg-slate-50">Manage tables</a>
                    <a href="/menu" class="rounded-xl border border-slate-200 px-4 py-3 text-sm font-bold hover:bg-slate-50">Manage menu</a>
                    <a href="/daily-closing" class="rounded-xl border border-slate-200 px-4 py-3 text-sm font-bold hover:bg-slate-50">Daily closing</a>
                </div>
            </section>
        </div>
    </div>
@endsection
