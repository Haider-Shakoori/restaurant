@extends('tenant.layouts.app')

@section('title', 'Purchasing')
@section('heading', 'Purchasing')
@section('subheading', 'Create purchase orders, manage suppliers and receive restaurant stock.')

@section('content')
    @php
        $canCreatePurchaseOrder = $branches->isNotEmpty() && $suppliers->isNotEmpty() && $inventoryItems->isNotEmpty();
        $openCreateForm = $errors->has('branch_id')
            || $errors->has('supplier_id')
            || $errors->has('lines')
            || collect($errors->keys())->contains(fn ($key) => str_starts_with($key, 'lines.'));
        $initialLines = old('lines', [[
            'inventory_item_id' => '',
            'purchase_quantity' => '1',
            'unit_cost' => '0',
        ]]);
        $itemOptions = $inventoryItems->map(fn ($item) => [
            'id' => $item->id,
            'label' => $item->name.' · '.($item->purchase_unit ?: $item->base_unit),
            'unit' => $item->purchase_unit ?: $item->base_unit,
        ])->values();
    @endphp

    <div
        x-data="{
            showCreate: @js($openCreateForm),
            lines: @js($initialLines),
            items: @js($itemOptions),
            addLine() {
                this.lines.push({ inventory_item_id: '', purchase_quantity: '1', unit_cost: '0' });
            },
            removeLine(index) {
                if (this.lines.length > 1) this.lines.splice(index, 1);
            },
            unitFor(id) {
                return this.items.find(item => item.id === id)?.unit || 'unit';
            },
            lineTotal(line) {
                return (Number(line.purchase_quantity || 0) * Number(line.unit_cost || 0));
            },
            grandTotal() {
                return this.lines.reduce((total, line) => total + this.lineTotal(line), 0);
            },
            money(value) {
                return Number(value || 0).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            }
        }"
        class="space-y-6"
    >
        <section class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
            <div class="flex flex-col gap-4 border-b border-slate-200 px-5 py-5 sm:flex-row sm:items-center sm:justify-between">
                <div>
                    <p class="text-xs font-black uppercase tracking-[0.16em] text-amber-700">Procurement</p>
                    <h2 class="mt-1 text-xl font-black text-slate-950">Purchase Orders</h2>
                    <p class="mt-1 text-sm text-slate-500">Order ingredients, packaging and other stock from approved suppliers.</p>
                </div>

                <button
                    type="button"
                    @click="showCreate = !showCreate"
                    @disabled(! $canCreatePurchaseOrder)
                    class="inline-flex items-center justify-center rounded-xl bg-slate-950 px-5 py-3 text-sm font-black text-white shadow-sm hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-40"
                >
                    <span class="mr-2 text-lg leading-none">+</span>
                    <span x-text="showCreate ? 'Close PO Form' : 'New Purchase Order'"></span>
                </button>
            </div>

            @unless ($canCreatePurchaseOrder)
                <div class="border-b border-amber-200 bg-amber-50 px-5 py-4 text-sm text-amber-900">
                    <span class="font-bold">Before creating a PO:</span>
                    @if ($branches->isEmpty()) add an active branch.@endif
                    @if ($suppliers->isEmpty()) {{ $branches->isEmpty() ? ' Then add' : ' Add' }} a supplier.@endif
                    @if ($inventoryItems->isEmpty()) {{ $branches->isEmpty() || $suppliers->isEmpty() ? ' Then add' : ' Add' }} inventory items.@endif
                </div>
            @endunless

            <div x-show="showCreate" x-cloak class="border-b border-slate-200 bg-slate-50/70 p-5 sm:p-6">
                <form method="POST" action="/purchasing/orders" class="space-y-5">
                    @csrf

                    <div class="grid gap-4 lg:grid-cols-2">
                        <label>
                            <span class="text-sm font-bold text-slate-700">Branch</span>
                            <select name="branch_id" required class="mt-2 w-full rounded-xl border border-slate-300 bg-white px-4 py-3 text-sm outline-none focus:border-amber-500 focus:ring-2 focus:ring-amber-200">
                                <option value="">Select branch</option>
                                @foreach ($branches as $branch)
                                    <option value="{{ $branch->id }}" @selected((string) old('branch_id') === (string) $branch->id)>{{ $branch->name }}</option>
                                @endforeach
                            </select>
                        </label>

                        <label>
                            <span class="text-sm font-bold text-slate-700">Supplier</span>
                            <select name="supplier_id" required class="mt-2 w-full rounded-xl border border-slate-300 bg-white px-4 py-3 text-sm outline-none focus:border-amber-500 focus:ring-2 focus:ring-amber-200">
                                <option value="">Select supplier</option>
                                @foreach ($suppliers as $supplier)
                                    <option value="{{ $supplier->id }}" @selected((string) old('supplier_id') === (string) $supplier->id)>{{ $supplier->name }}</option>
                                @endforeach
                            </select>
                        </label>
                    </div>

                    <div class="overflow-hidden rounded-2xl border border-slate-200 bg-white">
                        <div class="flex items-center justify-between gap-3 border-b border-slate-200 px-4 py-3">
                            <div>
                                <h3 class="font-black text-slate-950">PO Items</h3>
                                <p class="text-xs text-slate-500">Enter quantity in each item's configured purchase unit.</p>
                            </div>
                            <button type="button" @click="addLine()" class="rounded-lg border border-slate-300 bg-white px-3 py-2 text-xs font-bold text-slate-700 hover:bg-slate-50">
                                + Add line
                            </button>
                        </div>

                        <div class="space-y-3 p-4">
                            <template x-for="(line, index) in lines" :key="index">
                                <div class="grid gap-3 rounded-xl border border-slate-200 bg-slate-50 p-3 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)_minmax(0,1fr)_minmax(0,1fr)_auto] lg:items-end">
                                    <label>
                                        <span class="text-xs font-bold uppercase tracking-wide text-slate-500">Inventory item</span>
                                        <select
                                            x-model="line.inventory_item_id"
                                            :name="'lines[' + index + '][inventory_item_id]'"
                                            required
                                            class="mt-1.5 w-full rounded-lg border border-slate-300 bg-white px-3 py-2.5 text-sm"
                                        >
                                            <option value="">Select item</option>
                                            <template x-for="item in items" :key="item.id">
                                                <option :value="item.id" x-text="item.label"></option>
                                            </template>
                                        </select>
                                    </label>

                                    <label>
                                        <span class="text-xs font-bold uppercase tracking-wide text-slate-500">Quantity</span>
                                        <div class="mt-1.5 flex overflow-hidden rounded-lg border border-slate-300 bg-white">
                                            <input
                                                x-model="line.purchase_quantity"
                                                :name="'lines[' + index + '][purchase_quantity]'"
                                                type="number"
                                                min="0.0001"
                                                step="0.0001"
                                                required
                                                class="min-w-0 flex-1 border-0 px-3 py-2.5 text-sm outline-none"
                                            >
                                            <span class="flex items-center border-l border-slate-200 bg-slate-50 px-2 text-xs font-bold text-slate-500" x-text="unitFor(line.inventory_item_id)"></span>
                                        </div>
                                    </label>

                                    <label>
                                        <span class="text-xs font-bold uppercase tracking-wide text-slate-500">Unit cost</span>
                                        <div class="mt-1.5 flex overflow-hidden rounded-lg border border-slate-300 bg-white">
                                            <input
                                                x-model="line.unit_cost"
                                                :name="'lines[' + index + '][unit_cost]'"
                                                type="number"
                                                min="0"
                                                step="0.01"
                                                required
                                                class="min-w-0 flex-1 border-0 px-3 py-2.5 text-sm outline-none"
                                            >
                                            <span class="flex items-center border-l border-slate-200 bg-slate-50 px-2 text-xs font-bold text-slate-500">AFN</span>
                                        </div>
                                    </label>

                                    <div>
                                        <span class="text-xs font-bold uppercase tracking-wide text-slate-500">Line total</span>
                                        <div class="mt-1.5 rounded-lg bg-slate-900 px-3 py-2.5 text-sm font-black text-white">
                                            <span x-text="money(lineTotal(line))"></span> AFN
                                        </div>
                                    </div>

                                    <button
                                        type="button"
                                        @click="removeLine(index)"
                                        :disabled="lines.length === 1"
                                        class="rounded-lg border border-red-200 bg-white px-3 py-2.5 text-xs font-bold text-red-700 disabled:cursor-not-allowed disabled:opacity-30"
                                    >
                                        Remove
                                    </button>
                                </div>
                            </template>
                        </div>
                    </div>

                    <label class="block">
                        <span class="text-sm font-bold text-slate-700">Notes <span class="font-normal text-slate-400">(optional)</span></span>
                        <textarea name="notes" rows="3" maxlength="2000" placeholder="Delivery instructions, supplier reference, special notes..." class="mt-2 w-full rounded-xl border border-slate-300 bg-white px-4 py-3 text-sm outline-none focus:border-amber-500 focus:ring-2 focus:ring-amber-200">{{ old('notes') }}</textarea>
                    </label>

                    <div class="flex flex-col gap-4 rounded-xl border border-slate-200 bg-white p-4 sm:flex-row sm:items-center sm:justify-between">
                        <div>
                            <p class="text-xs font-bold uppercase tracking-wide text-slate-500">Estimated PO Total</p>
                            <p class="mt-1 text-2xl font-black text-slate-950"><span x-text="money(grandTotal())"></span> AFN</p>
                        </div>
                        <button class="rounded-xl bg-amber-500 px-6 py-3 text-sm font-black text-slate-950 shadow-sm hover:bg-amber-400">
                            Create Purchase Order
                        </button>
                    </div>
                </form>
            </div>

            <div class="overflow-x-auto">
                <table class="min-w-full text-left text-sm">
                    <thead class="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                        <tr>
                            <th class="px-5 py-3">PO</th>
                            <th class="px-5 py-3">Supplier</th>
                            <th class="px-5 py-3">Branch</th>
                            <th class="px-5 py-3">Status</th>
                            <th class="px-5 py-3 text-right">Lines</th>
                            <th class="px-5 py-3 text-right">Estimated</th>
                        </tr>
                    </thead>
                    <tbody class="divide-y divide-slate-100">
                        @forelse ($orders as $order)
                            <tr class="hover:bg-slate-50">
                                <td class="px-5 py-4">
                                    <div class="font-bold text-slate-950">{{ $order->po_number }}</div>
                                    <div class="mt-1 text-xs text-slate-400">{{ $order->ordered_at?->format('M d, Y · H:i') }}</div>
                                </td>
                                <td class="px-5 py-4">{{ $order->supplier?->name ?? '—' }}</td>
                                <td class="px-5 py-4">{{ $order->branch?->name ?? '—' }}</td>
                                <td class="px-5 py-4">
                                    <span class="rounded-full bg-slate-100 px-2.5 py-1 text-xs font-bold capitalize text-slate-700">{{ str_replace('_', ' ', $order->status) }}</span>
                                </td>
                                <td class="px-5 py-4 text-right">{{ $order->lines_count }}</td>
                                <td class="px-5 py-4 text-right font-black">{{ number_format((float) $order->estimated_total, 2) }} AFN</td>
                            </tr>
                        @empty
                            <tr><td colspan="6" class="px-5 py-12 text-center text-slate-500">No purchase orders yet. Use <span class="font-bold text-slate-700">New Purchase Order</span> to create the first one.</td></tr>
                        @endforelse
                    </tbody>
                </table>
            </div>
        </section>

        <div class="grid gap-6 lg:grid-cols-[1fr_22rem]">
            <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                <div class="flex items-center justify-between gap-3">
                    <div>
                        <h2 class="font-black">Suppliers</h2>
                        <p class="mt-1 text-sm text-slate-500">{{ $suppliers->count() }} active supplier{{ $suppliers->count() === 1 ? '' : 's' }}</p>
                    </div>
                </div>

                <div class="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                    @forelse ($suppliers as $supplier)
                        <div class="rounded-xl border border-slate-200 p-4">
                            <p class="font-bold">{{ $supplier->name }}</p>
                            <p class="mt-1 text-xs font-mono text-slate-400">{{ $supplier->code }}</p>
                            <p class="mt-2 text-sm text-slate-500">{{ $supplier->phone ?: $supplier->email ?: 'No contact details' }}</p>
                        </div>
                    @empty
                        <p class="text-sm text-slate-500">No suppliers yet.</p>
                    @endforelse
                </div>
            </section>

            <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                <h2 class="font-black">Add Supplier</h2>
                <p class="mt-1 text-sm text-slate-500">Create a supplier before raising purchase orders.</p>
                <form method="POST" action="/setup/supplier" class="mt-4 space-y-3">
                    @csrf
                    <input name="code" required placeholder="Supplier code" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                    <input name="name" required placeholder="Supplier name" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                    <input name="phone" placeholder="Phone" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                    <input name="email" type="email" placeholder="Email" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                    <button class="w-full rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-bold text-white hover:bg-slate-800">Add Supplier</button>
                </form>
            </section>
        </div>
    </div>
@endsection
