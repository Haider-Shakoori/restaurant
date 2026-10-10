@php
    // A separate order-entry surface using the same /orders/take service as Orders.
    // Prices and availability are always recalculated server-side.
    $posMenu = $menuCategories->flatMap(fn ($category) => $category->items->map(fn ($item) => [
        'id' => $item->id,
        'name' => $item->name,
        'category' => $category->name,
        'image' => $item->image_url,
        'price' => (float) $item->price,
        'requires_options' => $item->modifierGroups->contains(fn ($group) => $group->min_selections > 0),
    ]))->values();
    $posExisting = $activeOrders->map(fn ($order) => [
        'id' => $order->id,
        'name' => ($order->table?->name ?? $order->service_reference ?? ucfirst(str_replace('_', ' ', $order->service_type))).' · '.strtoupper($order->status),
        'service_type' => $order->service_type,
        'guests' => $order->guest_count,
    ])->values();
@endphp

<section id="pos-order-entry" class="flex h-[100dvh] min-h-0 flex-col overflow-hidden bg-white"
    x-data="{
        menu: @js($posMenu),
        currentCategory: 'All items',
        query: '',
        cart: [],
        serviceType: @js(old('service_type', 'dine_in')),
        existingOrderId: @js(old('existing_order_id', '')),
        existingOrders: @js($posExisting),
        submitting: false,
        panelTab: 'details',
        submitOrder(event) {
            if (this.submitting || !this.cart.length) { event.preventDefault(); return; }
            if (!event.target.checkValidity()) {
                event.preventDefault();
                this.panelTab = 'details';
                this.$nextTick(() => event.target.reportValidity());
                return;
            }
            this.submitting = true;
        },
        money(value) { return Number(value || 0).toLocaleString(undefined, {maximumFractionDigits: 2}); },
        visibleMenu() {
            return this.menu.filter(item => (this.currentCategory === 'All items' || item.category === this.currentCategory) &&
                (item.name + ' ' + item.category).toLowerCase().includes(this.query.toLowerCase()));
        },
        add(item) {
            if (item.requires_options) return;
            const line = this.cart.find(row => row.id === item.id);
            if (line) { if (line.quantity < 999) line.quantity++; return; }
            if (this.cart.length >= 100) return;
            this.panelTab = 'cart';
            this.cart.push({
                ...item, quantity: 1, client_line_id: window.crypto?.randomUUID?.() ||
                    (Date.now().toString(36) + Math.random().toString(36).slice(2))
            });
        },
        adjust(id, amount) {
            const line = this.cart.find(row => row.id === id);
            if (!line) return;
            line.quantity += amount;
            if (line.quantity <= 0) this.cart = this.cart.filter(row => row.id !== id);
        },
        sum() { return this.cart.reduce((total, item) => total + item.price * item.quantity, 0); },
        selectExisting() {
            const current = this.existingOrders.find(order => order.id === this.existingOrderId);
            if (current) this.serviceType = current.service_type;
        }
    }">
    <header class="flex flex-wrap items-center justify-between gap-3 bg-slate-950 px-5 py-5 text-white">
        <div>
            <p class="text-xs font-black uppercase tracking-widest text-emerald-300">BusinessOS · POS order entry</p>
            <h2 class="mt-1 text-xl font-black">Photo menu & current order</h2>
            <p class="mt-1 text-xs text-slate-300">Tap menu items to build a KOT. Open Bills & payments for cashier operations.</p>
        </div>
        <div class="flex flex-wrap items-center gap-2">
            <button type="button" onclick="document.getElementById('pos-cashier-dialog').showModal()" class="rounded-xl bg-emerald-500 px-4 py-2 text-xs font-black text-white hover:bg-emerald-400">Bills & payments</button>
            <a href="/orders" class="rounded-xl border border-white/25 px-4 py-2 text-xs font-bold text-white hover:bg-white/10">Advanced modifiers</a>
            <a href="/dashboard" class="rounded-xl border border-rose-300/60 px-4 py-2 text-xs font-bold text-white hover:bg-white/10">Exit full-screen POS</a>
            <button type="button" onclick="if(document.fullscreenElement){document.exitFullscreen?.()}else{document.documentElement.requestFullscreen?.()}"
                class="rounded-xl border border-white/25 px-4 py-2 text-xs font-bold text-white hover:bg-white/10">⛶ Kiosk screen</button>
        </div>
    </header>
    <form method="POST" action="/orders/take" class="grid min-h-0 flex-1 gap-0 overflow-hidden max-lg:grid-rows-[minmax(0,1fr)_minmax(0,1fr)] lg:grid-cols-[minmax(0,1.7fr)_minmax(340px,0.85fr)]"
        novalidate @submit="submitOrder($event)">
        @csrf
        <input type="hidden" name="client_order_id" value="{{ (string) \Illuminate\Support\Str::uuid() }}">
        <input type="hidden" name="client_mutation_id" value="{{ (string) \Illuminate\Support\Str::uuid() }}">
        <input type="hidden" name="from_pos" value="1">
        <div class="flex min-h-0 min-w-0 flex-col overflow-hidden border-b border-slate-200 p-3 sm:p-4 lg:border-b-0 lg:border-r">
            <div class="flex shrink-0 gap-2 overflow-x-auto pb-2">
                <button type="button" @click="currentCategory='All items'" :class="currentCategory==='All items' ? 'bg-slate-950 text-white' : 'bg-slate-100 text-slate-700'" class="rounded-lg px-3 py-2 text-xs font-black">All items</button>
                @foreach ($menuCategories as $category)
                    @if ($category->items->isNotEmpty())
                        <button type="button" @click="currentCategory=@js($category->name)" :class="currentCategory===@js($category->name) ? 'bg-slate-950 text-white' : 'bg-slate-100 text-slate-700'" class="rounded-lg px-3 py-2 text-xs font-black">{{ $category->name }}</button>
                    @endif
                @endforeach
            </div>
            <input x-model.debounce.150ms="query" type="search" placeholder="Search dishes or categories..." aria-label="Search POS menu"
                class="mt-2 w-full shrink-0 rounded-xl border border-slate-300 px-4 py-2.5 text-sm focus:border-violet-500 focus:ring-violet-500">
            <div id="pos-menu-items" class="mt-3 grid min-h-0 flex-1 auto-rows-max grid-cols-2 content-start gap-3 overflow-y-auto overscroll-contain pr-1 sm:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5" role="region" aria-label="Scrollable POS menu items" tabindex="0">
                @foreach ($menuCategories as $category)
                    @foreach ($category->items as $item)
                        @php($needsOptions = $item->modifierGroups->contains(fn ($group) => $group->min_selections > 0))
                        <div x-show="(currentCategory === 'All items' || currentCategory === @js($category->name)) && (@js(strtolower($item->name.' '.$category->name))).includes(query.toLowerCase())"
                            class="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm transition hover:border-violet-300 hover:shadow-md">
                            <button type="button" class="flex h-full w-full flex-col text-left disabled:cursor-not-allowed" @click="add(menu.find(item => item.id === @js($item->id)))"
                                @disabled($needsOptions) aria-label="Add {{ $item->name }}">
                                @if ($item->image_url)
                                    <img src="{{ $item->image_url }}" loading="lazy" alt="" onerror="this.style.display='none'"
                                        class="aspect-[4/3] w-full bg-slate-100 object-cover">
                                @else
                                    <div class="flex aspect-[4/3] items-center justify-center bg-slate-100 text-3xl" aria-hidden="true">🍽</div>
                                @endif
                                <span class="flex flex-1 flex-col justify-between gap-2 p-3">
                                    <span class="line-clamp-2 text-xs font-black text-slate-900">{{ $item->name }}</span>
                                    <span class="flex items-center justify-between gap-1">
                                        <span class="text-xs font-black text-violet-700">{{ number_format((float) $item->price, 0) }} AFN</span>
                                        <span class="rounded-lg bg-emerald-100 px-2 py-1 text-xs font-black text-emerald-800">{{ $needsOptions ? 'Options' : '+' }}</span>
                                    </span>
                                </span>
                            </button>
                            @if ($needsOptions)
                                <a href="/orders" class="block border-t border-amber-100 bg-amber-50 px-2 py-2 text-center text-xs font-bold text-amber-900">Configure options in Orders</a>
                            @endif
                        </div>
                    @endforeach
                @endforeach
            </div>
            <p x-show="visibleMenu().length === 0" class="mt-4 text-sm text-slate-500">No matching menu items. Update the filter or manage your menu.</p>
        </div>
        <aside class="flex min-h-0 min-w-0 flex-col overflow-hidden bg-slate-50/75 p-3 sm:p-4" aria-label="POS order workspace">
            <h3 class="text-lg font-black text-slate-950">Current Order</h3>
            <div class="mt-3 flex shrink-0 gap-2 border-b border-slate-200 pb-2" role="tablist" aria-label="Order tabs">
                <button type="button" role="tab" id="pos-details-tab" aria-controls="pos-details-panel" :aria-selected="panelTab === 'details'" @click="panelTab='details'" :class="panelTab === 'details' ? 'bg-slate-950 text-white' : 'bg-white text-slate-700'" class="flex-1 rounded-lg px-3 py-2 text-sm font-bold">Order details</button>
                <button type="button" role="tab" id="pos-cart-tab" aria-controls="pos-cart-panel" :aria-selected="panelTab === 'cart'" @click="panelTab='cart'" :class="panelTab === 'cart' ? 'bg-slate-950 text-white' : 'bg-white text-slate-700'" class="flex-1 rounded-lg px-3 py-2 text-sm font-bold">Cart (<span x-text="cart.length"></span>)</button>
            </div>
            <div class="min-h-0 flex-1 overflow-y-auto overscroll-contain py-2" role="region" aria-label="Scrollable order controls" tabindex="0">
            <div id="pos-details-panel" role="tabpanel" aria-labelledby="pos-details-tab" x-show="panelTab === 'details'">
            <label class="mt-3 block text-xs font-bold text-slate-600">Start or continue
                <select name="existing_order_id" x-model="existingOrderId" @change="selectExisting()" class="mt-1 w-full rounded-xl border border-slate-300 bg-white px-3 py-2.5 text-sm">
                    <option value="">New order</option>
                    @foreach ($activeOrders as $order)
                        <option value="{{ $order->id }}">{{ $order->table?->name ?? $order->service_reference ?? 'Counter' }} · {{ strtoupper($order->status) }}</option>
                    @endforeach
                </select>
            </label>
            <div class="mt-3 grid grid-cols-2 gap-2" x-show="!existingOrderId">
                <label class="text-xs font-bold text-slate-600">Service
                    <select name="service_type" x-model="serviceType" class="mt-1 w-full rounded-lg border border-slate-300 px-2 py-2 text-sm">
                        @foreach (['dine_in' => 'Dine In', 'takeaway' => 'Takeaway', 'delivery' => 'Delivery', 'counter' => 'Counter'] as $type => $label)
                            <option value="{{ $type }}">{{ $label }}</option>
                        @endforeach
                    </select>
                </label>
                <label class="text-xs font-bold text-slate-600">Guests
                    <input type="number" name="guest_count" min="1" max="100" value="1" required class="mt-1 w-full rounded-lg border border-slate-300 px-2 py-2 text-sm">
                </label>
                <label x-show="serviceType === 'dine_in'" class="col-span-2 text-xs font-bold text-slate-600">Available table
                    <select name="dining_table_id" :required="serviceType === 'dine_in' && !existingOrderId" class="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm">
                        <option value="">Select table</option>
                        @foreach ($availableTables as $table)
                            <option value="{{ $table->id }}">{{ $table->diningArea?->branch?->name }} / {{ $table->diningArea?->name }} / {{ $table->name }}</option>
                        @endforeach
                    </select>
                </label>
                <label x-show="serviceType !== 'dine_in'" class="col-span-2 text-xs font-bold text-slate-600">Branch
                    <select name="branch_id" :required="serviceType !== 'dine_in' && !existingOrderId" class="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm">
                        <option value="">Select branch</option>
                        @foreach ($branches as $branch)
                            <option value="{{ $branch->id }}">{{ $branch->name }}</option>
                        @endforeach
                    </select>
                </label>
                <label x-show="serviceType !== 'dine_in'" class="col-span-2 text-xs font-bold text-slate-600">Customer / pickup reference
                    <input type="text" name="service_reference" maxlength="120" class="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm">
                </label>
            </div>
            <input type="hidden" name="service_type" x-bind:value="serviceType" x-bind:disabled="!existingOrderId">
            <input type="hidden" name="guest_count" x-bind:value="existingOrders.find(row => row.id === existingOrderId)?.guests || 1" x-bind:disabled="!existingOrderId">
            </div>
            <div id="pos-cart-panel" role="tabpanel" aria-labelledby="pos-cart-tab" x-show="panelTab === 'cart'" class="space-y-2">
                <p x-show="!cart.length" class="rounded-xl border border-dashed border-slate-300 bg-white p-6 text-center text-sm text-slate-500">Choose dishes on the left to begin.</p>
                <template x-for="(line, index) in cart" :key="line.id">
                    <div class="rounded-xl border border-slate-200 bg-white p-3">
                        <input type="hidden" :name="'lines['+index+'][client_line_id]'" :value="line.client_line_id">
                        <input type="hidden" :name="'lines['+index+'][menu_item_id]'" :value="line.id">
                        <input type="hidden" :name="'lines['+index+'][quantity]'" :value="line.quantity">
                        <div class="flex items-start justify-between gap-3">
                            <div class="min-w-0"><p class="truncate text-sm font-bold" x-text="line.name"></p><p class="text-xs text-slate-500" x-text="money(line.price)+' AFN each'"></p></div>
                            <strong class="shrink-0 text-sm" x-text="money(line.price*line.quantity)+' AFN'"></strong>
                        </div>
                        <div class="mt-2 flex items-center gap-2">
                            <button type="button" @click="adjust(line.id, -1)" class="h-8 w-8 rounded-lg bg-slate-100 font-black" :aria-label="'Decrease '+line.name">−</button>
                            <span x-text="line.quantity" class="min-w-5 text-center text-sm font-bold"></span>
                            <button type="button" @click="adjust(line.id, 1)" class="h-8 w-8 rounded-lg bg-slate-100 font-black" :aria-label="'Increase '+line.name">+</button>
                        </div>
                    </div>
                </template>
            </div>
            </div>
            </div>
            <div class="shrink-0 border-t border-slate-200 bg-slate-50 pt-3">
                <div class="flex justify-between text-base font-black"><span>Order subtotal</span><span x-text="money(sum())+' AFN'"></span></div>
                <p class="mt-2 text-xs text-slate-500">Final charges are calculated by the server. This is not a payment receipt.</p>
                <div class="mt-4 grid grid-cols-2 gap-2">
                    <button type="submit" name="submit_action" value="draft" :disabled="!cart.length || submitting" class="rounded-xl border border-violet-300 bg-white px-3 py-3 text-sm font-black text-violet-800 disabled:opacity-40">Save draft</button>
                    <button type="submit" name="submit_action" value="kitchen" :disabled="!cart.length || submitting" class="rounded-xl bg-emerald-600 px-3 py-3 text-sm font-black text-white disabled:opacity-40">Send KOT</button>
                </div>
            </div>
        </aside>
    </form>
</section>
