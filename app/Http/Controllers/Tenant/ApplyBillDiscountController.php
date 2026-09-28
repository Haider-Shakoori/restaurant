<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\ApplyBillDiscountRequest;
use App\Models\Bill;
use App\Models\TenantUser;
use App\Services\Tenant\BillingService;
use Illuminate\Http\JsonResponse;

class ApplyBillDiscountController extends Controller
{
    public function __invoke(
        ApplyBillDiscountRequest $request,
        Bill $bill,
        BillingService $billing,
    ): JsonResponse {
        /** @var TenantUser $user */
        $user = $request->user();

        return response()->json([
            'data' => $billing->applyDiscount(
                $bill,
                $user,
                $request->string('type')->toString(),
                (string) $request->input('value'),
                $request->input('reason'),
            ),
        ]);
    }
}
