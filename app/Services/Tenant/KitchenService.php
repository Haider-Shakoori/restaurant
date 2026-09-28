<?php

namespace App\Services\Tenant;

use App\Models\KitchenStation;
use App\Models\KitchenTicket;
use App\Models\MenuItemKitchenRoute;
use App\Models\Order;
use App\Models\TenantUser;
use Illuminate\Support\Collection;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;

class KitchenService
{
    public function dispatch(Order $order, TenantUser $actor): Collection
    {
        return DB::connection('tenant')->transaction(function () use ($order, $actor): Collection {
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
                    'order' => 'Only submitted kitchen-bound orders can create KOT tickets.',
                ]);
            }

            if ($order->kitchenTickets()->exists()) {
                return $order->kitchenTickets()
                    ->with(['station', 'items'])
                    ->orderBy('queued_at')
                    ->get();
            }

            $branchId = $order->table->diningArea->branch_id;
            $routes = MenuItemKitchenRoute::query()
                ->where('branch_id', $branchId)
                ->whereIn('menu_item_id', $order->items->pluck('menu_item_id')->filter())
                ->get()
                ->keyBy('menu_item_id');

            $general = null;
            $groups = [];

            foreach ($order->items as $item) {
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
                    'kitchen_station_id' => $stationId,
                    'submitted_by_user_id' => $actor->getKey(),
                    'ticket_number' => 'KOT-'.strtoupper((string) Str::ulid()),
                    'status' => KitchenTicket::STATUS_QUEUED,
                    'queued_at' => now(),
                ]);

                foreach ($items as $item) {
                    $ticket->items()->create([
                        'order_item_id' => $item->id,
                        'item_name' => $item->item_name,
                        'quantity' => $item->quantity,
                        'notes' => $item->notes,
                        'status' => KitchenTicket::STATUS_QUEUED,
                    ]);

                    $item->update(['status' => KitchenTicket::STATUS_QUEUED]);
                }

                $this->event($ticket, $actor, 'kot.queued', null, KitchenTicket::STATUS_QUEUED);
                $tickets->push($ticket->load(['station', 'items']));
            }

            return $tickets;
        });
    }

    public function start(KitchenTicket $ticket, TenantUser $actor): KitchenTicket
    {
        return $this->transition(
            $ticket,
            $actor,
            [KitchenTicket::STATUS_QUEUED],
            KitchenTicket::STATUS_PREPARING,
            'kot.preparing',
        );
    }

    public function ready(KitchenTicket $ticket, TenantUser $actor): KitchenTicket
    {
        return $this->transition(
            $ticket,
            $actor,
            [KitchenTicket::STATUS_QUEUED, KitchenTicket::STATUS_PREPARING],
            KitchenTicket::STATUS_READY,
            'kot.ready',
        );
    }

    public function serve(Order $order, TenantUser $actor): Order
    {
        return DB::connection('tenant')->transaction(function () use ($order, $actor): Order {
            $order = Order::query()
                ->with(['kitchenTickets', 'items'])
                ->lockForUpdate()
                ->findOrFail($order->getKey());

            if ($order->status === Order::STATUS_SERVED) {
                return $order;
            }

            if ($order->status !== Order::STATUS_READY) {
                throw ValidationException::withMessages([
                    'order' => 'The order can only be served after every kitchen ticket is ready.',
                ]);
            }

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

            return $order->fresh()->load(['table.diningArea', 'waiter', 'items', 'kitchenTickets.station']);
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
                ->with('order')
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
                $timestamps['started_at'] = $ticket->started_at ?? now();
                $timestamps['ready_at'] = now();
            }

            $ticket->update([
                'status' => $to,
                ...$timestamps,
            ]);

            $ticket->items()->update(['status' => $to]);

            $this->event($ticket, $actor, $eventType, $from, $to);
            $this->synchronizeOrderStatus($ticket->order, $actor);

            return $ticket->fresh()->load(['station', 'items', 'order.table.diningArea']);
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

        if ($statuses->contains(KitchenTicket::STATUS_PREPARING) && $order->status === Order::STATUS_SUBMITTED) {
            $order->update(['status' => Order::STATUS_PREPARING]);
            $order->events()->create([
                'actor_user_id' => $actor->getKey(),
                'event_type' => 'order.preparing',
                'from_status' => Order::STATUS_SUBMITTED,
                'to_status' => Order::STATUS_PREPARING,
                'occurred_at' => now(),
            ]);
        }
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
