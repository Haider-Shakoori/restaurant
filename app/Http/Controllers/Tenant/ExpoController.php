<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\KotDispatchRound;
use App\Models\Order;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class ExpoController extends Controller
{
    public function __invoke(Request $request): JsonResponse
    {
        $rounds = KotDispatchRound::query()
            ->with([
                'order.table.diningArea',
                'order.waiter',
                'tickets.station',
                'tickets.items',
            ])
            ->whereHas('order', fn ($query) => $query->whereIn('status', Order::ACTIVE_STATUSES))
            ->latest('sent_at')
            ->limit(100)
            ->get()
            ->filter(fn (KotDispatchRound $round) => (bool) data_get($round->workflow_snapshot, 'expo_enabled', false))
            ->map(function (KotDispatchRound $round): array {
                $items = $round->tickets->flatMap->items;
                $relevant = $items->reject(fn ($item) => in_array($item->status, ['voided', 'cancelled'], true));
                $ready = $relevant->filter(fn ($item) => in_array($item->status, ['ready', 'completed'], true));

                return [
                    'round_id' => $round->id,
                    'order_id' => $round->order_id,
                    'sequence' => $round->sequence,
                    'kot_number' => $round->kot_number,
                    'priority' => $round->priority,
                    'sent_at' => $round->sent_at?->toIso8601String(),
                    'item_count' => $relevant->count(),
                    'ready_count' => $ready->count(),
                    'ready_to_serve' => $relevant->isNotEmpty() && $ready->count() === $relevant->count(),
                    'order' => [
                        'status' => $round->order->status,
                        'service_type' => $round->order->service_type,
                        'service_reference' => $round->order->service_reference,
                        'table' => $round->order->table?->name,
                        'waiter' => $round->order->waiter?->name,
                    ],
                    'stations' => $round->tickets->map(fn ($ticket) => [
                        'id' => $ticket->station->id,
                        'name' => $ticket->station->name,
                        'status' => $ticket->status,
                        'items' => $ticket->items,
                    ])->values(),
                ];
            })
            ->values();

        return response()->json(['data' => $rounds]);
    }
}
