@extends('tenant.layouts.app')

@section('title', 'Kitchen')
@section('heading', 'Kitchen / KDS')
@section('subheading', 'Live kitchen tickets by station and preparation status.')

@section('content')
    <div class="mb-5 flex flex-wrap gap-2">
        @forelse ($stations as $station)
            <span class="rounded-full border border-slate-200 bg-white px-3 py-1.5 text-xs font-bold">{{ $station->name }}</span>
        @empty
            <span class="text-sm text-slate-500">No kitchen stations configured yet.</span>
        @endforelse
    </div>

    <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        @forelse ($tickets as $ticket)
            <article class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                <div class="flex items-start justify-between gap-3">
                    <div>
                        <p class="text-xs font-bold uppercase tracking-[0.12em] text-slate-400">{{ $ticket->station?->name ?? 'Kitchen' }}</p>
                        <h2 class="mt-1 text-xl font-black">{{ $ticket->ticket_number }}</h2>
                    </div>
                    <span class="rounded-full px-3 py-1 text-xs font-black capitalize {{ $ticket->status === 'ready' ? 'bg-emerald-100 text-emerald-800' : ($ticket->status === 'preparing' ? 'bg-amber-100 text-amber-800' : 'bg-slate-100 text-slate-700') }}">
                        {{ $ticket->status }}
                    </span>
                </div>
                <p class="mt-3 text-sm text-slate-500">
                    {{ $ticket->order?->table?->name ?? 'Table' }} · {{ $ticket->order?->waiter?->name ?? 'Waiter' }}
                </p>
                <div class="mt-4 space-y-2">
                    @foreach ($ticket->items as $item)
                        <div class="flex items-center justify-between rounded-lg bg-slate-50 px-3 py-2 text-sm">
                            <span class="font-semibold">{{ $item->item_name }}</span>
                            <span class="font-black">× {{ $item->quantity }}</span>
                        </div>
                    @endforeach
                </div>
                <p class="mt-4 text-xs text-slate-400">Queued {{ $ticket->queued_at?->diffForHumans() }}</p>
            </article>
        @empty
            <div class="md:col-span-2 xl:col-span-3 rounded-2xl border border-dashed border-slate-300 bg-white p-10 text-center text-slate-500">Kitchen queue is clear.</div>
        @endforelse
    </div>
@endsection
