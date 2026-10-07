<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\DiningTable;
use App\Models\Order;
use App\Models\TenantUser;
use App\Services\Tenant\OrderOperationsService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class TransferOrderTableController extends Controller
{
    public function __invoke(Request $request, Order $order, OrderOperationsService $operations): JsonResponse
    {
        OrderController::authorizeOrder($request, $order);

        $data = $request->validate([
            'target_table_id' => ['required', 'string', 'exists:dining_tables,id'],
        ]);

        /** @var TenantUser $user */
        $user = $request->user();

        return response()->json([
            'data' => $operations->transferTable(
                $order,
                DiningTable::query()->findOrFail($data['target_table_id']),
                $user,
            ),
        ]);
    }
}
