@extends('platform.layouts.app')

@section('title', 'Platform Operators')
@section('heading', 'Platform Operators')
@section('subheading', 'Central BusinessOS staff accounts only. These accounts do not exist inside restaurant tenant databases.')

@section('content')
    @if ($errors->any())
        <div class="mb-5 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
            {{ $errors->first() }}
        </div>
    @endif

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
                            <td class="px-5 py-4">
                                <div class="flex flex-wrap items-center justify-end gap-2">
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

                                    <details class="relative">
                                        <summary class="cursor-pointer list-none rounded-lg bg-slate-950 px-3 py-2 text-xs font-bold text-white">
                                            Change password
                                        </summary>
                                        <div class="absolute right-0 z-20 mt-2 w-80 rounded-xl border border-slate-200 bg-white p-4 text-left shadow-xl">
                                            <p class="text-sm font-bold text-slate-900">Reset {{ $operator->name }} password</p>
                                            <p class="mt-1 text-xs leading-5 text-slate-500">The new password takes effect immediately.</p>
                                            <form method="POST" action="/platform/operators/{{ $operator->id }}/password" class="mt-4 space-y-3"
                                                  onsubmit="return confirm('Change this operator password now?')">
                                                @csrf
                                                @method('PATCH')
                                                <input name="password" type="password" required autocomplete="new-password"
                                                       placeholder="New password"
                                                       class="w-full rounded-lg border border-slate-300 px-3 py-2.5 text-sm outline-none focus:border-amber-500 focus:ring-2 focus:ring-amber-200">
                                                <input name="password_confirmation" type="password" required autocomplete="new-password"
                                                       placeholder="Confirm password"
                                                       class="w-full rounded-lg border border-slate-300 px-3 py-2.5 text-sm outline-none focus:border-amber-500 focus:ring-2 focus:ring-amber-200">
                                                <p class="text-xs text-slate-500">Minimum 10 characters with letters and numbers.</p>
                                                <button class="w-full rounded-lg bg-amber-500 px-3 py-2.5 text-xs font-black text-slate-950 hover:bg-amber-400">
                                                    Save new password
                                                </button>
                                            </form>
                                        </div>
                                    </details>
                                </div>
                            </td>
                        </tr>
                    @endforeach
                </tbody>
            </table>
        </div>
    </div>
@endsection
