<?php

namespace App\Services\Tenant;

use App\Models\KitchenStation;
use App\Models\KitchenTicket;
use App\Models\KotDispatchRound;
use App\Models\MenuItemKitchenRoute;
use App\Models\Order;
use App\Models\TenantUser;
use Illuminate\Support\Collection;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;

class KitchenService
{
    public function __construct(
        private readonly InventoryService $inventory,
        private readonly RestaurantSettingsService $settings,
        private readonly KotNumberService $kotNumbers,
    ) {}

    public function dispatch(
        Order $order,
        TenantUser $actor,
        ?string $mutationId = null,
        string $priority = 'normal',
    ): Collection {
        return DB::connection('tenant')->transaction(function () use ($order, $actor, $mutationId, $priority): Collection {
            $order = Order::query()
                ->with(['table.diningArea.branch', 'items'])
                ->lockForUpdate()
                ->findOrFail($order->getKey());

            if (! in_array($order->status, [
                Order::STATUS_SUBMITTED,
                Order::STATUS_PREPARING,
                Order::STATUS_READY,
                Order::STATUS_SERVED,
            ], true)) {
                throw ValidationException::withMessages([
                    'order' => 'Only operationally open kitchen-bound orders can create KOT rounds.',
                ]);
            }

            if ($mutationId !== null) {
                $existingRound = KotDispatchRound::query()
                    ->where('order_id', $order->id)
                    ->where('client_mutation_id', $mutationId)
                    ->first();

                if ($existingRound) {
                    return $existingRound->tickets()
                        ->with(['station', 'items'])
                        ->orderBy('queued_at')
                        ->get();
                }
            }

            $eligible = $order->items
                ->filter(fn ($item) => (int) $item->dispatched_quantity < (int) $item->quantity)
                ->values();

            if ($eligible->isEmpty()) {
                $latestRound = $order->kotRounds()->latest('sequence')->first();

                return $latestRound
                    ? $latestRound->tickets()->with(['station', 'items'])->orderBy('queued_at')->get()
                    : collect();
            }

            if (! in_array($priority, ['normal', 'rush'], true)) {
                throw ValidationException::withMessages([
                    'priority' => 'KOT priority must be normal or rush.',
                ]);
            }

            $branchId = $order->table->diningArea->branch_id;
            $workflow = $this->settings->all($branchId);
            $initialState = $workflow['kitchen_queue_enabled']
                ? KitchenTicket::STATUS_QUEUED
                : KitchenTicket::STATUS_ACTIVE;

            $round = KotDispatchRound::query()->create([
                'order_id' => $order->id,
                'sequence' => ((int) $order->kotRounds()->max('sequence')) + 1,
                'submitted_by_user_id' => $actor->getKey(),
                'client_mutation_id' => $mutationId,
                'kot_number' => $this->kotNumbers->next($branchId),
                'priority' => $priority,
                'workflow_snapshot' => [
                    'kitchen_queue_enabled' => (bool) $workflow['kitchen_queue_enabled'],
                    'preparing_stage_enabled' => (bool) $workflow['preparing_stage_enabled'],
                    'expo_enabled' => (bool) $workflow['expo_enabled'],
                    'courses_enabled' => (bool) $workflow['courses_enabled'],
                    'kitchen_warning_minutes' => (int) $workflow['kitchen_warning_minutes'],
                    'kitchen_late_minutes' => (int) $workflow['kitchen_late_minutes'],
                ],
                'sent_at' => now(),
            ]);

            $routes = MenuItemKitchenRoute::query()
                ->where('branch_id', $branchId)
                ->whereIn('menu_item_id', $eligible->pluck('menu_item_id')->filter())
                ->get()
                ->keyBy('menu_item_id');

            $general = null;
            $groups = [];

            foreach ($eligible as $item) {
                $route = $item->menu_item_id ? $routes->get($item->menu_item_id) : null;
                $stationId = $route?->kitchen_station_id;

                if (! $stationId) {
                    $general ??= $this->generalStation($branchId);
                    $stationId = $general->id;
                }

                $groups[$stationId][] = $item;
            }

            $tickets = collect();

            foreach ($groups as $stationId => $items) {
                $ticket = $order->kitchenTickets()->create([
                    'kot_dispatch_round_id' => $round->id,
                    'kitchen_station_id' => $stationId,
                    'submitted_by_user_id' => $actor->getKey(),
                    'ticket_number' => 'KOT-'.strtoupper((string) Str::ulid()),
                    'human_kot_number' => $round->kot_number,
                    'status' => $initialState,
                    'queued_at' => now(),
                ]);

                foreach ($items as $item) {
                    $quantity = max(0, (int) $item->quantity - (int) $item->dispatched_quantity);

                    if ($quantity < 1) {
                        continue;
                    }

                    $ticket->items()->create([
                        'order_item_id' => $item->id,
                        'item_name' => $item->item_name,
                        'quantity' => $quantity,
                        'notes' => $item->notes,
                        'status' => $initialState,
                    ]);

                    $item->update([
                        'dispatched_quantity' => (int) $item->dispatched_quantity + $quantity,
                        'last_dispatched_at' => now(),
                        'status' => $initialState,
                    ]);
                }

                $this->event(
                    $ticket,
                    $actor,
                    $initialState === KitchenTicket::STATUS_QUEUED ? 'kot.queued' : 'kot.active',
                    null,
                    $initialState,
                    [
                        'round_id' => $round->id,
                        'round_sequence' => $round->sequence,
                        'kot_number' => $round->kot_number,
                    ],
                );

                $tickets->push($ticket->load(['station', 'items']));
            }

            $order->events()->create([
                'actor_user_id' => $actor->getKey(),
                'event_type' => 'order.kot_round_sent',
                'from_status' => $order->status,
                'to_status' => $order->status,
                'payload' => [
                    'round_id' => $round->id,
                    'round_sequence' => $round->sequence,
                    'kot_number' => $round->kot_number,
                    'priority' => $round->priority,
                    'ticket_ids' => $tickets->pluck('id')->all(),
                ],
                'occurred_at' => now(),
            ]);

            return $tickets;
        });
    }

