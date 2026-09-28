<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\FinalizeDailyClosingRequest;
use App\Http\Requests\Tenant\ReopenDailyClosingRequest;
use App\Models\DailyClosing;
use App\Models\RestaurantBranch;
use App\Models\TenantUser;
use App\Services\Tenant\DailyClosingService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class DailyClosingController extends Controller
{
    public function index(Request $request): JsonResponse
    {
        $closings = DailyClosing::query()
            ->with(['branch', 'snapshots'])
            ->when(
                $request->filled('branch_id'),
                fn ($query) => $query->where('branch_id', $request->string('branch_id')->toString()),
            )
            ->latest('business_date')
            ->limit(100)
            ->get();

        return response()->json(['data' => $closings]);
    }

    public function show(DailyClosing $dailyClosing): JsonResponse
    {
        return response()->json([
            'data' => $dailyClosing->load(['branch', 'snapshots', 'events']),
        ]);
    }

    public function finalize(
        FinalizeDailyClosingRequest $request,
        DailyClosingService $closings,
    ): JsonResponse {
        /** @var TenantUser $user */
        $user = $request->user();

        $branch = RestaurantBranch::query()->findOrFail(
            $request->string('branch_id')->toString()
        );

        return response()->json([
            'data' => $closings->finalize(
                $branch,
                $request->string('business_date')->toString(),
                $user,
            ),
        ]);
    }

    public function reopen(
        ReopenDailyClosingRequest $request,
        DailyClosing $dailyClosing,
        DailyClosingService $closings,
    ): JsonResponse {
        /** @var TenantUser $user */
        $user = $request->user();

        return response()->json([
            'data' => $closings->reopen(
                $dailyClosing,
                $user,
                $request->string('reason')->toString(),
            ),
        ]);
    }
}
