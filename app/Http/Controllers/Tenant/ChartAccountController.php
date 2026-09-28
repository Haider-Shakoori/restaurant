<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\StoreChartAccountRequest;
use App\Models\ChartAccount;
use App\Services\Tenant\AccountingService;
use Illuminate\Http\JsonResponse;
use Illuminate\Validation\ValidationException;

class ChartAccountController extends Controller
{
    public function index(AccountingService $accounting): JsonResponse
    {
        $accounting->ensureSystemAccounts();

        return response()->json([
            'data' => ChartAccount::query()
                ->where('is_active', true)
                ->orderBy('code')
                ->get(),
        ]);
    }

    public function store(StoreChartAccountRequest $request): JsonResponse
    {
        $data = $request->validated();
        $code = strtoupper($data['code']);

        if (ChartAccount::query()->where('code', $code)->exists()) {
            throw ValidationException::withMessages([
                'code' => 'This account code is already in use.',
            ]);
        }

        $account = ChartAccount::query()->create([
            'code' => $code,
            'name' => $data['name'],
            'type' => $data['type'],
            'normal_balance' => $data['normal_balance'],
            'is_contra' => $data['is_contra'] ?? false,
            'system_key' => null,
            'is_active' => true,
        ]);

        return response()->json(['data' => $account], 201);
    }
}