    public function start(KitchenTicket $ticket, TenantUser $actor): KitchenTicket
    {
        $workflow = $this->workflowForTicket($ticket);

        if (! $workflow['preparing_stage_enabled']) {
            throw ValidationException::withMessages([
                'ticket' => 'Preparing is disabled for this KOT round.',
            ]);
        }

        return $this->transition(
            $ticket,
            $actor,
            $workflow['kitchen_queue_enabled']
                ? [KitchenTicket::STATUS_QUEUED]
                : [KitchenTicket::STATUS_ACTIVE],
            KitchenTicket::STATUS_PREPARING,
            'kot.preparing',
        );
    }

    public function ready(KitchenTicket $ticket, TenantUser $actor): KitchenTicket
    {
        $workflow = $this->workflowForTicket($ticket);

        $allowed = $workflow['preparing_stage_enabled']
            ? [KitchenTicket::STATUS_PREPARING]
            : ($workflow['kitchen_queue_enabled']
                ? [KitchenTicket::STATUS_QUEUED]
                : [KitchenTicket::STATUS_ACTIVE]);

        return $this->transition(
            $ticket,
            $actor,
            $allowed,
            KitchenTicket::STATUS_READY,
            'kot.ready',
        );
    }

    public function serve(Order $order, TenantUser $actor): Order
    {
        return DB::connection('tenant')->transaction(function () use ($order, $actor): Order {
            $order = Order::query()
                ->with(['kitchenTickets', 'items', 'table.diningArea.branch'])
                ->lockForUpdate()
                ->findOrFail($order->getKey());

            if ($order->status === Order::STATUS_SERVED) {
                $this->inventory->consumeOrder($order, $actor);

                return $order;
            }

            if ($order->status !== Order::STATUS_READY) {
                throw ValidationException::withMessages([
                    'order' => 'The order can only be served after every kitchen ticket is ready.',
                ]);
            }

            $this->inventory->consumeOrder($order, $actor);

            $order->update([
                'status' => Order::STATUS_SERVED,
                'served_at' => now(),
            ]);

            $order->items()->update(['status' => 'served']);

            foreach ($order->kitchenTickets as $ticket) {
                if ($ticket->status === KitchenTicket::STATUS_READY) {
                    $from = $ticket->status;
                    $ticket->update([
                        'status' => KitchenTicket::STATUS_COMPLETED,
                        'completed_at' => now(),
                    ]);
                    $ticket->items()->update(['status' => KitchenTicket::STATUS_COMPLETED]);
                    $this->event($ticket, $actor, 'kot.completed', $from, KitchenTicket::STATUS_COMPLETED);
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

    private function transition(
        KitchenTicket $ticket,
        TenantUser $actor,
        array $allowedFrom,
        string $to,
        string $eventType,
    ): KitchenTicket {
        return DB::connection('tenant')->transaction(function () use ($ticket, $actor, $allowedFrom, $to, $eventType): KitchenTicket {
            $ticket = KitchenTicket::query()
                ->with(['order', 'round'])
                ->lockForUpdate()
                ->findOrFail($ticket->getKey());

            if ($ticket->status === $to) {
                return $ticket->load(['station', 'items', 'order.table']);
            }

            if (! in_array($ticket->status, $allowedFrom, true)) {
                throw ValidationException::withMessages([
                    'ticket' => "Kitchen ticket cannot move from {$ticket->status} to {$to}.",
                ]);
            }

            $from = $ticket->status;
            $timestamps = [];

            if ($to === KitchenTicket::STATUS_PREPARING) {
                $timestamps['started_at'] = now();
            }

            if ($to === KitchenTicket::STATUS_READY) {
                if ($from === KitchenTicket::STATUS_PREPARING) {
                    $timestamps['started_at'] = $ticket->started_at ?? now();
                }
                $timestamps['ready_at'] = now();
            }

            $ticket->update([
                'status' => $to,
                ...$timestamps,
            ]);

            $ticket->items()->update(['status' => $to]);

            $this->event($ticket, $actor, $eventType, $from, $to);
            $this->synchronizeOrderStatus($ticket->order, $actor);

            return $ticket->fresh()->load(['round', 'station', 'items', 'order.table.diningArea']);
        });
    }

    private function synchronizeOrderStatus(Order $order, TenantUser $actor): void
    {
        $statuses = $order->kitchenTickets()->pluck('status');

        if ($statuses->isEmpty()) {
            return;
        }

        if ($statuses->every(fn (string $status) => in_array($status, [
            KitchenTicket::STATUS_READY,
            KitchenTicket::STATUS_COMPLETED,
        ], true))) {
            if ($order->status !== Order::STATUS_READY) {
                $from = $order->status;
                $order->update(['status' => Order::STATUS_READY]);
                $order->events()->create([
                    'actor_user_id' => $actor->getKey(),
                    'event_type' => 'order.ready',
                    'from_status' => $from,
                    'to_status' => Order::STATUS_READY,
                    'occurred_at' => now(),
                ]);
            }

            return;
        }

        if ($statuses->contains(KitchenTicket::STATUS_PREPARING) && $order->status !== Order::STATUS_PREPARING) {
            $from = $order->status;
            $order->update(['status' => Order::STATUS_PREPARING]);
            $order->events()->create([
                'actor_user_id' => $actor->getKey(),
                'event_type' => 'order.preparing',
                'from_status' => $from,
                'to_status' => Order::STATUS_PREPARING,
                'occurred_at' => now(),
            ]);
        }
    }

    private function workflowForTicket(KitchenTicket $ticket): array
    {
        $ticket->loadMissing(['round', 'order.table.diningArea']);
        $snapshot = $ticket->round?->workflow_snapshot;

        if (is_array($snapshot)) {
            return [
                ...$this->settings->all($ticket->order->table->diningArea->branch_id),
                ...$snapshot,
            ];
        }

        return $this->settings->all($ticket->order->table->diningArea->branch_id);
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
}
