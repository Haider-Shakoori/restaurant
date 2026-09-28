<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\StoreOperatingExpenseRequest;
use App\Models\ChartAccount;
use App\Models\OperatingExpense;
use App\Models\RestaurantBranch;
use App\Models\TenantUser;
use App\Services\Tenant\ExpenseService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class OperatingExpenseController extends Controller
{
    public function index(Request $request): JsonResponse
    {
        return response()->json([
            'data' => OperatingExpense::query()
                ->with(['branch', 'expenseAccount', 'paymentAccount'])
                ->when(
                    $request->filled('branch_id'),
                    fn ($query) => $query->where('branch_id', $request->string('branch_id')->toString()),
                )
                ->latest('expense_date')
                ->limit(200)
                ->get(),
        ]);
    }

    public function store(
        StoreOperatingExpenseRequest $request,
        ExpenseService $expenses,
    ): JsonResponse {
        /** @var TenantUser $user */
        $user = $request->user();
        $data = $request->validated();

        return response()->json([
            'data' => $expenses->post(
                RestaurantBranch::query()->findOrFail($data['branch_id']),
                $user,
                ChartAccount::query()->findOrFail($data['expense_account_id']),
                ChartAccount::query()->findOrFail($data['payment_account_id']),
                $data,
            ),
        ], 201);
    }
}
