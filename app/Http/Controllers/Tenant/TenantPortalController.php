<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\Bill;
use App\Models\Business;
use App\Models\CashierSession;
use App\Models\DailyClosing;
use App\Models\DeviceActivation;
use App\Models\DiningTable;
use App\Models\InventoryItem;
use App\Models\JournalEntry;
use App\Models\KitchenStation;
use App\Models\KitchenTicket;
use App\Models\MenuCategory;
use App\Models\LicenseKey;
use App\Models\OperatingExpense;
use App\Models\Order;
use App\Models\PurchaseOrder;
use App\Models\RestaurantBranch;
use App\Models\Supplier;
use App\Models\TenantPayment;
use App\Models\TenantUser;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Auth;
use Illuminate\View\View;

class TenantPortalController extends Controller
{
    public function dashboard(): View
    {
        $today = now()->toDateString();

        return $this->view('tenant.dashboard', [
            'cards' => [
                ['label' => 'Open orders', 'value' => Order::query()->whereIn('status', Order::ACTIVE_STATUSES)->count()],
                ['label' => 'Occupied tables', 'value' => DiningTable::query()->where('status', DiningTable::STATUS_OCCUPIED)->count()],
                ['label' => 'Kitchen queue', 'value' => KitchenTicket::query()->whereIn('status', [
                    KitchenTicket::STATUS_QUEUED,
                    KitchenTicket::STATUS_PREPARING,
                    KitchenTicket::STATUS_READY,
                ])->count()],
                ['label' => 'Sales today', 'value' => number_format((float) Bill::query()->whereDate('issued_at', $today)->where('status', Bill::STATUS_PAID)->sum('total'), 0).' AFN'],
            ],
            'recentOrders' => Order::query()
                ->with(['table.diningArea', 'waiter'])
                ->withCount('items')
                ->latest('opened_at')
                ->limit(8)
                ->get(),
            'recentPayments' => TenantPayment::query()->latest('received_at')->limit(6)->get(),
            'openCashierSessions' => CashierSession::query()
                ->with(['branch', 'cashier'])
                ->where('status', CashierSession::STATUS_OPEN)
                ->latest('opened_at')
                ->limit(6)
                ->get(),
        ]);
    }

    public function tables(): View
    {
        return $this->view('tenant.tables.index', [
            'branches' => RestaurantBranch::query()->with('diningAreas.tables')->orderBy('name')->get(),
        ]);
    }

    public function menu(): View
    {
        return $this->view('tenant.menu.index', [
            'categories' => MenuCategory::query()->with(['items' => fn ($query) => $query->orderBy('sort_order')->orderBy('name')])
                ->orderBy('sort_order')->orderBy('name')->get(),
        ]);
    }

    public function orders(Request $request): View
    {
        $user = Auth::guard('tenant')->user();

        return $this->view('tenant.orders.index', [
            'orders' => Order::query()
                ->with(['table.diningArea', 'waiter', 'items', 'bill'])
                ->when($user?->role === 'waiter', fn ($query) => $query->where('waiter_id', $user->id))
                ->when($request->filled('status'), fn ($query) => $query->where('status', $request->string('status')->toString()))
                ->latest('opened_at')
                ->limit(100)
                ->get(),
            'statuses' => [
                Order::STATUS_DRAFT,
                Order::STATUS_SUBMITTED,
                Order::STATUS_PREPARING,
                Order::STATUS_READY,
                Order::STATUS_SERVED,
                Order::STATUS_BILLED,
                Order::STATUS_CLOSED,
                Order::STATUS_CANCELLED,
            ],
        ]);
    }

    public function kitchen(): View
    {
        return $this->view('tenant.kitchen.index', [
            'stations' => KitchenStation::query()->with('branch')->where('is_active', true)->orderBy('sort_order')->get(),
            'tickets' => KitchenTicket::query()
                ->with(['station', 'items', 'order.table', 'order.waiter'])
                ->whereIn('status', [
                    KitchenTicket::STATUS_QUEUED,
                    KitchenTicket::STATUS_PREPARING,
                    KitchenTicket::STATUS_READY,
                ])
                ->orderBy('queued_at')
                ->limit(100)
                ->get(),
        ]);
    }

