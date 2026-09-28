<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\CloseCashierSessionRequest;
use App\Http\Requests\Tenant\OpenCashierSessionRequest;
use App\Models\CashierSession;
use App\Models\RestaurantBranch;
use App\Models\TenantUser;
use App\Services\Tenant\CashierService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class CashierSessionController extends Controller
{
    public function index(Request $request): JsonResponse
    {
        $sessions = CashierSession::query()
            ->with(['branch', 'cashier'])
            ->when(
                $request->filled('branch_id'),
                fn ($query) => $query->where('branch_id', $request->string('branch_id')->toString()),
            )
            ->when(
                $request->filled('status'),
                fn ($query) => $query->where('status', $request->string('status')->toString()),
            )
            ->latest('opened_at')
            ->limit(100)
            ->get();

        return response()->json(['data' => $sessions]);
    }

    public function store(
        OpenCashierSessionRequest $request,
        CashierService $cashiers,
    ): JsonResponse {
        /** @var TenantUser $user */
        $user = $request->user();

        $branch = RestaurantBranch::query()->findOrFail($request->string('branch_id')->toString());

        return response()->json([
            'data' => $cashiers->openSession(
                $branch,
                $user,
                (string) $request->input('opening_cash'),
            ),
        ], 201);
    }

    public function close(
        CloseCashierSessionRequest $request,
        CashierSession $cashierSession,
        CashierService $cashiers,
    ): JsonResponse {
        /** @var TenantUser $user */
        $user = $request->user();

        return response()->json([
            'data' => $cashiers->closeSession(
                $cashierSession,
                $user,
                (string) $request->input('declared_cash'),
            ),
        ]);
    }
}
