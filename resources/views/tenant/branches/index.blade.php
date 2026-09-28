@extends('tenant.layouts.app')

@section('title', 'Branches')
@section('heading', 'Branches')
@section('subheading', 'Branches belong to this restaurant tenant; floors, tables, stock and kitchen data will attach to a branch in later batches.')

@section('content')
    <div class="mb-5 flex items-center justify-between gap-4">
        <div class="text-sm text-slate-500">
            Active branches: {{ $branches->where('is_active', true)->count() }}
            @if ($maxBranches)
                / {{ $maxBranches }} allowed by plan
            @endif
        </div>
        <a href="/branches/create" class="rounded-xl bg-emerald-500 px-4 py-2.5 text-sm font-bold text-slate-950">Add branch</a>
    </div>

    <div class="overflow-hidden rounded-2xl border border-slate-200 bg-white">
        <div class="overflow-x-auto">
            <table class="min-w-full text-left text-sm">
                <thead class="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                    <tr>
                        <th class="px-5 py-3">Code</th>
                        <th class="px-5 py-3">Branch</th>
                        <th class="px-5 py-3">Phone</th>
                        <th class="px-5 py-3">Address</th>
                        <th class="px-5 py-3">Status</th>
                        <th class="px-5 py-3"></th>
                    </tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                    @forelse ($branches as $branch)
                        <tr>
                            <td class="px-5 py-4 font-mono text-xs">{{ $branch->code }}</td>
                            <td class="px-5 py-4">
                                <div class="font-semibold">{{ $branch->name }}</div>
                                @if ($branch->is_primary)
                                    <span class="mt-1 inline-flex rounded-full bg-emerald-100 px-2 py-1 text-xs font-bold text-emerald-800">Primary</span>
                                @endif
                            </td>
                            <td class="px-5 py-4">{{ $branch->phone ?: '—' }}</td>
                            <td class="px-5 py-4">{{ $branch->address ?: '—' }}</td>
                            <td class="px-5 py-4">{{ $branch->is_active ? 'Active' : 'Inactive' }}</td>
                            <td class="px-5 py-4 text-right">
                                <a href="/branches/{{ $branch->id }}/edit" class="rounded-lg border border-slate-300 px-3 py-2 text-xs font-semibold">Edit</a>
                            </td>
                        </tr>
                    @empty
                        <tr><td colspan="6" class="px-5 py-10 text-center text-slate-500">No branches yet. Complete onboarding first.</td></tr>
                    @endforelse
                </tbody>
            </table>
        </div>
    </div>
@endsection
