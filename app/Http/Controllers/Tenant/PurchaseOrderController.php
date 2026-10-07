<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\ReceivePurchaseOrderRequest;
use App\Http\Requests\Tenant\StorePurchaseOrderRequest;
use App\Models\PurchaseOrder;
use App\Models\RestaurantBranch;
use App\Models\Supplier;
use App\Models\TenantUser;
use App\Services\Tenant\ProcurementService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;

class PurchaseOrderController extends Controller
{
    public function index(Request $request): JsonResponse
    {
        $orders = PurchaseOrder::query()
            ->with(['branch', 'supplier'])
            ->withCount('lines')
            ->when(
                $request->filled('branch_id'),
                fn ($query) => $query->where('branch_id', $request->string('branch_id')->toString()),
            )
            ->when(
                $request->filled('status'),
                fn ($query) => $query->where('status', $request->string('status')->toString()),
            )
            ->latest('ordered_at')
            ->limit(100)
            ->get();

        return response()->json(['data' => $orders]);
    }

    public function store(
        StorePurchaseOrderRequest $request,
        ProcurementService $procurement,
    ): JsonResponse|RedirectResponse {
        /** @var TenantUser $user */
        $user = $request->user();

        $branch = RestaurantBranch::query()->findOrFail(
            $request->string('branch_id')->toString()
        );
        $supplier = Supplier::query()->findOrFail(
            $request->string('supplier_id')->toString()
        );

        $purchaseOrder = $procurement->createPurchaseOrder(
            $branch,
            $supplier,
            $user,
            $request->validated(),
        );

        if (! $request->expectsJson()) {
            return redirect('/purchasing')
                ->with('status', 'Purchase order '.$purchaseOrder->po_number.' created successfully.');
        }

        return response()->json([
            'data' => $purchaseOrder,
        ], 201);
    }

    public function show(PurchaseOrder $purchaseOrder): JsonResponse
    {
        return response()->json([
            'data' => $purchaseOrder->load([
                'branch',
                'supplier',
                'orderedBy',
                'lines.item',
                'receipts.lines.item',
            ]),
        ]);
    }

    public function receive(
        ReceivePurchaseOrderRequest $request,
        PurchaseOrder $purchaseOrder,
        ProcurementService $procurement,
    ): JsonResponse {
        /** @var TenantUser $user */
        $user = $request->user();

        return response()->json([
            'data' => $procurement->receive(
                $purchaseOrder,
                $user,
                $request->validated(),
            ),
        ], 201);
    }
}
