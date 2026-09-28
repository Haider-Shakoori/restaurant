<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\Order;
use App\Models\TenantUser;
use App\Services\Tenant\KitchenService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class ServeOrderController extends Controller
{
    public function __invoke(
        Request $request,
        Order $order,
        KitchenService $kitchen,
    ): JsonResponse {
        OrderController::authorizeOrder($request, $order);

        /** @var TenantUser $user */
        $user = $request->user();

        return response()->json([
            'data' => $kitchen->serve($order, $user),
        ]);
    }
}
