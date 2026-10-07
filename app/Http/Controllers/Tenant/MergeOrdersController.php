<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\Order;
use App\Models\TenantUser;
use App\Services\Tenant\OrderOperationsService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class MergeOrdersController extends Controller
{
    public function __invoke(Request $request, Order $order, OrderOperationsService $operations): JsonResponse
    {
        OrderController::authorizeOrder($request, $order);

        $data = $request->validate([
            'target_order_id' => ['required', 'string', 'exists:orders,id'],
        ]);

        /** @var TenantUser $user */
        $user = $request->user();

        $target = Order::query()->findOrFail($data['target_order_id']);
        OrderController::authorizeOrder($request, $target);

        return response()->json([
            'data' => $operations->mergeOrders($order, $target, $user),
        ]);
    }
}
