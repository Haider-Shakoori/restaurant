@php
    $modules = app(\App\Services\Tenant\RestaurantSettingsService::class)->all();
    $nav = [
        ['/dashboard', 'Dashboard', true],
        ['/tables', 'Tables', in_array($role, ['owner','admin','manager','waiter'], true)],
        ['/menu', 'Menu', true],
        ['/orders', 'Orders', in_array($role, ['owner','admin','manager','waiter','cashier'], true)],
        ['/kitchen', 'Kitchen / KDS', in_array($role, ['owner','admin','manager','kitchen'], true)],
        ['/pos', 'POS & Cashier', in_array($role, ['owner','admin','manager','cashier'], true)],
        ['/inventory', 'Inventory', in_array($role, ['owner','admin','manager','inventory'], true) && $modules['inventory_enabled']],
        ['/purchasing', 'Purchasing', in_array($role, ['owner','admin','manager','inventory'], true) && $modules['inventory_enabled'] && $modules['purchasing_enabled']],
        ['/daily-closing', 'Daily Closing', in_array($role, ['owner','admin','manager','cashier'], true)],
        ['/accounting', 'Accounting', in_array($role, ['owner','admin','manager'], true)],
        ['/users', 'Users & Roles', in_array($role, ['owner','admin'], true)],
        ['/settings', 'Settings', in_array($role, ['owner','admin'], true)],
    ];
@endphp

<nav class="grid gap-1 text-sm">
    @foreach ($nav as [$href, $label, $visible])
        @if ($visible)
            <a href="{{ $href }}"
               class="rounded-lg px-3 py-2.5 font-semibold {{ request()->is(ltrim($href, '/')) || request()->is(ltrim($href, '/').'/*') ? 'bg-slate-800 text-white' : 'text-slate-300 hover:bg-slate-900 hover:text-white' }}">
                {{ $label }}
            </a>
        @endif
    @endforeach
</nav>

<div class="mt-5 border-t border-slate-800 pt-4 lg:hidden">
    <p class="px-3 text-xs font-bold text-slate-200">{{ $currentUser?->name }}</p>
    <p class="mt-1 px-3 text-xs capitalize text-slate-500">{{ $role }}</p>
    <form method="POST" action="/logout" class="mt-3 px-3">
        @csrf
        <button class="rounded-lg border border-slate-700 px-3 py-2 text-xs font-semibold text-slate-200">Sign out</button>
    </form>
</div>
