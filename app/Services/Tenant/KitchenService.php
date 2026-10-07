<?php

namespace App\Services\Tenant;

use App\Models\KitchenStation;
use App\Models\KitchenTicket;
use App\Models\KitchenTicketItem;
use App\Models\KotRound;
use App\Models\MenuItemKitchenRoute;
use App\Models\Order;
use App\Models\OrderItem;
use App\Models\TenantUser;
use Illuminate\Support\Collection;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;

class KitchenService
{
    public function __construct(
        private readonly InventoryService $inventory,
    ) {}

    public function dispatch(
        Order $order,
        TenantUser $actor,
        ?string $clientDispatchId = null,
    ): Collection {
        return DB::connection('tenant')->transaction(function () use ($order, $actor, $clientDispatchId): Collection {
            $order = Order::query()
                ->with(['table.diningArea.branch', 'items'])
                ->lockForUpdate()
                ->findOrFail($order->getKey());

            if (! in_array($order->status, [
                Order::STATUS_SUBMITTED,
                Order::STATUS_PREPARING,
                Order::STATUS_READY,
            ], true)) {
                throw ValidationException::withMessages([
                    'order' => 'Only active kitchen-bound orders can create KOT tickets.',
                ]);
            }

            if ($clientDispatchId) {
                $existingRound = $order->kotRounds()
                    ->where('client_dispatch_id', $clientDispatchId)
                    ->first();

                if ($existingRound) {
                    return $existingRound->tickets()
                        ->with(['round', 'station', 'items'])
                        ->orderBy('queued_at')
                        ->get();
                }
            }

            $pendingItems = $order->items
                ->filter(fn (OrderItem $item): bool => $item->pendingDispatchQuantity() > 0)
                ->values();

            if ($pendingItems->isEmpty()) {
                $latestRound = $order->kotRounds()->latest('round_number')->first();

                return $latestRound
                    ? $latestRound->tickets()->with(['round', 'station', 'items'])->get()
                    : collect();
            }

            $branchId = $order->table->diningArea->branch_id;
            $businessDate = now()->toDateString();
            $roundNumber = ((int) $order->kotRounds()->max('round_number')) + 1;
            $displayNumber = $this->nextDisplayNumber($branchId, $businessDate);

            $round = $order->kotRounds()->create([
                'branch_id' => $branchId,
                'submitted_by_user_id' => $actor->getKey(),
                'round_number' => $roundNumber,
                'display_number' => $displayNumber,
                'business_date' => $businessDate,
                'client_dispatch_id' => $clientDispatchId,
                'dispatched_at' => now(),
            ]);

            $routes = MenuItemKitchenRoute::query()
                ->where('branch_id', $branchId)
                ->whereIn('menu_item_id', $pendingItems->pluck('menu_item_id')->filter())
                ->get()
                ->keyBy('menu_item_id');

            $general = null;
            $groups = [];

            foreach ($pendingItems as $item) {
                $route = $item->menu_item_id ? $routes->get($item->menu_item_id) : null;
                $stationId = $route?->kitchen_station_id;

                if (! $stationId) {
                    $general ??= $this->generalStation($branchId);
                    $stationId = $general->id;
                }

                $groups[$stationId][] = $item;
            }

            $stations = KitchenStation::query()
                ->whereIn('id', array_keys($groups))
                ->get()
                ->keyBy('id');

            $tickets = collect();
            $dateToken = str_replace('-', '', $businessDate);
            $branchToken = substr(strtoupper(preg_replace('/[^A-Z0-9]+/i', '', (string) $order->table->diningArea->branch->code) ?: 'BR'), 0, 8);
            $numberToken = str_pad((string) $displayNumber, 4, '0', STR_PAD_LEFT);

            foreach ($groups as $stationId => $items) {
                $station = $stations->get($stationId) ?? KitchenStation::query()->findOrFail($stationId);
                $stationToken = substr(strtoupper(preg_replace('/[^A-Z0-9]+/i', '', (string) $station->code) ?: 'STATION'), 0, 12);

                $ticket = $order->kitchenTickets()->create([
                    'kot_round_id' => $round->id,
                    'kitchen_station_id' => $stationId,
                    'submitted_by_user_id' => $actor->getKey(),
                    'ticket_number' => "KOT-{$dateToken}-{$branchToken}-{$numberToken}-{$stationToken}",
                    'status' => KitchenTicket::STATUS_QUEUED,
                    'queued_at' => now(),
                ]);

                foreach ($items as $item) {
                    $dispatchQuantity = $item->pendingDispatchQuantity();

                    if ($dispatchQuantity < 1) {
                        continue;
                    }

                    $ticket->items()->create([
                        'order_item_id' => $item->id,
                        'item_name' => $item->item_name,
                        'quantity' => $dispatchQuantity,
                        'notes' => $item->notes,
                        'status' => KitchenTicket::STATUS_QUEUED,
                    ]);

                    $item->update([
                        'dispatched_quantity' => (int) $item->dispatched_quantity + $dispatchQuantity,
                        'status' => KitchenTicket::STATUS_QUEUED,
                    ]);
                }

                $this->event($ticket, $actor, 'kot.queued', null, KitchenTicket::STATUS_QUEUED, [
                    'kot_round_id' => $round->id,
                    'kot_number' => $round->kot_number,
                    'round_number' => $round->round_number,
                ]);

                $tickets->push($ticket->load(['round', 'station', 'items']));
            }

            $order->events()->create([
                'actor_user_id' => $actor->getKey(),
                'event_type' => 'kot.round_created',
                'from_status' => $order->status,
                'to_status' => $order->status,
                'payload' => [
                    'kot_round_id' => $round->id,
                    'kot_number' => $round->kot_number,
                    'round_number' => $round->round_number,
                    'ticket_ids' => $tickets->pluck('id')->all(),
                ],
                'occurred_at' => now(),
            ]);

            return $tickets;
        });
    }