    public function pos(): View
    {
        return $this->view('tenant.pos.index', [
            'bills' => Bill::query()->with(['order.table', 'branch'])->withCount('payments')->latest('issued_at')->limit(100)->get(),
            'sessions' => CashierSession::query()->with(['branch', 'cashier'])->latest('opened_at')->limit(30)->get(),
        ]);
    }

    public function inventory(): View
    {
        return $this->view('tenant.inventory.index', [
            'items' => InventoryItem::query()->with('balances')->where('is_active', true)->orderBy('name')->get(),
            'branches' => RestaurantBranch::query()->where('is_active', true)->orderBy('name')->get(),
        ]);
    }

    public function purchasing(): View
    {
        return $this->view('tenant.purchasing.index', [
            'orders' => PurchaseOrder::query()->with(['branch', 'supplier'])->withCount('lines')->latest('ordered_at')->limit(100)->get(),
            'suppliers' => Supplier::query()->where('is_active', true)->orderBy('name')->get(),
        ]);
    }

    public function closing(): View
    {
        return $this->view('tenant.closing.index', [
            'closings' => DailyClosing::query()->with(['branch', 'snapshots'])->latest('business_date')->limit(100)->get(),
            'branches' => RestaurantBranch::query()->where('is_active', true)->orderBy('name')->get(),
        ]);
    }

    public function accounting(): View
    {
        $monthStart = now()->startOfMonth()->toDateString();
        $today = now()->toDateString();

        return $this->view('tenant.accounting.index', [
            'metrics' => [
                'sales' => Bill::query()->whereBetween('issued_at', [$monthStart.' 00:00:00', $today.' 23:59:59'])->where('status', Bill::STATUS_PAID)->sum('total'),
                'expenses' => OperatingExpense::query()->whereBetween('expense_date', [$monthStart, $today])->where('status', 'posted')->sum('amount'),
                'journal_entries' => JournalEntry::query()->whereBetween('entry_date', [$monthStart, $today])->count(),
            ],
            'expenses' => OperatingExpense::query()->with('branch')->latest('expense_date')->limit(30)->get(),
            'journals' => JournalEntry::query()->with('branch')->latest('entry_date')->limit(30)->get(),
        ]);
    }

    public function users(): View
    {
        return $this->view('tenant.users.index', [
            'users' => TenantUser::query()->orderBy('name')->get(),
            'roles' => ['owner', 'admin', 'manager', 'waiter', 'cashier', 'kitchen', 'inventory'],
        ]);
    }

    public function settings(): View
    {
        $business = Business::query()->where('tenant_id', tenant('id'))->first();

        $activeLicense = $business
            ? LicenseKey::query()
                ->where('business_id', $business->id)
                ->where('status', 'active')
                ->latest('version')
                ->first()
            : null;

        $mobileDevices = $business
            ? DeviceActivation::query()
                ->where('business_id', $business->id)
                ->whereIn('platform', ['android', 'ios'])
                ->latest('last_seen_at')
                ->get()
            : collect();

        return $this->view('tenant.settings.index', [
            'branches' => RestaurantBranch::query()->with(['diningAreas', 'kitchenStations'])->orderBy('name')->get(),
            'mobileDevices' => $mobileDevices,
            'activeMobileCount' => $mobileDevices->where('status', 'active')->count(),
            'mobileDeviceLimit' => $activeLicense?->max_mobile_devices_snapshot,
        ]);
    }

    private function view(string $view, array $data = []): View
    {
        $business = Business::query()->where('tenant_id', tenant('id'))->first();

        return view($view, [
            ...$data,
            'restaurant' => $business,
            'currentUser' => Auth::guard('tenant')->user(),
        ]);
    }
}
