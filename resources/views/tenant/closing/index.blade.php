@extends('tenant.layouts.app')

@section('title', 'Daily Closing')
@section('heading', 'Daily Closing')
@section('subheading', 'Review finalized business days and branch closing history.')

@section('content')
    <section class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
        <div class="overflow-x-auto">
            <table class="min-w-full text-left text-sm">
                <thead class="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                    <tr><th class="px-5 py-3">Business date</th><th class="px-5 py-3">Branch</th><th class="px-5 py-3">Status</th><th class="px-5 py-3">Snapshots</th><th class="px-5 py-3">Finalized</th></tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                    @forelse ($closings as $closing)
                        <tr>
                            <td class="px-5 py-4 font-bold">{{ $closing->business_date?->format('Y-m-d') }}</td>
                            <td class="px-5 py-4">{{ $closing->branch?->name ?? '—' }}</td>
                            <td class="px-5 py-4 capitalize">{{ $closing->status }}</td>
                            <td class="px-5 py-4">{{ $closing->snapshots->count() }}</td>
                            <td class="px-5 py-4">{{ $closing->finalized_at?->format('M d, H:i') ?? '—' }}</td>
                        </tr>
                    @empty
                        <tr><td colspan="5" class="px-5 py-10 text-center text-slate-500">No daily closings have been recorded yet.</td></tr>
                    @endforelse
                </tbody>
            </table>
        </div>
    </section>

    <div class="mt-5 rounded-2xl border border-amber-200 bg-amber-50 p-5 text-sm text-amber-900">
        Operational closing and reopen actions remain protected by the existing role-aware API workflow. The web portal currently gives management a safe audit view while cashier/mobile execution uses the same tenant data.
    </div>
@endsection
