@extends('tenant.layouts.app')

@section('title', 'Tables')
@section('heading', 'Tables')
@section('subheading', 'Dining areas, table capacity and live availability.')

@section('content')
    <div x-data="{ view: 'grid', status: 'all', query: '' }">
        <div class="mb-5 flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
            <div><h2 class="font-black">Dining floor</h2><p class="mt-1 text-xs text-slate-500">Tables remain occupied until the complete bill is settled.</p></div>
            <div class="flex items-center gap-1 rounded-xl bg-slate-100 p-1">
                <button type="button" @click="view = 'list'" :class="view === 'list' ? 'bg-violet-600 text-white' : 'text-slate-600'" class="rounded-lg px-4 py-2 text-xs font-black">List</button>
                <button type="button" @click="view = 'grid'" :class="view === 'grid' ? 'bg-violet-600 text-white' : 'text-slate-600'" class="rounded-lg px-4 py-2 text-xs font-black">Grid</button>
                <button type="button" @click="view = 'floor'" :class="view === 'floor' ? 'bg-violet-600 text-white' : 'text-slate-600'" class="rounded-lg px-4 py-2 text-xs font-black">Floor</button>
            </div>
            <div class="flex flex-wrap items-center gap-3 text-xs font-bold">
                <span class="text-emerald-700">● Available</span>
                <span class="text-blue-700">● Occupied</span>
                <span class="text-rose-700">● Reserved</span>
                <span class="text-slate-500">● Disabled</span>
            </div>
        </div>
        @php
            $floorTables = $branches->flatMap(fn ($branch) => $branch->diningAreas->flatMap(fn ($area) => $area->tables));
        @endphp
        <div class="mb-5 grid gap-3 sm:grid-cols-3">
            @foreach (['available' => 'Available', 'occupied' => 'Occupied', 'reserved' => 'Reserved'] as $state => $title)
                <button type="button" @click="status = status === @js($state) ? 'all' : @js($state)"
                    class="flex items-center justify-between rounded-xl border border-slate-200 bg-white px-4 py-4 text-left shadow-sm hover:border-violet-400">
                    <span class="font-black text-slate-700">{{ $title }}</span>
                    <span class="text-2xl font-black text-slate-950">{{ $floorTables->where('status', $state)->count() }}</span>
                </button>
            @endforeach
        </div>
        <label class="mb-5 block">
            <span class="sr-only">Search tables and areas</span>
            <input type="search" x-model.debounce.120ms="query" placeholder="Find table number, name or area..."
                class="w-full rounded-xl border border-slate-300 bg-white px-4 py-3 text-sm shadow-sm">
        </label>
        <div class="grid gap-6 lg:grid-cols-[1fr_22rem]">
        <div class="space-y-5">
            @forelse ($branches as $branch)
                <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                    <div class="flex items-center justify-between gap-4">
                        <div><h2 class="text-lg font-black">{{ $branch->name }}</h2><p class="text-xs text-slate-500">{{ $branch->code }}</p></div>
                        <span class="rounded-full bg-emerald-50 px-3 py-1 text-xs font-bold text-emerald-700">{{ $branch->is_active ? 'Active' : 'Inactive' }}</span>
                    </div>
                    <div class="mt-5 grid gap-4" :class="view === 'floor' ? 'grid-cols-1' : 'md:grid-cols-2'">
                        @forelse ($branch->diningAreas as $area)
                            <div class="rounded-xl border border-slate-200 p-4" x-show="@js(strtolower($area->name)).includes(query.toLowerCase()) || query === '' || {{ $area->tables->count() }} > 0">
                                <h3 class="font-bold">{{ $area->name }}</h3>
                                <div class="mt-3 grid gap-2" :class="view === 'list' ? 'grid-cols-1' : (view === 'floor' ? 'grid-cols-2 md:grid-cols-3 lg:grid-cols-4' : 'sm:grid-cols-2')">
                                    @forelse ($area->tables as $table)
                                        <div x-show="(status === 'all' || status === @js($table->status)) && (@js(strtolower($table->name.' '.$table->code.' '.$area->name)).includes(query.toLowerCase()))" 
                                            class="rounded-xl border p-3 transition-shadow hover:shadow-md {{ $table->status === 'occupied' ? 'border-blue-200 bg-blue-50' : ($table->status === 'reserved' ? 'border-rose-200 bg-rose-50' : ($table->status === 'available' ? 'border-emerald-200 bg-emerald-50' : 'border-slate-200 bg-slate-100')) }}">
                                            <div class="flex items-center justify-between gap-2">
                                                <span class="flex items-center gap-2 font-bold"><span class="text-xl" aria-hidden="true">◉</span> {{ $table->name }}</span>
                                                <span class="rounded-full bg-white/80 px-2 py-1 text-xs font-black capitalize {{ $table->status === 'occupied' ? 'text-blue-800' : ($table->status === 'reserved' ? 'text-rose-800' : 'text-emerald-800') }}">{{ $table->status }}</span>
                                            </div>
                                            <p class="mt-1 text-xs text-slate-600">{{ $table->code }} · {{ $table->capacity }} seats</p>
                                            @if ($table->orders->isNotEmpty())
                                                @php($activeOrder = $table->orders->first())
                                                <div class="mt-3 border-t border-slate-200/70 pt-2">
                                                    <p class="text-xs font-semibold capitalize">Order: {{ str_replace('_', ' ', $activeOrder->status) }}</p>
                                                    <p class="mt-1 text-xs font-black">{{ number_format((float) $activeOrder->total, 2) }} AFN</p>
                                                    <a href="/orders" class="mt-2 inline-block rounded-lg bg-slate-900 px-3 py-1.5 text-xs font-black text-white">Open order / bill</a>
                                                </div>
                                            @elseif ($table->status === 'available')
                                                <a href="/orders" class="mt-2 inline-block text-xs font-black text-emerald-700">+ Start order</a>
                                            @endif
                                        </div>
                                    @empty
                                        <p class="text-sm text-slate-500">No tables in this area.</p>
                                    @endforelse
                                </div>
                            </div>
                        @empty
                            <p class="text-sm text-slate-500">No dining areas yet.</p>
                        @endforelse
                    </div>
                </section>
            @empty
                <div class="rounded-2xl border border-dashed border-slate-300 bg-white p-10 text-center text-slate-500">Create your first branch to start configuring tables.</div>
            @endforelse
        </div>

        @if (in_array($currentUser->role, ['owner','admin','manager'], true))
            <aside class="space-y-5">
                <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                    <h2 class="font-black">Add dining table</h2>
                    <form method="POST" action="/setup/table" class="mt-4 space-y-3">
                        @csrf
                        <select name="dining_area_id" required class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                            <option value="">Dining area</option>
                            @foreach ($branches as $branch)
                                @foreach ($branch->diningAreas as $area)
                                    <option value="{{ $area->id }}">{{ $branch->name }} · {{ $area->name }}</option>
                                @endforeach
                            @endforeach
                        </select>
                        <input name="code" required placeholder="Code e.g. T01" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                        <input name="name" required placeholder="Table name" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                        <input name="capacity" required type="number" min="1" max="100" value="4" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                        <button class="w-full rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-bold text-white">Add table</button>
                    </form>
                </section>
                <a href="/settings" class="block rounded-xl border border-slate-300 bg-white px-4 py-3 text-center text-sm font-bold">Manage branches & areas</a>
            </aside>
        @endif
        </div>
    </div>
@endsection
