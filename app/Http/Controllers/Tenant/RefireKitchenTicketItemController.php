<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\KitchenTicketItem;
use App\Models\TenantUser;
use App\Services\Tenant\KitchenService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Validation\ValidationException;

class RefireKitchenTicketItemController extends Controller
{
    public function __invoke(Request $request, KitchenTicketItem $kitchenTicketItem, KitchenService $kitchen): JsonResponse
    {
        $data = $request->validate([
            'reason' => ['required', 'string', 'max:1000'],
        ]);

        $operationId = $request->header('Idempotency-Key');

        if (! filled($operationId)) {
            throw ValidationException::withMessages([
                'idempotency_key' => 'Idempotency-Key is required for a re-fire.',
            ]);
        }

        /** @var TenantUser $user */
        $user = $request->user();

        return response()->json([
            'data' => $kitchen->refireItem($kitchenTicketItem, $user, $data['reason'], (string) $operationId),
        ], 201);
    }
}