    public function start(KitchenTicket $ticket, TenantUser $actor): KitchenTicket
    {
        return $this->transitionTicketItems(
            $ticket,
            $actor,
            [KitchenTicket::STATUS_QUEUED],
            KitchenTicket::STATUS_PREPARING,
            'kot.item_preparing',
        );
    }

    public function ready(KitchenTicket $ticket, TenantUser $actor): KitchenTicket
    {
        return $this->transitionTicketItems(
            $ticket,
            $actor,
            [KitchenTicket::STATUS_QUEUED, KitchenTicket::STATUS_PREPARING],
            KitchenTicket::STATUS_READY,
            'kot.item_ready',
        );
    }

    public function startItem(KitchenTicketItem $item, TenantUser $actor): KitchenTicketItem
    {
        return $this->transitionItem(
            $item,
            $actor,
            [KitchenTicket::STATUS_QUEUED],
            KitchenTicket::STATUS_PREPARING,
            'kot.item_preparing',
        );
    }

    public function readyItem(KitchenTicketItem $item, TenantUser $actor): KitchenTicketItem
    {
        return $this->transitionItem(
            $item,
            $actor,
            [KitchenTicket::STATUS_QUEUED, KitchenTicket::STATUS_PREPARING],
            KitchenTicket::STATUS_READY,
            'kot.item_ready',
        );
    }

    public function serve(Order $order, TenantUser $actor): Order
    {
        return DB::connection('tenant')->transaction(function () use ($order, $actor): Order {
            $order = Order::query()
                ->with(['kitchenTickets.items', 'items', 'table.diningArea.branch'])
                ->lockForUpdate()
                ->findOrFail($order->getKey());

            if ($order->items->contains(fn (OrderItem $item): bool => $item->pendingDispatchQuantity() > 0)) {
                throw ValidationException::withMessages([
                    'order' => 'Send all pending order items to the kitchen before serving the order.',
                ]);
            }

            if ($order->status === Order::STATUS_SERVED) {
                $this->inventory->consumeOrder($order, $actor);

                return $order;
            }

            if ($order->status !== Order::STATUS_READY) {
                throw ValidationException::withMessages([
                    'order' => 'The order can only be served after every kitchen item is ready.',
                ]);
            }

            $this->inventory->consumeOrder($order, $actor);

            $order->update([
                'status' => Order::STATUS_SERVED,
                'served_at' => now(),
            ]);

            $order->items()->update(['status' => 'served']);

            foreach ($order->kitchenTickets as $ticket) {
                $ticketFrom = $ticket->status;

                foreach ($ticket->items as $item) {
                    if ($item->status !== KitchenTicket::STATUS_COMPLETED) {
                        $itemFrom = $item->status;
                        $item->update([
                            'status' => KitchenTicket::STATUS_COMPLETED,
                            'completed_at' => now(),
                        ]);
                        $this->itemEvent($item, $actor, 'kot.item_completed', $itemFrom, KitchenTicket::STATUS_COMPLETED);
                    }
                }

                if ($ticket->status !== KitchenTicket::STATUS_COMPLETED) {
                    $ticket->update([
                        'status' => KitchenTicket::STATUS_COMPLETED,
                        'completed_at' => now(),
                    ]);
                    $this->event($ticket, $actor, 'kot.completed', $ticketFrom, KitchenTicket::STATUS_COMPLETED);
                }
            }

            $order->events()->create([
                'actor_user_id' => $actor->getKey(),
                'event_type' => 'order.served',
                'from_status' => Order::STATUS_READY,
                'to_status' => Order::STATUS_SERVED,
                'occurred_at' => now(),
            ]);

            return $order->fresh()->load([
                'table.diningArea',
                'waiter',
                'items',
                'kotRounds.tickets.station',
                'kitchenTickets.station',
                'inventoryConsumption.lines.stockMovement.item',
            ]);
        });
    }

