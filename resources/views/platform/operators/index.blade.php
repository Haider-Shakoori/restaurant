@extends('platform.layouts.app')

@section('title', 'Platform Operators')
@section('heading', 'Platform Operators')
@section('subheading', 'Central BusinessOS staff accounts only. These accounts do not exist inside restaurant tenant databases.')

@section('content')
    <div class="mb-5 flex justify-end">
        <a href="/platform/operators/create" class="rounded-xl bg-emerald-500 px-4 py-2.5 text-sm font-bold text-slate-950">Add operator</a>
    </div>

    <div class="overflow-hidden rounded-2xl border border-slate-200 bg-white">
        <div class="overflow-x-auto">
            <table class="min-w-full text-left text-sm">
                <thead class="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                    <tr>
                        <th class="px-5 py-3">Operator</th>
                        <th class="px-5 py-3">Role</th>
                        <th class="px-5 py-3">Restaurants</th>
                        <th class="px-5 py-3">Last login</th>
                        <th class="px-5 py-3">Status</th>
                        <th class="px-5 py-3"></th>
                    </tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                    @foreach ($operators as $operator)
                        <tr>
                            <td class="px-5 py-4">
                                <div class="font-semibold">{{ $operator->name }}</div>
                                <div class="text-xs text-slate-500">{{ $operator->email }}</div>
                            </td>
                            <td class="px-5 py-4">{{ $operator->role->label() }}</td>
                            <td class="px-5 py-4">{{ $operator->assigned_businesses_count }}</td>
                            <td class="px-5 py-4 text-slate-500">{{ $operator->last_login_at?->format('Y-m-d H:i') ?? 'Never' }}</td>
                            <td class="px-5 py-4">{{ $operator->is_active ? 'Active' : 'Inactive' }}</td>
                            <td class="px-5 py-4 text-right">
                                @if (! $operator->is(auth()->user()))
                                    <form method="POST" action="/platform/operators/{{ $operator->id }}/toggle">
                                        @csrf
                                        @method('PATCH')
                                        <button class="rounded-lg border border-slate-300 px-3 py-2 text-xs font-semibold">
                                            {{ $operator->is_active ? 'Disable' : 'Enable' }}
                                        </button>
                                    </form>
                                @else
                                    <span class="text-xs text-slate-400">Current account</span>
                                @endif
                            </td>
                        </tr>
                    @endforeach
                </tbody>
            </table>
        </div>
    </div>
@endsection
