@extends('tenant.layouts.app')

@section('title', 'Purchasing')
@section('heading', 'Purchasing')
@section('subheading', 'Suppliers and purchase orders for restaurant stock.')

@section('content')
    <div class="grid gap-6 lg:grid-cols-[1fr_22rem]">
        <section class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
            <div class="border-b border-slate-200 px-5 py-4"><h2 class="font-black">Purchase orders</h2></div>
            <div class="overflow-x-auto">
                <table class="min-w-full text-left text-sm">
                    <thead class="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                        <tr><th class="px-5 py-3">PO</th><th class="px-5 py-3">Supplier</th><th class="px-5 py-3">Branch</th><th class="px-5 py-3">Status</th><th class="px-5 py-3">Lines</th><th class="px-5 py-3">Estimated</th></tr>
                    </thead>
                    <tbody class="divide-y divide-slate-100">
                        @forelse ($orders as $order)
                            <tr>
                                <td class="px-5 py-4"><div class="font-bold">{{ $order->po_number }}</div><div class="text-xs text-slate-400">{{ $order->ordered_at?->format('M d, Y') }}</div></td>
                                <td class="px-5 py-4">{{ $order->supplier?->name ?? '—' }}</td>
                                <td class="px-5 py-4">{{ $order->branch?->name ?? '—' }}</td>
                                <td class="px-5 py-4 capitalize">{{ str_replace('_', ' ', $order->status) }}</td>
                                <td class="px-5 py-4">{{ $order->lines_count }}</td>
                                <td class="px-5 py-4 font-bold">{{ number_format((float) $order->estimated_total, 0) }} AFN</td>
                            </tr>
                        @empty
                            <tr><td colspan="6" class="px-5 py-10 text-center text-slate-500">No purchase orders yet.</td></tr>
                        @endforelse
                    </tbody>
                </table>
            </div>
        </section>

        <aside class="space-y-5">
            <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                <h2 class="font-black">Suppliers</h2>
                <div class="mt-4 space-y-2">
                    @forelse ($suppliers as $supplier)
                        <div class="rounded-xl border border-slate-200 p-3">
                            <p class="font-bold">{{ $supplier->name }}</p>
                            <p class="mt-1 text-xs text-slate-500">{{ $supplier->phone ?: $supplier->email ?: $supplier->code }}</p>
                        </div>
                    @empty
                        <p class="text-sm text-slate-500">No suppliers yet.</p>
                    @endforelse
                </div>
            </section>

            @if (in_array($currentUser->role, ['owner','admin','manager','inventory'], true))
                <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                    <h2 class="font-black">Add supplier</h2>
                    <form method="POST" action="/setup/supplier" class="mt-4 space-y-3">
                        @csrf
                        <input name="code" required placeholder="Supplier code" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                        <input name="name" required placeholder="Supplier name" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                        <input name="phone" placeholder="Phone" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                        <input name="email" type="email" placeholder="Email" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                        <button class="w-full rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-bold text-white">Add supplier</button>
                    </form>
                </section>
            @endif
        </aside>
    </div>
@endsection
