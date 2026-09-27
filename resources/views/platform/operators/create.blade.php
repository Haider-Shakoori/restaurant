@extends('platform.layouts.app')

@section('title', 'Add Operator')
@section('heading', 'Add Platform Operator')
@section('subheading', 'Creates a central BusinessOS staff account; it does not create a restaurant user.')

@section('content')
    <form method="POST" action="/platform/operators" class="max-w-2xl rounded-2xl border border-slate-200 bg-white p-6">
        @csrf
        <div class="grid gap-5">
            <label>
                <span class="mb-2 block text-sm font-semibold">Name</span>
                <input name="name" value="{{ old('name') }}" required class="w-full rounded-xl border border-slate-300 px-4 py-3">
            </label>
            <label>
                <span class="mb-2 block text-sm font-semibold">Email</span>
                <input name="email" type="email" value="{{ old('email') }}" required class="w-full rounded-xl border border-slate-300 px-4 py-3">
            </label>
            <label>
                <span class="mb-2 block text-sm font-semibold">Role</span>
                <select name="role" required class="w-full rounded-xl border border-slate-300 px-4 py-3">
                    @foreach ($roles as $role)
                        <option value="{{ $role->value }}" @selected(old('role') === $role->value)>{{ $role->label() }}</option>
                    @endforeach
                </select>
            </label>
            <label>
                <span class="mb-2 block text-sm font-semibold">Password</span>
                <input name="password" type="password" required class="w-full rounded-xl border border-slate-300 px-4 py-3">
            </label>
            <label>
                <span class="mb-2 block text-sm font-semibold">Confirm password</span>
                <input name="password_confirmation" type="password" required class="w-full rounded-xl border border-slate-300 px-4 py-3">
            </label>
        </div>

        <div class="mt-6 flex gap-3">
            <button class="rounded-xl bg-emerald-500 px-5 py-3 font-bold text-slate-950">Create operator</button>
            <a href="/platform/operators" class="rounded-xl border border-slate-300 px-5 py-3 font-semibold">Cancel</a>
        </div>
    </form>
@endsection
