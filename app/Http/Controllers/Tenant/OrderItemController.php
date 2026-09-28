<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\AddOrderItemRequest;
use App\Models\Order;
use App\Models\TenantUser;
use App\Services\Tenant\OrderService;
use Illuminate\Http\JsonResponse;

class OrderItemController extends Controller
{
    public function store(
        AddOrderItemRequest $request,
        Order $order,
        OrderService $orders,
    ): JsonResponse {
        OrderController::authorizeOrder($request, $order);

        /** @var TenantUser $user */
        $user = $request->user();

        $line = $orders->addItem($order, $user, $request->validated());

        return response()->json(['data' => $line], 201);
    }
}
