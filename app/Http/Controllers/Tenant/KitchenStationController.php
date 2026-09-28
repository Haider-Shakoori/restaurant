<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\StoreKitchenStationRequest;
use App\Models\KitchenStation;
use Illuminate\Http\JsonResponse;

class KitchenStationController extends Controller
{
    public function index(): JsonResponse
    {
        $stations = KitchenStation::query()
            ->with('branch')
            ->where('is_active', true)
            ->orderBy('branch_id')
            ->orderBy('sort_order')
            ->orderBy('name')
            ->get();

        return response()->json(['data' => $stations]);
    }

    public function store(StoreKitchenStationRequest $request): JsonResponse
    {
        $data = $request->validated();

        $station = KitchenStation::query()->create([
            'branch_id' => $data['branch_id'],
            'code' => strtoupper($data['code']),
            'name' => $data['name'],
            'sort_order' => $data['sort_order'] ?? 0,
            'is_active' => true,
        ]);

        return response()->json(['data' => $station->load('branch')], 201);
    }
}
