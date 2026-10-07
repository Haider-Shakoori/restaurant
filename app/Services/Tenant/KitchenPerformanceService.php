<?php

namespace App\Services\Tenant;

use App\Models\KitchenTicketItem;
use App\Models\KotDispatchRound;
use Carbon\CarbonImmutable;

class KitchenPerformanceService
{
    public function summary(
        ?string $branchId,
        string $from,
        string $to,
    ): array {
        $fromAt = CarbonImmutable::parse($from)->startOfDay();
        $toAt = CarbonImmutable::parse($to)->endOfDay();

        $rounds = KotDispatchRound::query()
            ->with('order')
            ->whereBetween('sent_at', [$fromAt, $toAt])
            ->when(
                $branchId,
                fn ($query) => $query->whereHas(
                    'order',
                    fn ($orders) => $orders->where('branch_id', $branchId),
                ),
            )
            ->get();

        $items = KitchenTicketItem::query()
            ->with([
                'ticket.station',
                'ticket.round',
                'ticket.order',
                'wasteEvents',
            ])
            ->whereHas(
                'ticket.round',
                fn ($query) => $query->whereBetween('sent_at', [$fromAt, $toAt]),
            )
            ->when(
                $branchId,
                fn ($query) => $query->whereHas(
                    'ticket.order',
                    fn ($orders) => $orders->where('branch_id', $branchId),
                ),
            )
            ->get();

        $prepDurations = $items
            ->filter(fn (KitchenTicketItem $item) => $item->started_at && $item->ready_at)
            ->map(fn (KitchenTicketItem $item) => $item->started_at->diffInSeconds($item->ready_at));

        $queueDurations = $items
            ->filter(fn (KitchenTicketItem $item) => $item->ticket?->round?->sent_at && $item->started_at)
            ->map(
                fn (KitchenTicketItem $item) => $item->ticket->round->sent_at
                    ->diffInSeconds($item->started_at),
            );

        $wasteQuantity = $items
            ->flatMap->wasteEvents
            ->sum('quantity');

        $stations = $items
            ->groupBy(fn (KitchenTicketItem $item) => $item->ticket?->station?->id ?? 'unknown')
            ->map(function ($stationItems): array {
                /** @var KitchenTicketItem|null $first */
                $first = $stationItems->first();
                $durations = $stationItems
                    ->filter(fn (KitchenTicketItem $item) => $item->started_at && $item->ready_at)
                    ->map(fn (KitchenTicketItem $item) => $item->started_at->diffInSeconds($item->ready_at));

                return [
                    'station_id' => $first?->ticket?->station?->id,
                    'station_name' => $first?->ticket?->station?->name ?? 'Unknown',
                    'production_items' => $stationItems->count(),
                    'ready_items' => $stationItems->whereIn('status', ['ready', 'completed'])->count(),
                    'voided_items' => $stationItems->where('status', 'voided')->count(),
                    'refires' => $stationItems->whereNotNull('refire_of_kitchen_ticket_item_id')->count(),
                    'waste_quantity' => $stationItems->flatMap->wasteEvents->sum('quantity'),
                    'average_prep_seconds' => $this->average($durations->all()),
                ];
            })
            ->values()
            ->sortByDesc('production_items')
            ->values()
            ->all();

        $serviceTypes = $rounds
            ->groupBy(fn (KotDispatchRound $round) => $round->order?->service_type ?? 'unknown')
            ->map(fn ($group, string $serviceType) => [
                'service_type' => $serviceType,
                'rounds' => $group->count(),
            ])
            ->values()
            ->all();

        return [
            'period' => [
                'from' => $fromAt->toDateString(),
                'to' => $toAt->toDateString(),
                'branch_id' => $branchId,
            ],
            'kot_rounds' => $rounds->count(),
            'production_items' => $items->count(),
            'ready_or_completed_items' => $items->whereIn('status', ['ready', 'completed'])->count(),
            'voided_items' => $items->where('status', 'voided')->count(),
            'cancelled_items' => $items->where('status', 'cancelled')->count(),
            'refires' => $items->whereNotNull('refire_of_kitchen_ticket_item_id')->count(),
            'recalls' => $items->whereNotNull('recalled_at')->count(),
            'waste_quantity' => (int) $wasteQuantity,
            'average_queue_seconds' => $this->average($queueDurations->all()),
            'average_prep_seconds' => $this->average($prepDurations->all()),
            'rush_rounds' => $rounds->where('priority', 'rush')->count(),
            'stations' => $stations,
            'service_types' => $serviceTypes,
        ];
    }

    private function average(array $values): ?int
    {
        if ($values === []) {
            return null;
        }

        return (int) round(array_sum($values) / count($values));
    }
}
