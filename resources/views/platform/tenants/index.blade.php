@extends('platform.layouts.app')

@section('title', 'Tenant Infrastructure')
@section('heading', 'Tenant Infrastructure')
@section('subheading', 'Infrastructure records and domains only. Restaurant operating data remains inside each tenant database.')

@section('content')
    <div class="overflow-hidden rounded-2xl border border-slate-200 bg-white">
        <div class="overflow-x-auto">
            <table class="min-w-full text-left text-sm">
                <thead class="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                    <tr>
                        <th class="px-5 py-3">Tenant</th>
                        <th class="px-5 py-3">Restaurant</th>
                        <th class="px-5 py-3">Provisioning</th>
                        <th class="px-5 py-3">Domains</th>
                        <th class="px-5 py-3">Created</th>
                    </tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                    @forelse ($tenants as $tenant)
                        @php($business = $businesses->get($tenant->id))
                        <tr>
                            <td class="px-5 py-4 font-mono text-xs">{{ $tenant->id }}</td>
                            <td class="px-5 py-4">
                                @if ($business)
                                    <a href="/platform/restaurants/{{ $business->id }}" class="font-semibold hover:text-emerald-700">{{ $business->name }}</a>
                                @else
                                    <span class="text-slate-500">Not linked</span>
                                @endif
                            </td>
                            <td class="px-5 py-4">{{ ucfirst($tenant->provisioning_state ?? 'pending') }}</td>
                            <td class="px-5 py-4">
                                @forelse ($tenant->domains as $domain)
                                    <div class="font-mono text-xs">{{ $domain->domain }}</div>
                                @empty
                                    <span class="text-slate-500">No domain</span>
                                @endforelse
                            </td>
                            <td class="px-5 py-4 text-slate-500">{{ $tenant->created_at?->format('Y-m-d H:i') }}</td>
                        </tr>
                    @empty
                        <tr><td colspan="5" class="px-5 py-10 text-center text-slate-500">No tenant infrastructure records yet.</td></tr>
                    @endforelse
                </tbody>
            </table>
        </div>
        @if ($tenants->hasPages())
            <div class="border-t border-slate-200 px-5 py-4">{{ $tenants->links() }}</div>
        @endif
    </div>
@endsection
