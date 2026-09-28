@extends('tenant.layouts.app')

@section('title', 'Inventory')
@section('heading', 'Inventory')
@section('subheading', 'Ingredients, stock balances and reorder levels.')

@section('content')
    <div class="grid gap-6 lg:grid-cols-[1fr_22rem]">
        <section class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
            <div class="overflow-x-auto">
                <table class="min-w-full text-left text-sm">
                    <thead class="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                        <tr><th class="px-5 py-3">Item</th><th class="px-5 py-3">Unit</th><th class="px-5 py-3">Reorder level</th><th class="px-5 py-3">Balances</th></tr>
                    </thead>
                    <tbody class="divide-y divide-slate-100">
                        @forelse ($items as $item)
                            <tr class="align-top">
                                <td class="px-5 py-4"><div class="font-bold">{{ $item->name }}</div><div class="text-xs text-slate-400">{{ $item->sku }}</div></td>
                                <td class="px-5 py-4">{{ $item->base_unit }}</td>
                                <td class="px-5 py-4">{{ $item->reorder_level }}</td>
                                <td class="px-5 py-4">
                                    <div class="flex flex-wrap gap-2">
                                        @forelse ($item->balances as $balance)
                                            <span class="rounded-full bg-slate-100 px-2.5 py-1 text-xs font-bold">{{ $balance->quantity }} {{ $item->base_unit }}</span>
                                        @empty
                                            <span class="text-xs text-slate-400">No stock yet</span>
                                        @endforelse
                                    </div>
                                </td>
                            </tr>
                        @empty
                            <tr><td colspan="4" class="px-5 py-10 text-center text-slate-500">No inventory items yet.</td></tr>
                        @endforelse
                    </tbody>
                </table>
            </div>
        </section>

        @if (in_array($currentUser->role, ['owner','admin','manager','inventory'], true))
            <aside>
                <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                    <h2 class="font-black">Add inventory item</h2>
                    <form method="POST" action="/setup/inventory/item" class="mt-4 space-y-3">
                        @csrf
                        <input name="sku" required placeholder="SKU" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                        <input name="name" required placeholder="Item name" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                        <input name="base_unit" required placeholder="Unit e.g. kg, pcs, l" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                        <input name="reorder_level" type="number" min="0" step="0.0001" placeholder="Reorder level" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                        <button class="w-full rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-bold text-white">Add item</button>
                    </form>
                </section>
            </aside>
        @endif
    </div>
@endsection
