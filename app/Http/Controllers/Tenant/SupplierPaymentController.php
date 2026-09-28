<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\StoreSupplierPaymentRequest;
use App\Models\ChartAccount;
use App\Models\RestaurantBranch;
use App\Models\Supplier;
use App\Models\SupplierPayment;
use App\Models\TenantUser;
use App\Services\Tenant\SupplierPaymentService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class SupplierPaymentController extends Controller
{
    public function index(Request $request): JsonResponse
    {
        return response()->json([
            'data' => SupplierPayment::query()
                ->with(['branch', 'supplier', 'paymentAccount'])
                ->when(
                    $request->filled('branch_id'),
                    fn ($query) => $query->where('branch_id', $request->string('branch_id')->toString()),
                )
                ->when(
                    $request->filled('supplier_id'),
                    fn ($query) => $query->where('supplier_id', $request->string('supplier_id')->toString()),
                )
                ->latest('payment_date')
                ->limit(200)
                ->get(),
        ]);
    }

    public function store(
        StoreSupplierPaymentRequest $request,
        SupplierPaymentService $payments,
    ): JsonResponse {
        /** @var TenantUser $user */
        $user = $request->user();
        $data = $request->validated();

        return response()->json([
            'data' => $payments->post(
                RestaurantBranch::query()->findOrFail($data['branch_id']),
                Supplier::query()->findOrFail($data['supplier_id']),
                ChartAccount::query()->findOrFail($data['payment_account_id']),
                $user,
                $data,
            ),
        ], 201);
    }
}
