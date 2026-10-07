@extends('tenant.layouts.app')

@section('title', 'Kitchen')
@section('heading', 'Kitchen / KDS')
@section('subheading', 'Live production by KOT round, station, item and kitchen age.')

@section('content')
    <div class="mb-5 grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
        @foreach ([
            ['KOT rounds today', $performance['kot_rounds']],
            ['Production items', $performance['production_items']],
            ['Avg prep', $performance['average_prep_seconds'] === null ? '—' : round($performance['average_prep_seconds'] / 60, 1).' min'],
            ['Re-fires', $performance['refires']],
            ['Waste qty', $performance['waste_quantity']],
        ] as [$label, $value])
            <div class="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
                <p class="text-xs font-bold uppercase tracking-[0.12em] text-slate-400">{{ $label }}</p>
                <p class="mt-2 text-2xl font-black text-slate-900">{{ $value }}</p>
            </div>
        @endforeach
    </div>

    <div class="mb-5 flex flex-wrap items-center gap-2">
        <span class="mr-2 text-xs font-black uppercase tracking-[0.12em] text-slate-400">Stations</span>
        @forelse ($stations as $station)
            <span class="rounded-full border border-slate-200 bg-white px-3 py-1.5 text-xs font-bold">
                {{ $station->name }}
            </span>
        @empty
            <span class="text-sm text-slate-500">No kitchen stations configured yet.</span>
        @endforelse

        <div class="ml-auto flex flex-wrap gap-2 text-xs font-bold">
            <span class="rounded-full bg-amber-50 px-3 py-1.5 text-amber-800">
                Warning {{ $restaurantSettings['kitchen_warning_minutes'] }}m
            </span>
            <span class="rounded-full bg-rose-50 px-3 py-1.5 text-rose-800">
                Late {{ $restaurantSettings['kitchen_late_minutes'] }}m
            </span>
        </div>
    </div>

    <div class="grid gap-4 md:grid-cols-2 2xl:grid-cols-3">
        @forelse ($tickets as $ticket)
            @php
                $ageMinutes = $ticket->queued_at?->diffInMinutes(now()) ?? 0;
                $late = $ageMinutes >= $restaurantSettings['kitchen_late_minutes'];
                $warning = ! $late && $ageMinutes >= $restaurantSettings['kitchen_warning_minutes'];
                $borderClass = $late
                    ? 'border-rose-300 ring-2 ring-rose-100'
                    : ($warning ? 'border-amber-300 ring-2 ring-amber-100' : 'border-slate-200');
            @endphp

            <article class="rounded-2xl border bg-white p-5 shadow-sm {{ $borderClass }}">
                <div class="flex items-start justify-between gap-3">
                    <div>
                        <p class="text-xs font-bold uppercase tracking-[0.12em] text-slate-400">
                            {{ $ticket->station?->name ?? 'Kitchen' }}
                        </p>
                        <div class="mt-1 flex flex-wrap items-center gap-2">
                            <h2 class="text-xl font-black">
                                {{ $ticket->human_kot_number ?: $ticket->ticket_number }}
                            </h2>
                            @if ($ticket->round)
                                <span class="rounded-full bg-slate-100 px-2 py-1 text-[10px] font-black text-slate-600">
                                    Round {{ $ticket->round->sequence }}
                                </span>
                                @if ($ticket->round->priority === 'rush')
                                    <span class="rounded-full bg-rose-600 px-2 py-1 text-[10px] font-black text-white">
                                        RUSH
                                    </span>
                                @endif
                            @endif
                        </div>
                    </div>

                    <span class="rounded-full px-3 py-1 text-xs font-black capitalize
                        {{ $ticket->status === 'ready'
                            ? 'bg-emerald-100 text-emerald-800'
                            : ($ticket->status === 'preparing'
                                ? 'bg-amber-100 text-amber-800'
                                : ($ticket->status === 'active'
                                    ? 'bg-sky-100 text-sky-800'
                                    : 'bg-slate-100 text-slate-700')) }}">
                        {{ $ticket->status }}
                    </span>
                </div>

                <div class="mt-3 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-slate-500">
                    <span>
                        {{ $ticket->order?->table?->name
                            ?? $ticket->order?->service_reference
                            ?? ucfirst(str_replace('_', ' ', $ticket->order?->service_type ?? 'order')) }}
                    </span>
                    <span>•</span>
                    <span>{{ $ticket->order?->waiter?->name ?? 'Waiter' }}</span>
                    <span>•</span>
                    <span class="{{ $late ? 'font-black text-rose-700' : ($warning ? 'font-black text-amber-700' : '') }}">
                        {{ $ageMinutes }}m
                        @if ($late)
                            · DELAYED
                        @elseif ($warning)
                            · WARNING
                        @endif
                    </span>
                </div>

                <div class="mt-4 space-y-2">
                    @foreach ($ticket->items as $item)
                        <div class="rounded-xl border border-slate-100 bg-slate-50 p-3">
                            <div class="flex items-start justify-between gap-3">
                                <div>
                                    <p class="font-black text-slate-900">
                                        {{ $item->item_name }}
                                        <span class="ml-1 text-slate-500">× {{ $item->quantity }}</span>
                                    </p>

                                    <div class="mt-1 flex flex-wrap gap-1.5 text-[11px] font-bold">
                                        @if ($item->seat_number)
                                            <span class="rounded bg-white px-2 py-1 text-slate-600">Seat {{ $item->seat_number }}</span>
                                        @endif
                                        @if ($item->course_number)
                                            <span class="rounded bg-white px-2 py-1 text-slate-600">
                                                {{ $item->course_name ?: 'Course '.$item->course_number }}
                                            </span>
                                        @endif
                                        @if ($item->refire_of_kitchen_ticket_item_id)
                                            <span class="rounded bg-rose-100 px-2 py-1 text-rose-700">RE-FIRE</span>
                                        @endif
                                    </div>
                                </div>

                                <span class="rounded-full bg-white px-2.5 py-1 text-[10px] font-black uppercase text-slate-600">
                                    {{ $item->status }}
                                </span>
                            </div>

                            @if ($item->modifiers_snapshot)
                                <div class="mt-2 text-xs text-slate-600">
                                    @foreach ($item->modifiers_snapshot as $group)
                                        <span class="font-bold">{{ $group['group_name'] ?? 'Modifier' }}:</span>
                                        {{ collect($group['options'] ?? [])->pluck('option_name')->join(', ') }}
                                        @if (! $loop->last) · @endif
                                    @endforeach
                                </div>
                            @endif

                            @if ($item->kitchen_instructions)
                                <p class="mt-2 text-xs font-semibold text-slate-700">
                                    Kitchen: {{ $item->kitchen_instructions }}
                                </p>
                            @endif

                            @if ($item->allergy_instructions)
                                <p class="mt-2 rounded-lg bg-rose-100 px-3 py-2 text-xs font-black text-rose-800">
                                    ⚠ ALLERGY / CRITICAL: {{ $item->allergy_instructions }}
                                </p>
                            @endif

                            <div class="mt-3 flex flex-wrap gap-2">
                                @if ($restaurantSettings['preparing_stage_enabled'] && in_array($item->status, ['queued', 'active'], true))
                                    <form method="POST" action="/kitchen/items/{{ $item->id }}/start">
                                        @csrf
                                        <button class="rounded-lg bg-amber-500 px-3 py-2 text-xs font-black text-slate-950 hover:bg-amber-400">
                                            Start
                                        </button>
                                    </form>
                                @endif

                                @if (
                                    ($restaurantSettings['preparing_stage_enabled'] && $item->status === 'preparing')
                                    || (! $restaurantSettings['preparing_stage_enabled'] && in_array($item->status, ['queued', 'active'], true))
                                )
                                    <form method="POST" action="/kitchen/items/{{ $item->id }}/ready">
                                        @csrf
                                        <button class="rounded-lg bg-emerald-500 px-3 py-2 text-xs font-black text-slate-950 hover:bg-emerald-400">
                                            Ready
                                        </button>
                                    </form>
                                @endif
                            </div>

                            @if (! in_array($item->status, ['cancelled'], true))
                                <details class="mt-3 rounded-lg border border-slate-200 bg-white p-3">
                                    <summary class="cursor-pointer text-xs font-black text-slate-600">
                                        Recovery / exception actions
                                    </summary>

                                    <div class="mt-3 space-y-3">
                                        @if (! in_array($item->status, ['voided', 'completed'], true))
                                            <form method="POST" action="/kitchen/items/{{ $item->id }}/void" class="flex flex-col gap-2 sm:flex-row">
                                                @csrf
                                                <input name="reason" required maxlength="1000"
                                                       placeholder="Void reason"
                                                       class="min-w-0 flex-1 rounded-lg border border-slate-300 px-3 py-2 text-xs">
                                                <button class="rounded-lg border border-rose-300 bg-rose-50 px-3 py-2 text-xs font-black text-rose-700">
                                                    Void
                                                </button>
                                            </form>
                                        @endif

                                        @if ($item->status === 'ready')
                                            <form method="POST" action="/kitchen/items/{{ $item->id }}/recall" class="flex flex-col gap-2 sm:flex-row">
                                                @csrf
                                                <input name="reason" required maxlength="1000"
                                                       placeholder="Recall reason"
                                                       class="min-w-0 flex-1 rounded-lg border border-slate-300 px-3 py-2 text-xs">
                                                <button class="rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-xs font-black text-amber-800">
                                                    Recall
                                                </button>
                                            </form>
                                        @endif

                                        @if (in_array($item->status, ['ready', 'completed', 'voided'], true))
                                            <form method="POST" action="/kitchen/items/{{ $item->id }}/refire" class="flex flex-col gap-2 sm:flex-row">
                                                @csrf
                                                <input type="hidden" name="client_operation_id" value="{{ (string) \Illuminate\Support\Str::uuid() }}">
                                                <input name="reason" required maxlength="1000"
                                                       placeholder="Re-fire reason"
                                                       class="min-w-0 flex-1 rounded-lg border border-slate-300 px-3 py-2 text-xs">
                                                <button class="rounded-lg border border-sky-300 bg-sky-50 px-3 py-2 text-xs font-black text-sky-800">
                                                    Re-fire
                                                </button>
                                            </form>
                                        @endif

                                        @if (in_array($item->status, ['preparing', 'ready', 'completed', 'voided'], true))
                                            <form method="POST" action="/kitchen/items/{{ $item->id }}/waste" class="grid gap-2 sm:grid-cols-[6rem_minmax(0,1fr)_auto]">
                                                @csrf
                                                <input name="quantity" type="number" min="1" max="{{ $item->quantity }}" value="1" required
                                                       class="rounded-lg border border-slate-300 px-3 py-2 text-xs">
                                                <input name="reason" required maxlength="1000"
                                                       placeholder="Waste reason"
                                                       class="min-w-0 rounded-lg border border-slate-300 px-3 py-2 text-xs">
                                                <button class="rounded-lg border border-slate-300 bg-slate-100 px-3 py-2 text-xs font-black text-slate-700">
                                                    Record waste
                                                </button>
                                            </form>
                                        @endif
                                    </div>
                                </details>
                            @endif
                        </div>
                    @endforeach
                </div>

                <p class="mt-4 text-xs text-slate-400">
                    Sent {{ $ticket->round?->sent_at?->diffForHumans() ?? $ticket->queued_at?->diffForHumans() }}
                </p>
            </article>
        @empty
            <div class="rounded-2xl border border-dashed border-slate-300 bg-white p-10 text-center text-slate-500 md:col-span-2 2xl:col-span-3">
                Kitchen queue is clear.
            </div>
        @endforelse
    </div>
@endsection
