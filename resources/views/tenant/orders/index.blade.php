@extends('tenant.layouts.app')

@section('title', 'Orders')
@section('heading', 'Orders')
@section('subheading', 'Monitor active and completed table orders.')

@section('content')
    <form method="GET" action="/orders" class="mb-5 flex flex-col gap-3 sm:flex-row sm:items-center">
        <select name="status" class="rounded-xl border border-slate-300 bg-white px-4 py-2.5 text-sm">
            <option value="">All statuses</option>
            @foreach ($statuses as $status)
                <option value="{{ $status }}" @selected(request('status') === $status)>{{ ucfirst(str_replace('_', ' ', $status)) }}</option>
            @endforeach
        </select>
        <button class="rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-bold text-white">Filter</button>
        <a href="/orders" class="rounded-xl border border-slate-300 bg-white px-4 py-2.5 text-center text-sm font-bold">Reset</a>
    </form>

    <div class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
        <div class="overflow-x-auto">
            <table class="min-w-full text-left text-sm">
                <thead class="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                    <tr>
                        <th class="px-5 py-3">Opened</th>
                        <th class="px-5 py-3">Table</th>
                        <th class="px-5 py-3">Waiter</th>
                        <th class="px-5 py-3">Items</th>
                        <th class="px-5 py-3">Status</th>
                        <th class="px-5 py-3">Total</th>
                        <th class="px-5 py-3">Bill</th>
                    </tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                    @forelse ($orders as $order)
                        <tr class="align-top">
                            <td class="px-5 py-4 whitespace-nowrap">{{ $order->opened_at?->format('M d, H:i') ?? $order->created_at?->format('M d, H:i') }}</td>
                            <td class="px-5 py-4 font-bold">{{ $order->table?->name ?? '—' }}</td>
                            <td class="px-5 py-4">{{ $order->waiter?->name ?? '—' }}</td>
                            <td class="px-5 py-4">{{ $order->items->sum('quantity') }}</td>
                            <td class="px-5 py-4"><span class="rounded-full bg-slate-100 px-2.5 py-1 text-xs font-bold capitalize">{{ str_replace('_', ' ', $order->status) }}</span></td>
                            <td class="px-5 py-4 font-bold">{{ number_format((float) $order->total, 0) }} AFN</td>
                            <td class="px-5 py-4">{{ $order->bill?->bill_number ?? '—' }}</td>
                        </tr>
                    @empty
                        <tr><td colspan="7" class="px-5 py-10 text-center text-slate-500">No orders match this filter.</td></tr>
                    @endforelse
                </tbody>
            </table>
        </div>
    </div>
@endsection
