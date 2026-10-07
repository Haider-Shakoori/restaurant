@extends('tenant.layouts.app')

@section('title', 'Orders')
@section('heading', 'Orders')
@section('subheading', 'Take new table orders and monitor active or completed orders.')

@section('content')
    @php
        $menuOptions = $menuCategories
            ->flatMap(fn ($category) => $category->items->map(fn ($item) => [
                'id' => $item->id,
                'name' => $item->name,
                'category' => $category->name,
                'price' => (float) $item->price,
            ]))
            ->values();

        $oldLines = collect(old('lines', []))
            ->map(function ($line) use ($menuOptions) {
                $item = $menuOptions->firstWhere('id', $line['menu_item_id'] ?? null);

                return [
                    'menu_item_id' => $line['menu_item_id'] ?? '',
                    'name' => $item['name'] ?? 'Menu item',
                    'price' => (float) ($item['price'] ?? 0),
                    'quantity' => (int) ($line['quantity'] ?? 1),
                    'notes' => $line['notes'] ?? '',
                ];
            })
            ->values();

        $openTakeOrder = $errors->has('dining_table_id')
            || $errors->has('guest_count')
            || $errors->has('lines')
            || collect($errors->keys())->contains(fn ($key) => str_starts_with($key, 'lines.'));
    @endphp

    <div
        x-data="{
            showTakeOrder: @js($openTakeOrder),
            items: @js($menuOptions),
            lines: @js($oldLines),
            addItem(item) {
                const existing = this.lines.find(line => line.menu_item_id === item.id);

                if (existing) {
                    existing.quantity++;
                    return;
                }

                this.lines.push({
                    menu_item_id: item.id,
                    name: item.name,
                    price: Number(item.price),
                    quantity: 1,
                    notes: ''
                });
            },
            removeLine(index) {
                this.lines.splice(index, 1);
            },
            lineTotal(line) {
                return Number(line.price || 0) * Number(line.quantity || 0);
            },
            grandTotal() {
                return this.lines.reduce((sum, line) => sum + this.lineTotal(line), 0);
            },
            money(value) {
                return Number(value || 0).toLocaleString(undefined, {
                    minimumFractionDigits: 0,
                    maximumFractionDigits: 2
                });
            }
        }"
        class="space-y-6"
    >
        <section class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
            <div class="flex flex-col gap-4 border-b border-slate-200 px-5 py-5 sm:flex-row sm:items-center sm:justify-between">
                <div>
                    <p class="text-xs font-black uppercase tracking-[0.16em] text-emerald-700">Order Entry</p>
                    <h2 class="mt-1 text-xl font-black text-slate-950">Take a Restaurant Order</h2>
                    <p class="mt-1 text-sm text-slate-500">Select a table, add menu items and send the order directly to Kitchen.</p>
                </div>

                <button
                    type="button"
                    @click="showTakeOrder = !showTakeOrder"
                    @disabled($availableTables->isEmpty() || $menuOptions->isEmpty())
                    class="inline-flex items-center justify-center rounded-xl bg-emerald-500 px-5 py-3 text-sm font-black text-slate-950 shadow-sm hover:bg-emerald-400 disabled:cursor-not-allowed disabled:opacity-40"
                >
                    <span class="mr-2 text-lg leading-none">+</span>
                    <span x-text="showTakeOrder ? 'Close Order Entry' : 'New Order / Take Order'"></span>
                </button>
            </div>

            @if ($availableTables->isEmpty() || $menuOptions->isEmpty())
                <div class="border-b border-amber-200 bg-amber-50 px-5 py-4 text-sm text-amber-900">
                    <span class="font-bold">Order entry needs setup:</span>
                    @if ($availableTables->isEmpty()) there are no available active tables.@endif
                    @if ($menuOptions->isEmpty()) {{ $availableTables->isEmpty() ? ' Also,' : '' }} there are no available menu items.@endif
                    Use <a href="/tables" class="font-black underline">Tables</a> and <a href="/menu" class="font-black underline">Menu</a> to configure them.
                </div>
            @endif

            <div x-show="showTakeOrder" x-cloak class="border-b border-slate-200 bg-slate-50/70 p-5 sm:p-6">
                <form method="POST" action="/orders/take" class="space-y-6">
                    @csrf

                    <div class="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
                        <label>
                            <span class="text-sm font-bold text-slate-700">Dining table</span>
                            <select name="dining_table_id" required class="mt-2 w-full rounded-xl border border-slate-300 bg-white px-4 py-3 text-sm outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-200">
                                <option value="">Select an available table</option>
                                @foreach ($availableTables as $table)
                                    <option value="{{ $table->id }}" @selected((string) old('dining_table_id') === (string) $table->id)>
                                        {{ $table->diningArea?->branch?->name }} · {{ $table->diningArea?->name }} · {{ $table->name }} ({{ $table->capacity }} seats)
                                    </option>
                                @endforeach
                            </select>
                        </label>

                        <label>
                            <span class="text-sm font-bold text-slate-700">Guests</span>
                            <input name="guest_count" type="number" min="1" max="100" value="{{ old('guest_count', 1) }}" required
                                   class="mt-2 w-full rounded-xl border border-slate-300 bg-white px-4 py-3 text-sm outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-200">
                        </label>
                    </div>

                    <div class="grid gap-5 xl:grid-cols-[minmax(0,1.45fr)_minmax(22rem,0.8fr)]">
                        <div class="space-y-4">
                            <div>
                                <h3 class="font-black text-slate-950">Menu</h3>
                                <p class="mt-1 text-sm text-slate-500">Tap an item to add it to this order.</p>
                            </div>

                            @foreach ($menuCategories as $category)
                                @if ($category->items->isNotEmpty())
                                    <section class="overflow-hidden rounded-2xl border border-slate-200 bg-white">
                                        <div class="border-b border-slate-200 bg-slate-50 px-4 py-3">
                                            <h4 class="font-black text-slate-800">{{ $category->name }}</h4>
                                        </div>
                                        <div class="grid gap-3 p-4 sm:grid-cols-2 lg:grid-cols-3">
                                            @foreach ($category->items as $item)
                                                <button
                                                    type="button"
                                                    @click='addItem(@js([
                                                        "id" => $item->id,
                                                        "name" => $item->name,
                                                        "price" => (float) $item->price,
                                                    ]))'
                                                    class="group rounded-xl border border-slate-200 bg-white p-4 text-left transition hover:border-emerald-300 hover:bg-emerald-50"
                                                >
                                                    <div class="flex items-start justify-between gap-3">
                                                        <div>
                                                            <p class="font-bold text-slate-950">{{ $item->name }}</p>
                                                            @if ($item->description)
                                                                <p class="mt-1 line-clamp-2 text-xs leading-5 text-slate-500">{{ $item->description }}</p>
                                                            @endif
                                                        </div>
                                                        <span class="shrink-0 rounded-lg bg-slate-950 px-2.5 py-1.5 text-xs font-black text-white">{{ number_format((float) $item->price, 0) }} AFN</span>
                                                    </div>
                                                    <p class="mt-3 text-xs font-black text-emerald-700 group-hover:text-emerald-800">+ Add to order</p>
                                                </button>
                                            @endforeach
                                        </div>
                                    </section>
                                @endif
                            @endforeach
                        </div>

                        <aside class="h-fit rounded-2xl border border-slate-200 bg-white shadow-sm xl:sticky xl:top-5">
                            <div class="border-b border-slate-200 px-5 py-4">
                                <div class="flex items-center justify-between">
                                    <h3 class="font-black text-slate-950">Current Order</h3>
                                    <span class="rounded-full bg-slate-100 px-2.5 py-1 text-xs font-bold text-slate-600" x-text="lines.length + (lines.length === 1 ? ' item' : ' items')"></span>
                                </div>
                            </div>

                            <div class="max-h-[32rem] space-y-3 overflow-y-auto p-4">
                                <template x-if="lines.length === 0">
                                    <div class="rounded-xl border border-dashed border-slate-300 p-8 text-center text-sm text-slate-500">
                                        Add menu items to start the order.
                                    </div>
                                </template>

                                <template x-for="(line, index) in lines" :key="line.menu_item_id">
                                    <div class="rounded-xl border border-slate-200 p-3">
                                        <input type="hidden" :name="'lines[' + index + '][menu_item_id]'" :value="line.menu_item_id">

                                        <div class="flex items-start justify-between gap-3">
                                            <div class="min-w-0">
                                                <p class="truncate font-bold text-slate-900" x-text="line.name"></p>
                                                <p class="mt-1 text-xs text-slate-500"><span x-text="money(line.price)"></span> AFN each</p>
                                            </div>
                                            <button type="button" @click="removeLine(index)" class="text-xs font-bold text-red-600 hover:text-red-700">Remove</button>
                                        </div>

                                        <div class="mt-3 grid grid-cols-[7rem_1fr] gap-3">
                                            <label>
                                                <span class="text-xs font-bold uppercase tracking-wide text-slate-500">Qty</span>
                                                <input
                                                    x-model.number="line.quantity"
                                                    :name="'lines[' + index + '][quantity]'"
                                                    type="number"
                                                    min="1"
                                                    max="999"
                                                    required
                                                    class="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm"
                                                >
                                            </label>

                                            <div>
                                                <span class="text-xs font-bold uppercase tracking-wide text-slate-500">Line total</span>
                                                <div class="mt-1 rounded-lg bg-slate-950 px-3 py-2 text-sm font-black text-white">
                                                    <span x-text="money(lineTotal(line))"></span> AFN
                                                </div>
                                            </div>
                                        </div>

                                        <label class="mt-3 block">
                                            <span class="text-xs font-bold uppercase tracking-wide text-slate-500">Item note</span>
                                            <input
                                                x-model="line.notes"
                                                :name="'lines[' + index + '][notes]'"
                                                maxlength="1000"
                                                placeholder="e.g. no chili, extra sauce"
                                                class="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm"
                                            >
                                        </label>
                                    </div>
                                </template>
                            </div>

                            <div class="space-y-4 border-t border-slate-200 p-5">
                                <label class="block">
                                    <span class="text-sm font-bold text-slate-700">Order note <span class="font-normal text-slate-400">(optional)</span></span>
                                    <textarea name="notes" rows="2" maxlength="2000" placeholder="General order notes..." class="mt-2 w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">{{ old('notes') }}</textarea>
                                </label>

                                <div class="flex items-end justify-between gap-4">
                                    <div>
                                        <p class="text-xs font-bold uppercase tracking-wide text-slate-500">Order total</p>
                                        <p class="mt-1 text-2xl font-black text-slate-950"><span x-text="money(grandTotal())"></span> AFN</p>
                                    </div>
                                </div>

                                <div class="grid gap-2 sm:grid-cols-2">
                                    <button
                                        name="submit_action"
                                        value="draft"
                                        :disabled="lines.length === 0"
                                        class="rounded-xl border border-slate-300 bg-white px-4 py-3 text-sm font-black text-slate-700 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40"
                                    >
                                        Save Draft
                                    </button>
                                    <button
                                        name="submit_action"
                                        value="kitchen"
                                        :disabled="lines.length === 0"
                                        class="rounded-xl bg-emerald-500 px-4 py-3 text-sm font-black text-slate-950 hover:bg-emerald-400 disabled:cursor-not-allowed disabled:opacity-40"
                                    >
                                        Send to Kitchen
                                    </button>
                                </div>
                            </div>
                        </aside>
                    </div>
                </form>
            </div>
        </section>

        <div class="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
            <div>
                <h2 class="text-lg font-black text-slate-950">Order History</h2>
                <p class="mt-1 text-sm text-slate-500">Active and completed restaurant orders.</p>
            </div>

            <form method="GET" action="/orders" class="flex flex-col gap-2 sm:flex-row sm:items-center">
                <select name="status" class="rounded-xl border border-slate-300 bg-white px-4 py-2.5 text-sm">
                    <option value="">All statuses</option>
                    @foreach ($statuses as $status)
                        <option value="{{ $status }}" @selected(request('status') === $status)>{{ ucfirst(str_replace('_', ' ', $status)) }}</option>
                    @endforeach
                </select>
                <button class="rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-bold text-white">Filter</button>
                <a href="/orders" class="rounded-xl border border-slate-300 bg-white px-4 py-2.5 text-center text-sm font-bold">Reset</a>
            </form>
        </div>

        <div class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
            <div class="overflow-x-auto">
                <table class="min-w-full text-left text-sm">
                    <thead class="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                        <tr>
                            <th class="px-5 py-3">Opened</th>
                            <th class="px-5 py-3">Table</th>
                            <th class="px-5 py-3">Waiter</th>
                            <th class="px-5 py-3 text-right">Items</th>
                            <th class="px-5 py-3">Status</th>
                            <th class="px-5 py-3 text-right">Total</th>
                            <th class="px-5 py-3">Bill</th>
                        </tr>
                    </thead>
                    <tbody class="divide-y divide-slate-100">
                        @forelse ($orders as $order)
                            <tr class="align-top {{ session('created_order_id') === $order->id ? 'bg-emerald-50' : 'hover:bg-slate-50' }}">
                                <td class="px-5 py-4 whitespace-nowrap">{{ $order->opened_at?->format('M d, H:i') ?? $order->created_at?->format('M d, H:i') }}</td>
                                <td class="px-5 py-4 font-bold">{{ $order->table?->name ?? '—' }}</td>
                                <td class="px-5 py-4">{{ $order->waiter?->name ?? '—' }}</td>
                                <td class="px-5 py-4 text-right">{{ $order->items->sum('quantity') }}</td>
                                <td class="px-5 py-4">
                                    <span class="rounded-full bg-slate-100 px-2.5 py-1 text-xs font-bold capitalize">{{ str_replace('_', ' ', $order->status) }}</span>
                                </td>
                                <td class="px-5 py-4 text-right font-black">{{ number_format((float) $order->total, 0) }} AFN</td>
                                <td class="px-5 py-4">{{ $order->bill?->bill_number ?? '—' }}</td>
                            </tr>
                        @empty
                            <tr>
                                <td colspan="7" class="px-5 py-12 text-center text-slate-500">
                                    No orders yet. Use <span class="font-bold text-slate-700">New Order / Take Order</span> to enter the first order.
                                </td>
                            </tr>
                        @endforelse
                    </tbody>
                </table>
            </div>
        </div>
    </div>
@endsection
