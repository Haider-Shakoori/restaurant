<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\KitchenTicket;
use App\Models\TenantUser;
use App\Services\Tenant\KitchenService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class ReadyKitchenTicketController extends Controller
{
    public function __invoke(
        Request $request,
        KitchenTicket $kitchenTicket,
        KitchenService $kitchen,
    ): JsonResponse {
        /** @var TenantUser $user */
        $user = $request->user();

        return response()->json([
            'data' => $kitchen->ready($kitchenTicket, $user),
        ]);
    }
}
