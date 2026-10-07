<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\StoreOrderRequest;
use App\Models\Order;
use App\Models\TenantUser;
use App\Services\Tenant\OrderService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class OrderController extends Controller
{
    public function index(Request $request): JsonResponse
    {
        /** @var TenantUser $user */
        $user = $request->user();

        $orders = Order::query()
            ->with([
                'table.diningArea',
                'waiter',
                'kotRounds.tickets.station',
                'kotRounds.tickets.items',
                'kitchenTickets.station',
            ])
            ->withCount('items')
            ->when($user->role === 'waiter', fn ($query) => $query->where('waiter_id', $user->id))
            ->whereIn('status', Order::ACTIVE_STATUSES)
            ->latest('opened_at')
            ->limit(100)
            ->get();

        return response()->json(['data' => $orders]);
    }

    public function store(StoreOrderRequest $request, OrderService $orders): JsonResponse
    {
        /** @var TenantUser $user */
        $user = $request->user();

        $order = $orders->open($user, $request->validated());

        return response()->json(['data' => $order], 201);
    }

    public function show(Request $request, Order $order): JsonResponse
    {
        $this->authorizeOrder($request, $order);

        return response()->json([
            'data' => $order->load([
                'table.diningArea.branch',
                'waiter',
                'items',
                'events',
                'kotRounds.tickets.station',
                'kotRounds.tickets.items',
                'kitchenTickets.station',
                'kitchenTickets.items',
            ]),
        ]);
    }

    public static function authorizeOrder(Request $request, Order $order): void
    {
        /** @var TenantUser $user */
        $user = $request->user();

        if ($user->role === 'waiter') {
            abort_unless((int) $order->waiter_id === (int) $user->id, 403);
        }
    }
}
