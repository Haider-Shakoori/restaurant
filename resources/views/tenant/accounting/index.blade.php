@extends('tenant.layouts.app')

@section('title', 'Accounting')
@section('heading', 'Accounting')
@section('subheading', 'Month-to-date sales, expenses and journal activity.')

@section('content')
    <div class="grid gap-4 sm:grid-cols-3">
        <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <p class="text-xs font-bold uppercase tracking-wide text-slate-400">Sales this month</p>
            <p class="mt-3 text-3xl font-black">{{ number_format((float) $metrics['sales'], 0) }} AFN</p>
        </div>
        <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <p class="text-xs font-bold uppercase tracking-wide text-slate-400">Expenses this month</p>
            <p class="mt-3 text-3xl font-black">{{ number_format((float) $metrics['expenses'], 0) }} AFN</p>
        </div>
        <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <p class="text-xs font-bold uppercase tracking-wide text-slate-400">Journal entries</p>
            <p class="mt-3 text-3xl font-black">{{ $metrics['journal_entries'] }}</p>
        </div>
    </div>

    <div class="mt-6 grid gap-6 xl:grid-cols-2">
        <section class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
            <div class="border-b border-slate-200 px-5 py-4"><h2 class="font-black">Recent expenses</h2></div>
            <div class="divide-y divide-slate-100">
                @forelse ($expenses as $expense)
                    <div class="flex items-start justify-between gap-4 px-5 py-4">
                        <div><p class="font-bold">{{ $expense->description }}</p><p class="mt-1 text-xs text-slate-500">{{ $expense->branch?->name ?? 'Branch' }} · {{ $expense->expense_date?->format('Y-m-d') }}</p></div>
                        <span class="font-black">{{ number_format((float) $expense->amount, 0) }} AFN</span>
                    </div>
                @empty
                    <p class="px-5 py-8 text-sm text-slate-500">No expenses yet.</p>
                @endforelse
            </div>
        </section>

        <section class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
            <div class="border-b border-slate-200 px-5 py-4"><h2 class="font-black">Recent journals</h2></div>
            <div class="divide-y divide-slate-100">
                @forelse ($journals as $journal)
                    <div class="px-5 py-4">
                        <div class="flex items-start justify-between gap-4">
                            <div><p class="font-bold">{{ $journal->entry_number }}</p><p class="mt-1 text-sm text-slate-600">{{ $journal->description }}</p></div>
                            <span class="rounded-full bg-slate-100 px-2.5 py-1 text-xs font-bold capitalize">{{ $journal->status }}</span>
                        </div>
                        <p class="mt-2 text-xs text-slate-400">{{ $journal->entry_date?->format('Y-m-d') }} · {{ $journal->source_type }}</p>
                    </div>
                @empty
                    <p class="px-5 py-8 text-sm text-slate-500">No journal entries yet.</p>
                @endforelse
            </div>
        </section>
    </div>
@endsection
