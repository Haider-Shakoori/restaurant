<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\StoreBillPaymentRequest;
use App\Models\Bill;
use App\Models\CashierSession;
use App\Models\TenantUser;
use App\Services\Tenant\BillingService;
use Illuminate\Http\JsonResponse;

class BillPaymentController extends Controller
{
    public function store(
        StoreBillPaymentRequest $request,
        Bill $bill,
        BillingService $billing,
    ): JsonResponse {
        /** @var TenantUser $user */
        $user = $request->user();

        $session = CashierSession::query()->findOrFail(
            $request->string('cashier_session_id')->toString()
        );

        return response()->json([
            'data' => $billing->addPayment(
                $bill,
                $session,
                $user,
                $request->validated(),
            ),
        ], 201);
    }
}
