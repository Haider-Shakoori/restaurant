@extends('tenant.layouts.app')

@section('title', 'Users & Roles')
@section('heading', 'Users & Roles')
@section('subheading', 'Owner, manager, waiter, cashier, kitchen and inventory access.')

@section('content')
    <div x-data="{ query: '' }" class="space-y-5">
        <div class="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
            <div><h2 class="text-lg font-black">Staff directory</h2><p class="text-xs text-slate-500">{{ $users->count() }} staff accounts · Controlled role access</p></div>
            <input x-model="query" type="search" placeholder="Search staff name or email" class="w-full rounded-xl border border-slate-300 px-4 py-2.5 text-sm sm:w-80">
        </div>
        <div class="grid gap-6 lg:grid-cols-[1fr_22rem]">
        <section class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
            <div class="overflow-x-auto">
                <table class="min-w-full text-left text-sm">
                    <thead class="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                        <tr><th class="px-5 py-3">Name</th><th class="px-5 py-3">Email</th><th class="px-5 py-3">Role</th><th class="px-5 py-3">Status</th><th class="px-5 py-3">Actions</th></tr>
                    </thead>
                    <tbody class="divide-y divide-slate-100">
                        @forelse ($users as $user)
                            <tr x-show="@js(strtolower($user->name.' '.$user->email)).includes(query.toLowerCase())">
                                <td class="px-5 py-4 font-bold">{{ $user->name }}</td>
                                <td class="px-5 py-4">{{ $user->email ?: '—' }}</td>
                                <td class="px-5 py-4 capitalize">{{ $user->role }}</td>
                                <td class="px-5 py-4"><span class="rounded-full px-2.5 py-1 text-xs font-bold {{ $user->is_active ? 'bg-emerald-100 text-emerald-700' : 'bg-slate-100 text-slate-600' }}">{{ $user->is_active ? 'Active' : 'Inactive' }}</span></td>
                                <td class="px-5 py-4">
                                    <details class="min-w-64">
                                        <summary class="cursor-pointer font-bold text-indigo-700">Edit / Reset Password</summary>
                                        <form method="POST" action="{{ route('tenant.web.users.update', $user) }}" class="mt-3 space-y-2 rounded-xl border border-slate-200 bg-slate-50 p-3">
                                            @csrf
                                            @method('PATCH')
                                            <label class="block text-xs font-semibold">Name
                                                <input name="name" value="{{ $user->name }}" required maxlength="255" class="mt-1 w-full rounded-lg border border-slate-300 p-2">
                                            </label>
                                            <label class="block text-xs font-semibold">Email
                                                <input name="email" type="email" value="{{ $user->email }}" required class="mt-1 w-full rounded-lg border border-slate-300 p-2">
                                            </label>
                                            <label class="block text-xs font-semibold">Phone
                                                <input name="phone" value="{{ $user->phone }}" class="mt-1 w-full rounded-lg border border-slate-300 p-2">
                                            </label>
                                            <label class="block text-xs font-semibold">Role
                                                <select name="role" class="mt-1 w-full rounded-lg border border-slate-300 p-2">
                                                    @foreach ($roles as $role)
                                                        <option value="{{ $role }}" @selected($user->role === $role)>{{ ucfirst($role) }}</option>
                                                    @endforeach
                                                </select>
                                            </label>
                                            <label class="block text-xs font-semibold">Account status
                                                <select name="is_active" class="mt-1 w-full rounded-lg border border-slate-300 p-2">
                                                    <option value="1" @selected($user->is_active)>Active</option>
                                                    <option value="0" @selected(! $user->is_active)>Disabled</option>
                                                </select>
                                            </label>
                                            <label class="block text-xs font-semibold">New password (leave blank to keep existing)
                                                <input name="password" type="password" minlength="8" autocomplete="new-password" class="mt-1 w-full rounded-lg border border-slate-300 p-2">
                                            </label>
                                            <button class="rounded-lg bg-slate-900 px-4 py-2 font-bold text-white">Save changes</button>
                                        </form>
                                    </details>
                                </td>
                            </tr>
                        @empty
                            <tr><td colspan="5" class="px-5 py-10 text-center text-slate-500">No restaurant users yet.</td></tr>
                        @endforelse
                    </tbody>
                </table>
            </div>
        </section>

        <aside>
            <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                <h2 class="font-black">Add staff user</h2>
                <form method="POST" action="/setup/user" class="mt-4 space-y-3">
                    @csrf
                    <input name="name" required placeholder="Full name" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                    <input name="email" required type="email" placeholder="Email" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                    <input name="phone" placeholder="Phone" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                    <select name="role" required class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                        @foreach ($roles as $role)<option value="{{ $role }}">{{ ucfirst($role) }}</option>@endforeach
                    </select>
                    <input name="password" required type="password" minlength="8" placeholder="Temporary password" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                    <button class="w-full rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-bold text-white">Create user</button>
                </form>
            </section>
        </aside>
        </div>
    </div>
@endsection
