<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\ChartAccount;
use App\Services\Tenant\ReportingService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class AccountingReportController extends Controller
{
    public function trialBalance(Request $request, ReportingService $reports): JsonResponse
    {
        [$branch, $from, $to] = $this->period($request);

        return response()->json([
            'data' => $reports->trialBalance($branch, $from, $to),
        ]);
    }

    public function incomeStatement(Request $request, ReportingService $reports): JsonResponse
    {
        [$branch, $from, $to] = $this->period($request);

        return response()->json([
            'data' => $reports->incomeStatement($branch, $from, $to),
        ]);
    }

    public function balanceSheet(Request $request, ReportingService $reports): JsonResponse
    {
        $data = $request->validate([
            'branch_id' => ['nullable', 'string', 'max:40'],
            'as_of' => ['nullable', 'date_format:Y-m-d'],
        ]);

        return response()->json([
            'data' => $reports->balanceSheet(
                $data['branch_id'] ?? null,
                $data['as_of'] ?? now()->format('Y-m-d'),
            ),
        ]);
    }

    public function receivables(Request $request, ReportingService $reports): JsonResponse
    {
        $data = $request->validate([
            'branch_id' => ['nullable', 'string', 'max:40'],
        ]);

        return response()->json([
            'data' => $reports->receivables($data['branch_id'] ?? null),
        ]);
    }

    public function payables(Request $request, ReportingService $reports): JsonResponse
    {
        $data = $request->validate([
            'branch_id' => ['nullable', 'string', 'max:40'],
            'as_of' => ['nullable', 'date_format:Y-m-d'],
        ]);

        return response()->json([
            'data' => $reports->payables(
                $data['branch_id'] ?? null,
                $data['as_of'] ?? now()->format('Y-m-d'),
            ),
        ]);
    }

    public function managementSummary(Request $request, ReportingService $reports): JsonResponse
    {
        [$branch, $from, $to] = $this->period($request);

        return response()->json([
            'data' => $reports->managementSummary($branch, $from, $to),
        ]);
    }

    public function ledger(
        Request $request,
        ChartAccount $chartAccount,
        ReportingService $reports,
    ): JsonResponse {
        [$branch, $from, $to] = $this->period($request);

        return response()->json([
            'data' => $reports->accountLedger($chartAccount, $branch, $from, $to),
        ]);
    }

    private function period(Request $request): array
    {
        $data = $request->validate([
            'branch_id' => ['nullable', 'string', 'max:40'],
            'from' => ['nullable', 'date_format:Y-m-d'],
            'to' => ['nullable', 'date_format:Y-m-d'],
        ]);

        return [
            $data['branch_id'] ?? null,
            $data['from'] ?? now()->startOfMonth()->format('Y-m-d'),
            $data['to'] ?? now()->format('Y-m-d'),
        ];
    }
}
