<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\KitchenTicketItem;
use App\Models\TenantUser;
use App\Services\Tenant\KitchenService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class RecallKitchenTicketItemController extends Controller
{
    public function __invoke(Request $request, KitchenTicketItem $kitchenTicketItem, KitchenService $kitchen): JsonResponse
    {
        $data = $request->validate([
            'reason' => ['required', 'string', 'max:1000'],
        ]);

        /** @var TenantUser $user */
        $user = $request->user();

        return response()->json([
            'data' => $kitchen->recallItem($kitchenTicketItem, $user, $data['reason']),
        ]);
    }
}
