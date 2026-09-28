<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\Order;
use App\Models\TenantUser;
use App\Services\Tenant\OrderService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class SubmitOrderController extends Controller
{
    public function __invoke(Request $request, Order $order, OrderService $orders): JsonResponse
    {
        OrderController::authorizeOrder($request, $order);

        /** @var TenantUser $user */
        $user = $request->user();

        return response()->json([
            'data' => $orders->submit($order, $user),
        ]);
    }
}
