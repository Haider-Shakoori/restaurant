<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\StockMovement;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class StockMovementController extends Controller
{
    public function index(Request $request): JsonResponse
    {
        $movements = StockMovement::query()
            ->with(['branch', 'item', 'actor'])
            ->when(
                $request->filled('branch_id'),
                fn ($query) => $query->where('branch_id', $request->string('branch_id')->toString()),
            )
            ->when(
                $request->filled('inventory_item_id'),
                fn ($query) => $query->where('inventory_item_id', $request->string('inventory_item_id')->toString()),
            )
            ->latest('occurred_at')
            ->limit(250)
            ->get();

        return response()->json(['data' => $movements]);
    }
}
