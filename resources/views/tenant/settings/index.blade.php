@extends('tenant.layouts.app')

@section('title', 'Settings')
@section('heading', 'Restaurant Settings')
@section('subheading', 'Configure restaurant operations, mobile connectivity and kitchen structure.')

@section('content')

    <section id="modules" class="mb-6 overflow-hidden rounded-2xl border border-violet-200 bg-white shadow-sm">
        <div class="border-b border-slate-100 bg-slate-950 px-5 py-5 text-white sm:px-6">
            <p class="text-xs font-bold uppercase tracking-widest text-violet-300">Settings / Modules</p>
            <h2 class="mt-1 text-xl font-black">Restaurant capability controls</h2>
            <p class="mt-2 text-sm text-slate-300">Switch operational modules on or off for the whole restaurant, including connected Windows terminals after sync. Existing recipes, purchases and stock history are retained.</p>
        </div>
        <form method="POST" action="/settings/modules" class="p-5 sm:p-6" x-data="{
            recipes: @js((bool) old('recipes_enabled', $restaurantSettings['recipes_enabled'])),
            inventory: @js((bool) old('inventory_enabled', $restaurantSettings['inventory_enabled'])),
            purchasing: @js((bool) old('purchasing_enabled', $restaurantSettings['purchasing_enabled'])),
            automatic: @js((bool) old('automatic_recipe_consumption_enabled', $restaurantSettings['automatic_recipe_consumption_enabled']))
        }">
            @csrf
            <div class="grid gap-3 md:grid-cols-2">
                <label class="flex cursor-pointer items-start justify-between gap-4 rounded-xl border border-slate-200 p-4">
                    <span><span class="block font-black">Recipe management</span><span class="mt-1 block text-xs leading-5 text-slate-500">Ingredient recipes, portion quantities, and cost tracking.</span></span>
                    <input type="hidden" name="recipes_enabled" value="0"><input type="checkbox" name="recipes_enabled" value="1" x-model="recipes" @change="if (!recipes) automatic = false" class="mt-1 h-5 w-5 accent-violet-600">
                </label>
                <label class="flex cursor-pointer items-start justify-between gap-4 rounded-xl border border-slate-200 p-4">
                    <span><span class="block font-black">Inventory management</span><span class="mt-1 block text-xs leading-5 text-slate-500">Stock levels, valuation, adjustments, and alerts.</span></span>
                    <input type="hidden" name="inventory_enabled" value="0"><input type="checkbox" name="inventory_enabled" value="1" x-model="inventory" @change="if (!inventory) { purchasing = false; automatic = false }" class="mt-1 h-5 w-5 accent-violet-600">
                </label>
                <label class="flex cursor-pointer items-start justify-between gap-4 rounded-xl border border-slate-200 p-4">
                    <span><span class="block font-black">Purchasing management</span><span class="mt-1 block text-xs leading-5 text-slate-500">Suppliers, purchase orders, and received stock. Requires Inventory.</span></span>
                    <input type="hidden" name="purchasing_enabled" value="0"><input type="checkbox" name="purchasing_enabled" value="1" x-model="purchasing" :disabled="!inventory" class="mt-1 h-5 w-5 accent-violet-600">
                </label>
                <label class="flex cursor-pointer items-start justify-between gap-4 rounded-xl border border-slate-200 p-4">
                    <span><span class="block font-black">Automatic recipe consumption</span><span class="mt-1 block text-xs leading-5 text-slate-500">Reserve and deduct recipe ingredients as orders are prepared. Requires Recipes and Inventory.</span></span>
                    <input type="hidden" name="automatic_recipe_consumption_enabled" value="0"><input type="checkbox" name="automatic_recipe_consumption_enabled" value="1" x-model="automatic" :disabled="!recipes || !inventory" class="mt-1 h-5 w-5 accent-violet-600">
                </label>
            </div>
            <div class="mt-5 flex flex-wrap items-center justify-between gap-3">
                <p class="max-w-xl text-xs text-slate-500">POS, KOT, orders, payments, and table closing always remain active. Turning off a module never deletes financial or inventory records.</p>
                <button type="submit" class="rounded-xl bg-violet-600 px-5 py-3 text-sm font-black text-white hover:bg-violet-700">Save modules</button>
            </div>
        </form>
    </section>

    <section class="mb-6 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <div class="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
            <div>
                <h2 class="font-black">Desktop & waiter connectivity</h2>
                <p class="mt-1 max-w-3xl text-sm text-slate-500">
                    Waiter apps use Automatic mode: restaurant LAN first, cloud fallback when LAN is unavailable,
                    offline queue when neither path is reachable, then automatic return to LAN.
                </p>
            </div>
            <div class="grid min-w-[280px] grid-cols-2 gap-3">
                <div class="rounded-xl bg-slate-50 p-3">
                    <div class="text-xs font-bold uppercase tracking-wide text-slate-400">Waiter mobiles</div>
                    <div class="mt-1 text-lg font-black">{{ $activeMobileDevices }} / {{ $mobileDeviceLimit ?? '∞' }}</div>
                </div>
                <div class="rounded-xl bg-slate-50 p-3">
                    <div class="text-xs font-bold uppercase tracking-wide text-slate-400">Cloud route</div>
                    <div class="mt-1 text-sm font-black text-emerald-700">Available via this tenant</div>
                </div>
            </div>
        </div>

        <div class="mt-5 overflow-x-auto">
            <table class="min-w-full text-left text-sm">
                <thead class="text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                        <th class="px-3 py-2">Device</th>
                        <th class="px-3 py-2">Platform</th>
                        <th class="px-3 py-2">Status</th>
                        <th class="px-3 py-2">Last seen</th>
                        <th class="px-3 py-2 text-right">Action</th>
                    </tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                    @forelse ($activatedDevices as $device)
                        <tr>
                            <td class="px-3 py-3 font-semibold">{{ $device->device_name ?: 'Unnamed device' }}</td>
                            <td class="px-3 py-3">{{ strtoupper($device->platform) }}</td>
                            <td class="px-3 py-3">{{ ucfirst($device->status->value) }}</td>
                            <td class="px-3 py-3 text-slate-500">{{ $device->last_seen_at?->diffForHumans() ?? 'Never' }}</td>
                            <td class="px-3 py-3 text-right">
                                @if (in_array(strtolower($device->platform), ['android', 'ios'], true) && $device->status->value === 'active')
                                    <form method="POST" action="/settings/devices/{{ $device->id }}/revoke" onsubmit="return confirm('Revoke this waiter mobile activation?');">
                                        @csrf
                                        <button class="rounded-lg border border-rose-200 px-3 py-1.5 text-xs font-bold text-rose-700 hover:bg-rose-50">
                                            Revoke mobile
                                        </button>
                                    </form>
                                @else
                                    <span class="text-xs text-slate-400">—</span>
                                @endif
                            </td>
                        </tr>
                    @empty
                        <tr>
                            <td colspan="5" class="px-3 py-5 text-center text-slate-500">No activated devices yet.</td>
                        </tr>
                    @endforelse
                </tbody>
            </table>
        </div>
    </section>

    <section class="mb-6 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <div class="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
            <div>
                <h2 class="font-black">Kitchen workflow</h2>
                <p class="mt-1 max-w-3xl text-sm text-slate-500">
                    Queue and Preparing are independent switches. New KOT rounds use the current settings; historical kitchen work remains unchanged.
                </p>
            </div>
            <span class="rounded-full bg-emerald-50 px-3 py-1 text-xs font-black text-emerald-700">Shared with Web, Mobile & Desktop</span>
        </div>

        <form method="POST" action="/settings/restaurant" class="mt-5 space-y-5">
            @csrf
            <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                @foreach ([
                    'kitchen_queue_enabled' => ['Queue', 'New KOT production may enter QUEUED before becoming production-active.'],
                    'preparing_stage_enabled' => ['Preparing', 'Kitchen staff explicitly start production before marking it ready.'],
                    'expo_enabled' => ['Expo', 'Aggregate station readiness before food is served.'],
                    'courses_enabled' => ['Courses', 'Allow held/fired starter, main and dessert sequencing.'],
                ] as $key => [$label, $help])
                    <label class="rounded-xl border border-slate-200 p-4">
                        <input type="hidden" name="{{ $key }}" value="0">
                        <div class="flex items-start gap-3">
                            <input
                                type="checkbox"
                                name="{{ $key }}"
                                value="1"
                                @checked(old($key, $restaurantSettings[$key]) == true)
                                class="mt-1 h-4 w-4 rounded border-slate-300 text-emerald-600 focus:ring-emerald-500"
                            >
                            <div>
                                <p class="font-black text-slate-900">{{ $label }}</p>
                                <p class="mt-1 text-xs leading-5 text-slate-500">{{ $help }}</p>
                            </div>
                        </div>
                    </label>
                @endforeach
            </div>

            <div class="grid gap-4 lg:grid-cols-4">
                <label>
                    <span class="text-sm font-bold text-slate-700">Kitchen warning (minutes)</span>
                    <input name="kitchen_warning_minutes" type="number" min="1" max="240" required
                           value="{{ old('kitchen_warning_minutes', $restaurantSettings['kitchen_warning_minutes']) }}"
                           class="mt-2 w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                </label>
                <label>
                    <span class="text-sm font-bold text-slate-700">Kitchen late (minutes)</span>
                    <input name="kitchen_late_minutes" type="number" min="1" max="480" required
                           value="{{ old('kitchen_late_minutes', $restaurantSettings['kitchen_late_minutes']) }}"
                           class="mt-2 w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                </label>
                <label>
                    <span class="text-sm font-bold text-slate-700">Negative stock policy</span>
                    <select name="negative_stock_policy" class="mt-2 w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                        @foreach (['block' => 'Block', 'warn' => 'Warn', 'allow' => 'Allow'] as $value => $label)
                            <option value="{{ $value }}" @selected(old('negative_stock_policy', $restaurantSettings['negative_stock_policy']) === $value)>{{ $label }}</option>
                        @endforeach
                    </select>
                </label>
                <div class="space-y-3">
                    <label class="flex items-start gap-3 rounded-xl border border-slate-200 p-3">
                        <input type="hidden" name="kot_sound_enabled" value="0">
                        <input type="checkbox" name="kot_sound_enabled" value="1" @checked(old('kot_sound_enabled', $restaurantSettings['kot_sound_enabled']) == true)
                               class="mt-1 h-4 w-4 rounded border-slate-300 text-emerald-600 focus:ring-emerald-500">
                        <span><span class="block text-sm font-black">KOT sound</span><span class="text-xs text-slate-500">Allow KDS clients to play new-KOT alerts.</span></span>
                    </label>
                    <label class="flex items-start gap-3 rounded-xl border border-slate-200 p-3">
                        <input type="hidden" name="require_manager_approval_post_kot_void" value="0">
                        <input type="checkbox" name="require_manager_approval_post_kot_void" value="1" @checked(old('require_manager_approval_post_kot_void', $restaurantSettings['require_manager_approval_post_kot_void']) == true)
                               class="mt-1 h-4 w-4 rounded border-slate-300 text-emerald-600 focus:ring-emerald-500">
                        <span><span class="block text-sm font-black">Manager approval for post-KOT void</span><span class="text-xs text-slate-500">Require approval before dispatched production is voided.</span></span>
                    </label>
                </div>
            </div>

            <div class="rounded-xl bg-slate-50 p-4 text-sm text-slate-600">
                <strong>Current mode:</strong>
                @if ($restaurantSettings['kitchen_queue_enabled'] && $restaurantSettings['preparing_stage_enabled'])
                    Queue → Preparing → Ready
                @elseif ($restaurantSettings['kitchen_queue_enabled'])
                    Queue → Ready
                @elseif ($restaurantSettings['preparing_stage_enabled'])
                    Active → Preparing → Ready
                @else
                    Active → Ready
                @endif
            </div>

            <button class="rounded-xl bg-slate-900 px-5 py-3 text-sm font-black text-white">Save kitchen workflow</button>
        </form>
    </section>

    <div class="grid gap-6 xl:grid-cols-3">
        <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 class="font-black">Add branch</h2>
            <form method="POST" action="/setup/branch" class="mt-4 space-y-3">
                @csrf
                <input name="code" required placeholder="Code e.g. MAIN" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                <input name="name" required placeholder="Branch name" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                <button class="w-full rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-bold text-white">Create branch</button>
            </form>
        </section>

        <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 class="font-black">Add dining area</h2>
            <form method="POST" action="/setup/area" class="mt-4 space-y-3">
                @csrf
                <select name="branch_id" required class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                    <option value="">Choose branch</option>
                    @foreach ($branches as $branch)<option value="{{ $branch->id }}">{{ $branch->name }}</option>@endforeach
                </select>
                <input name="name" required placeholder="Area name e.g. Main Hall" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                <button class="w-full rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-bold text-white">Create area</button>
            </form>
        </section>

        <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 class="font-black">Add kitchen station</h2>
            <form method="POST" action="/setup/station" class="mt-4 space-y-3">
                @csrf
                <select name="branch_id" required class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                    <option value="">Choose branch</option>
                    @foreach ($branches as $branch)<option value="{{ $branch->id }}">{{ $branch->name }}</option>@endforeach
                </select>
                <input name="code" required placeholder="Code e.g. HOT" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                <input name="name" required placeholder="Station name" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                <button class="w-full rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-bold text-white">Create station</button>
            </form>
        </section>
    </div>

    <section class="mt-6 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <h2 class="font-black">Current structure</h2>
        <div class="mt-4 grid gap-4 lg:grid-cols-2">
            @forelse ($branches as $branch)
                <div class="rounded-xl border border-slate-200 p-4">
                    <div class="flex items-center justify-between gap-3">
                        <div><p class="font-black">{{ $branch->name }}</p><p class="text-xs text-slate-500">{{ $branch->code }}</p></div>
                        <span class="text-xs font-bold text-emerald-700">{{ $branch->is_active ? 'Active' : 'Inactive' }}</span>
                    </div>
                    <div class="mt-4 grid gap-3 sm:grid-cols-2">
                        <div><p class="text-xs font-bold uppercase text-slate-400">Dining areas</p><p class="mt-1 text-sm">{{ $branch->diningAreas->pluck('name')->join(', ') ?: 'None' }}</p></div>
                        <div><p class="text-xs font-bold uppercase text-slate-400">Kitchen stations</p><p class="mt-1 text-sm">{{ $branch->kitchenStations->pluck('name')->join(', ') ?: 'None' }}</p></div>
                    </div>
                </div>
            @empty
                <p class="text-sm text-slate-500">No branches configured yet.</p>
            @endforelse
        </div>
    </section>
@endsection
