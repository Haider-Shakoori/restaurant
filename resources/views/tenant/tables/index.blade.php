@extends('tenant.layouts.app')

@section('title', 'Tables')
@section('heading', 'Tables')
@section('subheading', 'Dining areas, table capacity and live availability.')

@section('content')
    <div class="grid gap-6 lg:grid-cols-[1fr_22rem]">
        <div class="space-y-5">
            @forelse ($branches as $branch)
                <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                    <div class="flex items-center justify-between gap-4">
                        <div><h2 class="text-lg font-black">{{ $branch->name }}</h2><p class="text-xs text-slate-500">{{ $branch->code }}</p></div>
                        <span class="rounded-full bg-emerald-50 px-3 py-1 text-xs font-bold text-emerald-700">{{ $branch->is_active ? 'Active' : 'Inactive' }}</span>
                    </div>
                    <div class="mt-5 grid gap-4 md:grid-cols-2">
                        @forelse ($branch->diningAreas as $area)
                            <div class="rounded-xl border border-slate-200 p-4">
                                <h3 class="font-bold">{{ $area->name }}</h3>
                                <div class="mt-3 grid gap-2 sm:grid-cols-2">
                                    @forelse ($area->tables as $table)
                                        <div class="rounded-lg bg-slate-50 p-3">
                                            <div class="flex items-center justify-between gap-2">
                                                <span class="font-bold">{{ $table->name }}</span>
                                                <span class="text-xs capitalize text-slate-500">{{ $table->status }}</span>
                                            </div>
                                            <p class="mt-1 text-xs text-slate-500">{{ $table->code }} · {{ $table->capacity }} seats</p>
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
@endsection
