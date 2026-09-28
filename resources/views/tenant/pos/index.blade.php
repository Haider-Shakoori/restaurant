@extends('tenant.layouts.app')

@section('title', 'POS & Cashier')
@section('heading', 'POS & Cashier')
@section('subheading', 'Bills, payment status and cashier sessions.')

@section('content')
    <section class="mb-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        @foreach ($sessions->take(6) as $session)
            <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                <div class="flex items-center justify-between gap-3">
                    <h2 class="font-black">{{ $session->cashier?->name ?? 'Cashier' }}</h2>
                    <span class="rounded-full px-2.5 py-1 text-xs font-bold capitalize {{ $session->status === 'open' ? 'bg-emerald-100 text-emerald-700' : 'bg-slate-100 text-slate-600' }}">{{ $session->status }}</span>
                </div>
                <p class="mt-2 text-sm text-slate-500">{{ $session->branch?->name ?? 'Branch' }}</p>
                <p class="mt-3 text-xs text-slate-400">Opening cash: {{ number_format((float) $session->opening_cash, 0) }} AFN</p>
            </div>
        @endforeach
    </section>

    <section class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
        <div class="border-b border-slate-200 px-5 py-4">
            <h2 class="font-black">Recent bills</h2>
        </div>
        <div class="overflow-x-auto">
            <table class="min-w-full text-left text-sm">
                <thead class="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                    <tr><th class="px-5 py-3">Bill</th><th class="px-5 py-3">Table</th><th class="px-5 py-3">Status</th><th class="px-5 py-3">Total</th><th class="px-5 py-3">Paid</th><th class="px-5 py-3">Balance</th></tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                    @forelse ($bills as $bill)
                        <tr>
                            <td class="px-5 py-4"><div class="font-bold">{{ $bill->bill_number }}</div><div class="text-xs text-slate-400">{{ $bill->issued_at?->format('M d, H:i') }}</div></td>
                            <td class="px-5 py-4">{{ $bill->order?->table?->name ?? '—' }}</td>
                            <td class="px-5 py-4"><span class="rounded-full bg-slate-100 px-2.5 py-1 text-xs font-bold capitalize">{{ $bill->status }}</span></td>
                            <td class="px-5 py-4 font-bold">{{ number_format((float) $bill->total, 0) }} AFN</td>
                            <td class="px-5 py-4">{{ number_format((float) $bill->paid_amount, 0) }} AFN</td>
                            <td class="px-5 py-4">{{ number_format((float) $bill->balance_due, 0) }} AFN</td>
                        </tr>
                    @empty
                        <tr><td colspan="6" class="px-5 py-10 text-center text-slate-500">No bills yet.</td></tr>
                    @endforelse
                </tbody>
            </table>
        </div>
    </section>
@endsection
