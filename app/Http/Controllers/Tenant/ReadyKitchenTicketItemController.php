<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\KitchenTicketItem;
use App\Models\TenantUser;
use App\Services\Tenant\KitchenService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class ReadyKitchenTicketItemController extends Controller
{
    public function __invoke(
        Request $request,
        KitchenTicketItem $kitchenTicketItem,
        KitchenService $kitchen,
    ): JsonResponse {
        /** @var TenantUser $user */
        $user = $request->user();

        return response()->json([
            'data' => $kitchen->readyItem($kitchenTicketItem, $user),
        ]);
    }
}