    private function transitionTicketItems(
        KitchenTicket $ticket,
        TenantUser $actor,
        array $allowedFrom,
        string $to,
        string $itemEventType,
    ): KitchenTicket {
        return DB::connection('tenant')->transaction(function () use ($ticket, $actor, $allowedFrom, $to, $itemEventType): KitchenTicket {
            $ticket = KitchenTicket::query()
                ->with(['items', 'order'])
                ->lockForUpdate()
                ->findOrFail($ticket->getKey());

            foreach ($ticket->items as $item) {
                if ($item->status === $to || ! in_array($item->status, $allowedFrom, true)) {
                    continue;
                }

                $from = $item->status;
                $timestamps = $this->itemTimestamps($item, $to);
                $item->update(['status' => $to, ...$timestamps]);
                $this->itemEvent($item, $actor, $itemEventType, $from, $to);
            }

            $this->synchronizeTicketStatus($ticket, $actor);
            $this->synchronizeOrderStatus($ticket->order, $actor);

            return $ticket->fresh()->load(['round', 'station', 'items', 'order.table.diningArea']);
        });
    }

    private function transitionItem(
        KitchenTicketItem $item,
        TenantUser $actor,
        array $allowedFrom,
        string $to,
        string $eventType,
    ): KitchenTicketItem {
        return DB::connection('tenant')->transaction(function () use ($item, $actor, $allowedFrom, $to, $eventType): KitchenTicketItem {
            $item = KitchenTicketItem::query()
                ->with('ticket.order')
                ->lockForUpdate()
                ->findOrFail($item->getKey());

            if ($item->status === $to) {
                return $item->load(['ticket.round', 'ticket.station', 'orderItem']);
            }

            if (! in_array($item->status, $allowedFrom, true)) {
                throw ValidationException::withMessages([
                    'ticket_item' => "Kitchen item cannot move from {$item->status} to {$to}.",
                ]);
            }

            $from = $item->status;
            $item->update([
                'status' => $to,
                ...$this->itemTimestamps($item, $to),
            ]);

            $this->itemEvent($item, $actor, $eventType, $from, $to);
            $this->synchronizeTicketStatus($item->ticket, $actor);
            $this->synchronizeOrderStatus($item->ticket->order, $actor);

            return $item->fresh()->load(['ticket.round', 'ticket.station', 'orderItem']);
        });
    }

    private function synchronizeTicketStatus(KitchenTicket $ticket, TenantUser $actor): void
    {
        $ticket = KitchenTicket::query()->with('items')->findOrFail($ticket->getKey());
        $statuses = $ticket->items->pluck('status');

        if ($statuses->isEmpty()) {
            return;
        }

        $target = KitchenTicket::STATUS_QUEUED;

        if ($statuses->every(fn (string $status): bool => in_array($status, [
            KitchenTicket::STATUS_READY,
            KitchenTicket::STATUS_COMPLETED,
        ], true))) {
            $target = KitchenTicket::STATUS_READY;
        } elseif ($statuses->contains(fn (string $status): bool => in_array($status, [
            KitchenTicket::STATUS_PREPARING,
            KitchenTicket::STATUS_READY,
        ], true))) {
            $target = KitchenTicket::STATUS_PREPARING;
        }

        if ($ticket->status === $target) {
            return;
        }

        $from = $ticket->status;
        $timestamps = [];

        if ($target === KitchenTicket::STATUS_PREPARING) {
            $timestamps['started_at'] = $ticket->started_at ?? now();
        }

        if ($target === KitchenTicket::STATUS_READY) {
            $timestamps['started_at'] = $ticket->started_at ?? now();
            $timestamps['ready_at'] = now();
        }

        $ticket->update(['status' => $target, ...$timestamps]);
        $this->event($ticket, $actor, 'kot.'.$target, $from, $target);
    }

