@extends('platform.layouts.app')

@section('title', 'Add Restaurant')
@section('heading', 'Add Restaurant')
@section('subheading', 'Creates the central commercial record only. Infrastructure remains pending until the provisioning workflow runs.')

@section('content')
    <form method="POST" action="/platform/restaurants" class="max-w-4xl rounded-2xl border border-slate-200 bg-white p-6">
        @csrf
        <div class="grid gap-5 md:grid-cols-2">
            <label class="md:col-span-2">
                <span class="mb-2 block text-sm font-semibold">Restaurant / business name</span>
                <input name="name" value="{{ old('name') }}" required class="w-full rounded-xl border border-slate-300 px-4 py-3">
            </label>
            <label>
                <span class="mb-2 block text-sm font-semibold">Requested subdomain</span>
                <input name="requested_subdomain" value="{{ old('requested_subdomain') }}" placeholder="restaurant-name" class="w-full rounded-xl border border-slate-300 px-4 py-3">
            </label>
            <label>
                <span class="mb-2 block text-sm font-semibold">Location</span>
                <input name="location" value="{{ old('location') }}" placeholder="Kabul, Afghanistan" class="w-full rounded-xl border border-slate-300 px-4 py-3">
            </label>
            <label>
                <span class="mb-2 block text-sm font-semibold">Contact person</span>
                <input name="contact_name" value="{{ old('contact_name') }}" required class="w-full rounded-xl border border-slate-300 px-4 py-3">
            </label>
            <label>
                <span class="mb-2 block text-sm font-semibold">Phone</span>
                <input name="phone" value="{{ old('phone') }}" required class="w-full rounded-xl border border-slate-300 px-4 py-3">
            </label>
            <label>
                <span class="mb-2 block text-sm font-semibold">WhatsApp</span>
                <input name="whatsapp" value="{{ old('whatsapp') }}" class="w-full rounded-xl border border-slate-300 px-4 py-3">
            </label>
            <label>
                <span class="mb-2 block text-sm font-semibold">Email</span>
                <input name="email" type="email" value="{{ old('email') }}" class="w-full rounded-xl border border-slate-300 px-4 py-3">
            </label>
            <label>
                <span class="mb-2 block text-sm font-semibold">Plan</span>
                <select name="plan_id" class="w-full rounded-xl border border-slate-300 px-4 py-3">
                    <option value="">Unassigned</option>
                    @foreach ($plans as $plan)
                        <option value="{{ $plan->id }}" @selected((string) old('plan_id') === (string) $plan->id)>{{ $plan->name }}</option>
                    @endforeach
                </select>
            </label>
            <label>
                <span class="mb-2 block text-sm font-semibold">Assigned operator</span>
                <select name="assigned_operator_id" class="w-full rounded-xl border border-slate-300 px-4 py-3">
                    <option value="">Unassigned</option>
                    @foreach ($operators as $operator)
                        <option value="{{ $operator->id }}" @selected((string) old('assigned_operator_id') === (string) $operator->id)>{{ $operator->name }}</option>
                    @endforeach
                </select>
            </label>
        </div>
        <div class="mt-6 flex gap-3">
            <button class="rounded-xl bg-emerald-500 px-5 py-3 font-bold text-slate-950">Create pending restaurant</button>
            <a href="/platform/restaurants" class="rounded-xl border border-slate-300 px-5 py-3 font-semibold">Cancel</a>
        </div>
    </form>
@endsection
