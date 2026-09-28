<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\AdjustInventoryRequest;
use App\Http\Requests\Tenant\StoreInventoryItemRequest;
use App\Models\InventoryItem;
use App\Models\RestaurantBranch;
use App\Models\TenantUser;
use App\Services\Tenant\InventoryService;
use App\Support\Quantity;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class InventoryItemController extends Controller
{
    public function index(Request $request): JsonResponse
    {
        $branchId = $request->string('branch_id')->toString();

        $items = InventoryItem::query()
            ->where('is_active', true)
            ->with(['balances' => fn ($query) => $branchId !== ''
                ? $query->where('branch_id', $branchId)
                : $query])
            ->orderBy('name')
            ->get()
            ->map(function (InventoryItem $item) use ($branchId): array {
                $balance = $branchId !== '' ? $item->balances->first() : null;
                $quantity = $balance?->quantity ?? '0.0000';

                return [
                    'id' => $item->id,
                    'sku' => $item->sku,
                    'name' => $item->name,
                    'base_unit' => $item->base_unit,
                    'purchase_unit' => $item->purchase_unit ?: $item->base_unit,
                    'purchase_to_base_factor' => $item->purchase_to_base_factor,
                    'reorder_level' => $item->reorder_level,
                    'quantity' => $branchId !== '' ? $quantity : null,
                    'low_stock' => $branchId !== ''
                        ? Quantity::toScaled((string) $quantity) <= Quantity::toScaled((string) $item->reorder_level)
                        : null,
                ];
            });

        return response()->json(['data' => $items]);
    }

    public function store(StoreInventoryItemRequest $request): JsonResponse
    {
        $data = $request->validated();

        $item = InventoryItem::query()->create([
            'sku' => strtoupper($data['sku']),
            'name' => $data['name'],
            'base_unit' => strtolower($data['base_unit']),
            'purchase_unit' => isset($data['purchase_unit']) ? strtolower($data['purchase_unit']) : null,
            'purchase_to_base_factor' => $data['purchase_to_base_factor'] ?? '1.000000',
            'reorder_level' => $data['reorder_level'] ?? '0.0000',
            'is_active' => true,
        ]);

        return response()->json(['data' => $item], 201);
    }

    public function adjust(
        AdjustInventoryRequest $request,
        InventoryItem $inventoryItem,
        InventoryService $inventory,
    ): JsonResponse {
        /** @var TenantUser $user */
        $user = $request->user();

        $branch = RestaurantBranch::query()->findOrFail(
            $request->string('branch_id')->toString()
        );

        return response()->json([
            'data' => $inventory->adjust(
                $branch,
                $inventoryItem,
                $user,
                (string) $request->input('quantity_delta'),
                $request->string('client_adjustment_id')->toString(),
                $request->string('reason')->toString(),
            ),
        ]);
    }
}
