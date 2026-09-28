<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\Bill;
use App\Models\Order;
use App\Models\TenantUser;
use App\Services\Tenant\BillingService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class BillController extends Controller
{
    public function index(Request $request): JsonResponse
    {
        $bills = Bill::query()
            ->with(['order.table.diningArea', 'branch'])
            ->withCount('payments')
            ->when(
                $request->filled('branch_id'),
                fn ($query) => $query->where('branch_id', $request->string('branch_id')->toString()),
            )
            ->when(
                $request->filled('status'),
                fn ($query) => $query->where('status', $request->string('status')->toString()),
            )
            ->latest('issued_at')
            ->limit(100)
            ->get();

        return response()->json(['data' => $bills]);
    }

    public function store(
        Request $request,
        Order $order,
        BillingService $billing,
    ): JsonResponse {
        /** @var TenantUser $user */
        $user = $request->user();

        return response()->json([
            'data' => $billing->createBill($order, $user),
        ], 201);
    }

    public function show(Bill $bill): JsonResponse
    {
        return response()->json([
            'data' => $bill->load([
                'branch',
                'lines',
                'payments.session',
                'events.actor',
                'order.table.diningArea',
                'order.waiter',
            ]),
        ]);
    }
}
