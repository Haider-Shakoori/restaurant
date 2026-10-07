<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\KitchenTicket;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class KitchenTicketController extends Controller
{
    public function index(Request $request): JsonResponse
    {
        $tickets = KitchenTicket::query()
            ->with([
                'round',
                'station',
                'items',
                'order.table.diningArea.branch',
                'order.waiter',
            ])
            ->when(
                $request->filled('station_id'),
                fn ($query) => $query->where('kitchen_station_id', $request->string('station_id')->toString()),
            )
            ->when(
                $request->filled('status'),
                fn ($query) => $query->where('status', $request->string('status')->toString()),
                fn ($query) => $query->whereIn('status', [
                    KitchenTicket::STATUS_ACTIVE,
                    KitchenTicket::STATUS_QUEUED,
                    KitchenTicket::STATUS_PREPARING,
                    KitchenTicket::STATUS_READY,
                ]),
            )
            ->orderByRaw("CASE status WHEN 'preparing' THEN 0 WHEN 'queued' THEN 1 WHEN 'active' THEN 1 WHEN 'ready' THEN 2 ELSE 3 END")
            ->orderBy('queued_at')
            ->limit(200)
            ->get();

        return response()->json(['data' => $tickets]);
    }

    public function show(KitchenTicket $kitchenTicket): JsonResponse
    {
        return response()->json([
            'data' => $kitchenTicket->load([
                'round',
                'station.branch',
                'items.orderItem',
                'events.actor',
                'order.table.diningArea',
                'order.waiter',
            ]),
        ]);
    }
}
