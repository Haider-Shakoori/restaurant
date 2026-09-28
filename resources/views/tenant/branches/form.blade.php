@extends('tenant.layouts.app')

@section('title', $branch ? 'Edit Branch' : 'Add Branch')
@section('heading', $branch ? 'Edit Branch' : 'Add Branch')
@section('subheading', 'Branch codes are unique within the restaurant. Primary-branch changes are protected to avoid leaving the restaurant without a primary location.')

@section('content')
    <form method="POST" action="{{ $branch ? '/branches/'.$branch->id : '/branches' }}" class="max-w-3xl rounded-2xl border border-slate-200 bg-white p-6">
        @csrf
        @if ($branch)
            @method('PUT')
        @endif

        <div class="grid gap-5 sm:grid-cols-2">
            <label>
                <span class="mb-2 block text-sm font-semibold">Code</span>
                <input name="code" required value="{{ old('code', $branch?->code) }}" class="w-full rounded-xl border border-slate-300 px-4 py-3 uppercase">
            </label>
            <label>
                <span class="mb-2 block text-sm font-semibold">Name</span>
                <input name="name" required value="{{ old('name', $branch?->name) }}" class="w-full rounded-xl border border-slate-300 px-4 py-3">
            </label>
            <label>
                <span class="mb-2 block text-sm font-semibold">Phone</span>
                <input name="phone" value="{{ old('phone', $branch?->phone) }}" class="w-full rounded-xl border border-slate-300 px-4 py-3">
            </label>
            <label>
                <span class="mb-2 block text-sm font-semibold">Address</span>
                <input name="address" value="{{ old('address', $branch?->address) }}" class="w-full rounded-xl border border-slate-300 px-4 py-3">
            </label>
            <label class="flex items-center gap-2 text-sm">
                <input type="hidden" name="is_primary" value="0">
                <input type="checkbox" name="is_primary" value="1" @checked(old('is_primary', $branch?->is_primary ?? false))>
                Primary branch
            </label>
            <label class="flex items-center gap-2 text-sm">
                <input type="hidden" name="is_active" value="0">
                <input type="checkbox" name="is_active" value="1" @checked(old('is_active', $branch?->is_active ?? true))>
                Active
            </label>
        </div>

        <div class="mt-6 flex gap-3">
            <button class="rounded-xl bg-slate-900 px-5 py-3 font-semibold text-white">{{ $branch ? 'Save branch' : 'Create branch' }}</button>
            <a href="/branches" class="rounded-xl border border-slate-300 px-5 py-3 font-semibold">Cancel</a>
        </div>
    </form>
@endsection