    private function synchronizeOrderStatus(Order $order, TenantUser $actor): void
    {
        $statuses = $order->kitchenTickets()->pluck('status');

        if ($statuses->isEmpty()) {
            return;
        }

        if ($statuses->every(fn (string $status): bool => in_array($status, [
            KitchenTicket::STATUS_READY,
            KitchenTicket::STATUS_COMPLETED,
        ], true))) {
            $target = Order::STATUS_READY;
            $eventType = 'order.ready';
        } elseif ($statuses->contains(fn (string $status): bool => in_array($status, [
            KitchenTicket::STATUS_PREPARING,
            KitchenTicket::STATUS_READY,
        ], true))) {
            $target = Order::STATUS_PREPARING;
            $eventType = 'order.preparing';
        } else {
            $target = Order::STATUS_SUBMITTED;
            $eventType = 'order.submitted';
        }

        if ($order->status === $target) {
            return;
        }

        $from = $order->status;
        $order->update(['status' => $target]);
        $order->events()->create([
            'actor_user_id' => $actor->getKey(),
            'event_type' => $eventType,
            'from_status' => $from,
            'to_status' => $target,
            'occurred_at' => now(),
        ]);
    }

    private function nextDisplayNumber(string $branchId, string $businessDate): int
    {
        DB::connection('tenant')->table('kot_number_sequences')->insertOrIgnore([
            'id' => (string) Str::ulid(),
            'branch_id' => $branchId,
            'business_date' => $businessDate,
            'last_number' => 0,
            'created_at' => now(),
            'updated_at' => now(),
        ]);

        $sequence = DB::connection('tenant')
            ->table('kot_number_sequences')
            ->where('branch_id', $branchId)
            ->where('business_date', $businessDate)
            ->lockForUpdate()
            ->first();

        $next = ((int) $sequence->last_number) + 1;

        DB::connection('tenant')
            ->table('kot_number_sequences')
            ->where('id', $sequence->id)
            ->update([
                'last_number' => $next,
                'updated_at' => now(),
            ]);

        return $next;
    }

    private function itemTimestamps(KitchenTicketItem $item, string $to): array
    {
        if ($to === KitchenTicket::STATUS_PREPARING) {
            return ['started_at' => $item->started_at ?? now()];
        }

        if ($to === KitchenTicket::STATUS_READY) {
            return [
                'started_at' => $item->started_at ?? now(),
                'ready_at' => now(),
            ];
        }

        return [];
    }

    private function generalStation(string $branchId): KitchenStation
    {
        return KitchenStation::query()->firstOrCreate(
            [
                'branch_id' => $branchId,
                'code' => 'GENERAL',
            ],
            [
                'name' => 'General Kitchen',
                'sort_order' => 999,
                'is_active' => true,
            ],
        );
    }

    private function event(
        KitchenTicket $ticket,
        TenantUser $actor,
        string $eventType,
        ?string $from,
        ?string $to,
        array $payload = [],
    ): void {
        $ticket->events()->create([
            'actor_user_id' => $actor->getKey(),
            'event_type' => $eventType,
            'from_status' => $from,
            'to_status' => $to,
            'payload' => $payload ?: null,
            'occurred_at' => now(),
        ]);
    }

    private function itemEvent(
        KitchenTicketItem $item,
        TenantUser $actor,
        string $eventType,
        ?string $from,
        ?string $to,
        array $payload = [],
    ): void {
        $item->events()->create([
            'actor_user_id' => $actor->getKey(),
            'event_type' => $eventType,
            'from_status' => $from,
            'to_status' => $to,
            'payload' => $payload ?: null,
            'occurred_at' => now(),
        ]);
    }
}
