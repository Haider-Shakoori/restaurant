<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\DiningTable;
use Illuminate\Http\JsonResponse;

class DiningTableController extends Controller
{
    public function index(): JsonResponse
    {
        $tables = DiningTable::query()
            ->with(['diningArea.branch'])
            ->where('is_active', true)
            ->whereHas('diningArea', fn ($query) => $query->where('is_active', true)
                ->whereHas('branch', fn ($branches) => $branches->where('is_active', true)))
            ->orderBy('code')
            ->get()
            ->map(fn (DiningTable $table) => [
                'id' => $table->id,
                'code' => $table->code,
                'name' => $table->name,
                'capacity' => $table->capacity,
                'status' => $table->status,
                'area' => [
                    'id' => $table->diningArea->id,
                    'name' => $table->diningArea->name,
                ],
                'branch' => [
                    'id' => $table->diningArea->branch->id,
                    'name' => $table->diningArea->branch->name,
                ],
            ]);

        return response()->json(['data' => $tables]);
    }
}
