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
                'modifier_groups' => $item->modifierGroups->map(fn ($group) => [
                    'id' => $group->id,
                    'name' => $group->name,
                    'min_selections' => $group->min_selections,
                    'max_selections' => $group->max_selections,
                    'options' => $group->options->map(fn ($option) => [
                        'id' => $option->id,
                        'name' => $option->name,
                        'price_delta' => (float) $option->price_delta,
                    ])->values(),
                ])->values(),
            ]))
            ->values();

        $oldLines = collect(old('lines', []))
            ->map(function ($line) use ($menuOptions) {
                $item = $menuOptions->firstWhere('id', $line['menu_item_id'] ?? null);

                return [
                    'client_line_id' => $line['client_line_id'] ?? '',
                    'menu_item_id' => $line['menu_item_id'] ?? '',
                    'name' => $item['name'] ?? 'Menu item',
                    'price' => (float) ($item['price'] ?? 0),
                    'quantity' => (int) ($line['quantity'] ?? 1),
                    'notes' => $line['notes'] ?? '',
                    'seat_number' => $line['seat_number'] ?? '',
                    'course_number' => $line['course_number'] ?? '',
                    'course_name' => $line['course_name'] ?? '',
                    'hold_for_course' => (bool) ($line['hold_for_course'] ?? false),
                    'allergy_instructions' => $line['allergy_instructions'] ?? '',
                    'kitchen_instructions' => $line['kitchen_instructions'] ?? '',
                    'modifier_groups' => $item['modifier_groups'] ?? [],
                    'selected_modifiers' => collect($line['modifiers'] ?? [])->pluck('option_id')->values(),
                ];
            })
            ->values();

        $activeOrderOptions = $orders
            ->whereIn('status', ['draft', 'submitted', 'preparing', 'ready', 'served'])
            ->values();

        $openTakeOrder = $errors->has('dining_table_id')
            || $errors->has('guest_count')
            || $errors->has('lines')
            || collect($errors->keys())->contains(fn ($key) => str_starts_with($key, 'lines.'));
    @endphp

    <div
        x-data="{
            showTakeOrder: @js($openTakeOrder),
            serviceType: @js(old('service_type', 'dine_in')),
            existingOrderId: @js(old('existing_order_id', '')),
            items: @js($menuOptions),
            lines: @js($oldLines),
            addItem(item) {
                const existing = this.lines.find(line => line.menu_item_id === item.id);

                if (existing) {
                    existing.quantity++;
                    return;
                }

                this.lines.push({
                    client_line_id: (window.crypto && window.crypto.randomUUID)
                        ? window.crypto.randomUUID()
                        : String(Date.now()) + '-' + Math.random().toString(16).slice(2),
                    menu_item_id: item.id,
                    name: item.name,
                    price: Number(item.price),
                    quantity: 1,
                    notes: '',
                    seat_number: '',
                    course_number: '',
                    course_name: '',
                    hold_for_course: false,
                    allergy_instructions: '',
                    kitchen_instructions: '',
                    modifier_groups: item.modifier_groups || [],
                    selected_modifiers: []
                });
            },
            removeLine(index) {
                this.lines.splice(index, 1);
            },
            toggleModifier(line, optionId, maxSelections, group) {
                const selectedInGroup = line.selected_modifiers.filter(id =>
                    (group.options || []).some(option => option.id === id)
                );

                if (line.selected_modifiers.includes(optionId)) {
                    line.selected_modifiers = line.selected_modifiers.filter(id => id !== optionId);
                    return;
                }

                if (selectedInGroup.length < Number(maxSelections || 1)) {
                    line.selected_modifiers.push(optionId);
                }
            },
            modifierDelta(line) {
                return (line.modifier_groups || []).reduce((sum, group) => {
                    return sum + (group.options || []).reduce((groupSum, option) => {
                        return groupSum + (line.selected_modifiers.includes(option.id)
                            ? Number(option.price_delta || 0)
                            : 0);
                    }, 0);
                }, 0);
            },
            lineTotal(line) {
                return (Number(line.price || 0) + this.modifierDelta(line))
                    * Number(line.quantity || 0);
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
                    @disabled($menuOptions->isEmpty())
                    class="inline-flex items-center justify-center rounded-xl bg-emerald-500 px-5 py-3 text-sm font-black text-slate-950 shadow-sm hover:bg-emerald-400 disabled:cursor-not-allowed disabled:opacity-40"
                >
                    <span class="mr-2 text-lg leading-none">+</span>
                    <span x-text="showTakeOrder ? 'Close Order Entry' : 'New Order / Take Order'"></span>
                </button>
            </div>

            @if ($menuOptions->isEmpty())
                <div class="border-b border-amber-200 bg-amber-50 px-5 py-4 text-sm text-amber-900">
                    <span class="font-bold">Order entry needs setup:</span>
                    @if ($menuOptions->isEmpty()) there are no available menu items.@endif
                    Use <a href="/tables" class="font-black underline">Tables</a> and <a href="/menu" class="font-black underline">Menu</a> to configure them.
                </div>
            @endif

            <div x-show="showTakeOrder" x-cloak class="border-b border-slate-200 bg-slate-50/70 p-5 sm:p-6">
                <form method="POST" action="/orders/take" class="space-y-6">
                    @csrf
                    <input type="hidden" name="client_order_id" value="{{ old('client_order_id', (string) \Illuminate\Support\Str::uuid()) }}">
                    <input type="hidden" name="client_mutation_id" value="{{ old('client_mutation_id', (string) \Illuminate\Support\Str::uuid()) }}">

                    <div class="rounded-2xl border border-slate-200 bg-white p-4">
                        <label>
                            <span class="text-sm font-black text-slate-800">Order target</span>
                            <select name="existing_order_id" x-model="existingOrderId"
                                    class="mt-2 w-full rounded-xl border border-slate-300 bg-white px-4 py-3 text-sm">
                                <option value="">Create a new order</option>
                                @foreach ($activeOrderOptions as $activeOrder)
                                    <option value="{{ $activeOrder->id }}">
                                        Add another KOT to
                                        {{ $activeOrder->table?->name
                                            ?? $activeOrder->service_reference
                                            ?? ucfirst(str_replace('_', ' ', $activeOrder->service_type ?? 'order')) }}
                                        · {{ ucfirst($activeOrder->status) }}
                                    </option>
                                @endforeach
                            </select>
                            <p class="mt-2 text-xs text-slate-500">
                                Select an active order to add only new items and send a later KOT round without resending earlier production.
                            </p>
                        </label>
                    </div>

                    <div x-show="!existingOrderId" class="grid gap-4 lg:grid-cols-4">
                        <label>
                            <span class="text-sm font-bold text-slate-700">Service type</span>
                            <select x-model="serviceType" name="service_type" required
                                    class="mt-2 w-full rounded-xl border border-slate-300 bg-white px-4 py-3 text-sm">
                                @foreach (['dine_in' => 'Dine In', 'takeaway' => 'Takeaway', 'delivery' => 'Delivery', 'counter' => 'Counter'] as $value => $label)
                                    <option value="{{ $value }}">{{ $label }}</option>
                                @endforeach
                            </select>
                        </label>

                        <label x-show="serviceType !== 'dine_in'">
                            <span class="text-sm font-bold text-slate-700">Branch</span>
                            <select name="branch_id" :required="serviceType !== 'dine_in'"
                                    class="mt-2 w-full rounded-xl border border-slate-300 bg-white px-4 py-3 text-sm">
                                <option value="">Select branch</option>
                                @foreach ($activeBranches as $branch)
                                    <option value="{{ $branch->id }}" @selected((string) old('branch_id') === (string) $branch->id)>
                                        {{ $branch->name }}
                                    </option>
                                @endforeach
                            </select>
                        </label>

                        <label x-show="serviceType === 'dine_in'">
                            <span class="text-sm font-bold text-slate-700">Dining table</span>
                            <select name="dining_table_id" :required="serviceType === 'dine_in'"
                                    class="mt-2 w-full rounded-xl border border-slate-300 bg-white px-4 py-3 text-sm">
                                <option value="">Select an available table</option>
                                @foreach ($availableTables as $table)
                                    <option value="{{ $table->id }}" @selected((string) old('dining_table_id') === (string) $table->id)>
                                        {{ $table->diningArea?->branch?->name }} · {{ $table->diningArea?->name }} · {{ $table->name }}
                                    </option>
                                @endforeach
                            </select>
                        </label>

                        <label x-show="serviceType !== 'dine_in'">
                            <span class="text-sm font-bold text-slate-700">Reference</span>
                            <input name="service_reference" value="{{ old('service_reference') }}"
                                   placeholder="Token / delivery / customer ref"
                                   class="mt-2 w-full rounded-xl border border-slate-300 bg-white px-4 py-3 text-sm">
                        </label>

                        <label>
                            <span class="text-sm font-bold text-slate-700">Guests</span>
                            <input name="guest_count" type="number" min="1" max="100" value="{{ old('guest_count', 1) }}" required
                                   class="mt-2 w-full rounded-xl border border-slate-300 bg-white px-4 py-3 text-sm">
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
                                                    @click="addItem(items.find(item => item.id === '{{ $item->id }}'))"
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
                                        <input type="hidden" :name="'lines[' + index + '][client_line_id]'" :value="line.client_line_id">
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

                                        <template x-for="group in line.modifier_groups" :key="group.id">
                                            <div class="mt-3">
                                                <p class="text-xs font-black uppercase tracking-wide text-slate-500"
                                                   x-text="group.name + ' (' + group.min_selections + '–' + group.max_selections + ')'"></p>
                                                <div class="mt-2 flex flex-wrap gap-2">
                                                    <template x-for="option in group.options" :key="option.id">
                                                        <label class="cursor-pointer rounded-lg border border-slate-200 bg-slate-50 px-2.5 py-1.5 text-xs font-semibold">
                                                            <input
                                                                type="checkbox"
                                                                class="mr-1"
                                                                :checked="line.selected_modifiers.includes(option.id)"
                                                                @change="toggleModifier(line, option.id, group.max_selections, group)"
                                                            >
                                                            <span x-text="option.name"></span>
                                                            <span class="text-emerald-700" x-text="' +' + money(option.price_delta)"></span>
                                                        </label>
                                                    </template>
                                                </div>
                                                <template x-for="(optionId, modifierIndex) in line.selected_modifiers" :key="optionId">
                                                    <input type="hidden"
                                                           :name="'lines[' + index + '][modifiers][' + modifierIndex + '][option_id]'"
                                                           :value="optionId">
                                                </template>
                                            </div>
                                        </template>

                                        <div class="mt-3 grid gap-2 sm:grid-cols-3">
                                            <input x-model="line.seat_number" :name="'lines[' + index + '][seat_number]'"
                                                   type="number" min="1" placeholder="Seat"
                                                   class="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                                            <input x-model="line.course_number" :name="'lines[' + index + '][course_number]'"
                                                   type="number" min="1" placeholder="Course #"
                                                   class="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                                            <input x-model="line.course_name" :name="'lines[' + index + '][course_name]'"
                                                   placeholder="Course name"
                                                   class="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                                        </div>

                                        <label class="mt-2 flex items-center gap-2 text-xs font-bold text-slate-600">
                                            <input type="hidden" :name="'lines[' + index + '][hold_for_course]'" value="0">
                                            <input type="checkbox" x-model="line.hold_for_course"
                                                   :name="'lines[' + index + '][hold_for_course]'" value="1">
                                            Hold until course is fired
                                        </label>

                                        <label class="mt-3 block">
                                            <span class="text-xs font-bold uppercase tracking-wide text-slate-500">Item note</span>
                                            <input x-model="line.notes" :name="'lines[' + index + '][notes]'"
                                                   maxlength="1000" placeholder="Guest-facing note"
                                                   class="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm">
                                        </label>

                                        <label class="mt-2 block">
                                            <span class="text-xs font-bold uppercase tracking-wide text-slate-500">Kitchen instruction</span>
                                            <input x-model="line.kitchen_instructions"
                                                   :name="'lines[' + index + '][kitchen_instructions]'"
                                                   maxlength="1000" placeholder="e.g. sauce on side"
                                                   class="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm">
                                        </label>

                                        <label class="mt-2 block">
                                            <span class="text-xs font-black uppercase tracking-wide text-rose-600">Allergy / critical</span>
                                            <input x-model="line.allergy_instructions"
                                                   :name="'lines[' + index + '][allergy_instructions]'"
                                                   maxlength="1000" placeholder="Critical kitchen instruction"
                                                   class="mt-1 w-full rounded-lg border border-rose-300 bg-rose-50 px-3 py-2 text-sm">
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

        <div class="grid gap-4 md:grid-cols-2 2xl:grid-cols-3">
            @forelse ($orders as $order)
                @php
                    $isPaid = $order->bill?->status === 'paid';
                    $bill = $order->bill;
                    $cashierAllowed = in_array($currentUser?->role, ['owner', 'admin', 'manager', 'cashier'], true);
                @endphp
                <article class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm {{ session('created_order_id') === $order->id ? 'ring-2 ring-violet-400' : '' }}">
                    <div class="flex items-start justify-between gap-3 border-b border-slate-100 bg-slate-950 p-4 text-white">
                        <div>
                            <p class="text-xs font-bold text-violet-300">#{{ substr($order->id, -8) }}</p>
                            <h3 class="mt-1 font-black">{{ $order->table?->name ?? ucfirst(str_replace('_', ' ', $order->service_type)) }}</h3>
                            <p class="mt-1 text-xs text-slate-300">{{ $order->waiter?->name ?? 'Unassigned waiter' }}</p>
                        </div>
                        <span class="rounded-lg px-2.5 py-1 text-xs font-black {{ $isPaid ? 'bg-emerald-600 text-white' : 'bg-amber-100 text-amber-900' }}">
                            {{ $isPaid ? 'PAID' : ($bill ? 'PAYMENT DUE' : 'NOT BILLED') }}
                        </span>
                    </div>
                    <div class="space-y-3 p-4">
                        <div class="flex items-center justify-between gap-3 text-xs text-slate-500">
                            <span>{{ $order->opened_at?->format('M d, H:i') }}</span>
                            <span>{{ $order->items->sum('quantity') }} item(s)</span>
                        </div>
                        <div class="flex items-center justify-between gap-2">
                            <span class="rounded-lg bg-violet-100 px-2.5 py-1 text-xs font-black capitalize text-violet-800">{{ str_replace('_', ' ', $order->status) }}</span>
                            <span class="text-lg font-black text-slate-950">{{ number_format((float) $order->total, 2) }} <small class="text-xs">AFN</small></span>
                        </div>
                        <p class="text-xs text-slate-500">
                            Kitchen & service: <strong class="capitalize">{{ str_replace('_', ' ', $order->status) }}</strong>
                            @if ($bill)
                                · Balance due: <strong class="text-rose-700">{{ number_format((float) $bill->balance_due, 2) }} AFN</strong>
                            @endif
                        </p>
                        <div class="flex flex-wrap gap-2 border-t border-slate-100 pt-3">
                            @if ($order->status === 'ready')
                                <form method="POST" action="/orders/{{ $order->id }}/serve">@csrf
                                    <button class="rounded-lg bg-emerald-600 px-3 py-2 text-xs font-black text-white">Mark served</button>
                                </form>
                            @endif
                            @if ($cashierAllowed && $order->status === 'served')
                                <form method="POST" action="/orders/{{ $order->id }}/bill">@csrf
                                    <button class="rounded-lg bg-violet-600 px-3 py-2 text-xs font-black text-white">Issue bill</button>
                                </form>
                            @endif
                            @if ($cashierAllowed && $bill && $bill->status === 'open')
                                <a class="rounded-lg bg-slate-900 px-3 py-2 text-xs font-black text-white" href="/pos">Receive payment</a>
                            @endif
                            @if ($order->status === 'closed')
                                <span class="rounded-lg bg-emerald-50 px-3 py-2 text-xs font-bold text-emerald-700">Closed · Table available</span>
                            @endif
                        </div>
                    </div>
                </article>
            @empty
                <div class="rounded-2xl border border-dashed border-slate-300 bg-white p-10 text-center text-sm text-slate-500 md:col-span-2">
                    No orders match this filter. Select <strong>New Order / Take Order</strong> to begin.
                </div>
            @endforelse
        </div>

    </div>
@endsection
