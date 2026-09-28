@extends('tenant.layouts.app')

@section('title', 'Users & Roles')
@section('heading', 'Users & Roles')
@section('subheading', 'Owner, manager, waiter, cashier, kitchen and inventory access.')

@section('content')
    <div class="grid gap-6 lg:grid-cols-[1fr_22rem]">
        <section class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
            <div class="overflow-x-auto">
                <table class="min-w-full text-left text-sm">
                    <thead class="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                        <tr><th class="px-5 py-3">Name</th><th class="px-5 py-3">Email</th><th class="px-5 py-3">Role</th><th class="px-5 py-3">Status</th></tr>
                    </thead>
                    <tbody class="divide-y divide-slate-100">
                        @forelse ($users as $user)
                            <tr>
                                <td class="px-5 py-4 font-bold">{{ $user->name }}</td>
                                <td class="px-5 py-4">{{ $user->email ?: '—' }}</td>
                                <td class="px-5 py-4 capitalize">{{ $user->role }}</td>
                                <td class="px-5 py-4"><span class="rounded-full px-2.5 py-1 text-xs font-bold {{ $user->is_active ? 'bg-emerald-100 text-emerald-700' : 'bg-slate-100 text-slate-600' }}">{{ $user->is_active ? 'Active' : 'Inactive' }}</span></td>
                            </tr>
                        @empty
                            <tr><td colspan="4" class="px-5 py-10 text-center text-slate-500">No restaurant users yet.</td></tr>
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
@endsection
