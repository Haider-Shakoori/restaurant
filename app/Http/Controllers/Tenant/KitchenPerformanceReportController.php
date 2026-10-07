<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Services\Tenant\KitchenPerformanceService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class KitchenPerformanceReportController extends Controller
{
    public function __invoke(
        Request $request,
        KitchenPerformanceService $reports,
    ): JsonResponse {
        $data = $request->validate([
            'branch_id' => ['nullable', 'string', 'exists:branches,id'],
            'from' => ['nullable', 'date_format:Y-m-d'],
            'to' => ['nullable', 'date_format:Y-m-d'],
        ]);

        return response()->json([
            'data' => $reports->summary(
                $data['branch_id'] ?? null,
                $data['from'] ?? now()->startOfMonth()->toDateString(),
                $data['to'] ?? now()->toDateString(),
            ),
        ]);
    }
}
