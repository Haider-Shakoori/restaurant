<?php

namespace App\Services\Tenant;

use App\Models\DiningTable;
use App\Models\MenuItem;
use App\Models\Order;
use App\Models\OrderItem;
use App\Models\TenantUser;
use Illuminate\Support\Facades\DB;
use Illuminate\Validation\ValidationException;

class OrderService
{
    public function __construct(
        private readonly KitchenService $kitchen,
    ) {}

    public function take(TenantUser $actor, array $data): Order
    {
        return DB::connection('tenant')->transaction(function () use ($actor, $data): Order {
            $order = $this->open($actor, [
                'client_order_id' => $data['client_order_id'] ?? null,
                'dining_table_id' => $data['dining_table_id'],
                'guest_count' => $data['guest_count'] ?? 1,
                'notes' => $data['notes'] ?? null,
            ]);

            foreach ($data['lines'] as $line) {
                $this->addItem($order, $actor, [
                    'client_line_id' => $line['client_line_id'] ?? null,
                    'menu_item_id' => $line['menu_item_id'],
                    'quantity' => $line['quantity'],
                    'notes' => $line['notes'] ?? null,
                ]);
            }

            if (($data['submit_action'] ?? 'kitchen') === 'kitchen') {
                return $this->submit($order, $actor);
            }

            return $order->fresh()->load([
                'table.diningArea',
                'waiter',
                'items',
                'kotRounds.tickets.station',
            ]);
        });
    }

    public function open(TenantUser $waiter, array $data): Order
    {
        return DB::connection('tenant')->transaction(function () use ($waiter, $data): Order {
            if (! empty($data['client_order_id'])) {
                $existing = Order::query()->where('client_order_id', $data['client_order_id'])->first();

                if ($existing) {
                    return $existing->load(['table.diningArea', 'items', 'kotRounds.tickets.station']);
                }
            }

            $table = DiningTable::query()->lockForUpdate()->findOrFail($data['dining_table_id']);

            if (! $table->is_active || in_array($table->status, [
                DiningTable::STATUS_DISABLED,
                DiningTable::STATUS_RESERVED,
            ], true)) {
                throw ValidationException::withMessages([
                    'dining_table_id' => 'This table is not currently available for walk-in ordering.',
                ]);
            }

            $activeOrder = Order::query()
                ->where('dining_table_id', $table->id)
                ->whereIn('status', Order::ACTIVE_STATUSES)
                ->lockForUpdate()
                ->first();

            if ($activeOrder) {
                throw ValidationException::withMessages([
                    'dining_table_id' => 'This table already has an active order.',
                ]);
            }

            $order = Order::query()->create([
                'client_order_id' => $data['client_order_id'] ?? null,
                'dining_table_id' => $table->id,
                'waiter_id' => $waiter->getKey(),
                'status' => Order::STATUS_DRAFT,
                'guest_count' => $data['guest_count'] ?? 1,
                'notes' => $data['notes'] ?? null,
                'subtotal' => '0.00',
                'total' => '0.00',
                'opened_at' => now(),
            ]);

            $table->update(['status' => DiningTable::STATUS_OCCUPIED]);

            $this->event($order, $waiter, 'order.opened', null, Order::STATUS_DRAFT, [
                'table_id' => $table->id,
            ]);

            return $order->load(['table.diningArea', 'items']);
        });
    }

    public function addItem(Order $order, TenantUser $actor, array $data): OrderItem
    {
        return DB::connection('tenant')->transaction(function () use ($order, $actor, $data): OrderItem {
            $order = Order::query()->lockForUpdate()->findOrFail($order->getKey());

            if (! in_array($order->status, Order::EDITABLE_STATUSES, true)) {
                throw ValidationException::withMessages([
                    'order' => 'New items cannot be added after the order is financially closed or cancelled.',
                ]);
            }

            if (! empty($data['client_line_id'])) {
                $existing = $order->items()
                    ->where('client_line_id', $data['client_line_id'])
                    ->first();

                if ($existing) {
                    return $existing;
                }
            }

            $menuItem = MenuItem::query()
                ->whereKey($data['menu_item_id'])
                ->where('is_available', true)
                ->first();

            if (! $menuItem) {
                throw ValidationException::withMessages([
                    'menu_item_id' => 'The selected menu item is unavailable.',
                ]);
            }

            $quantity = (int) $data['quantity'];
            $unitPrice = (string) $menuItem->price;
            $lineTotal = $this->multiplyMoney($unitPrice, $quantity);

            $line = $order->items()->create([
                'menu_item_id' => $menuItem->id,
                'client_line_id' => $data['client_line_id'] ?? null,
                'item_name' => $menuItem->name,
                'unit_price' => $unitPrice,
                'quantity' => $quantity,
                'dispatched_quantity' => 0,
                'line_total' => $lineTotal,
                'notes' => $data['notes'] ?? null,
                'status' => 'pending',
            ]);

            $subtotal = (string) $order->items()->sum('line_total');
            $order->update([
                'subtotal' => $subtotal,
                'total' => $subtotal,
            ]);

            $this->event($order, $actor, 'order.item_added', $order->status, $order->status, [
                'order_item_id' => $line->id,
                'menu_item_id' => $menuItem->id,
                'quantity' => $quantity,
                'unsent_quantity' => $quantity,
            ]);

            return $line->fresh();
        });
    }

    public function submit(
        Order $order,
        TenantUser $actor,
        ?string $mutationId = null,
        string $priority = 'normal',
    ): Order {
        return DB::connection('tenant')->transaction(function () use ($order, $actor, $mutationId, $priority): Order {
            $order = Order::query()
                ->withCount('items')
                ->lockForUpdate()
                ->findOrFail($order->getKey());

            if (! in_array($order->status, Order::EDITABLE_STATUSES, true)) {
                throw ValidationException::withMessages([
                    'order' => 'This order can no longer send kitchen production.',
                ]);
            }

            if ($order->items_count < 1) {
                throw ValidationException::withMessages([
                    'order' => 'Add at least one item before submitting the order.',
                ]);
            }

            $hasUnsent = $order->items()
                ->whereColumn('dispatched_quantity', '<', 'quantity')
                ->exists();

            if ($hasUnsent && in_array($order->status, [
                Order::STATUS_DRAFT,
                Order::STATUS_READY,
                Order::STATUS_SERVED,
            ], true)) {
                $from = $order->status;
                $event = $from === Order::STATUS_DRAFT ? 'order.submitted' : 'order.reopened_for_kitchen';

                $order->update([
                    'status' => Order::STATUS_SUBMITTED,
                    'submitted_at' => $order->submitted_at ?? now(),
                ]);

                $this->event($order, $actor, $event, $from, Order::STATUS_SUBMITTED);
            }

            $this->kitchen->dispatch($order->fresh(), $actor, $mutationId, $priority);

            return $order->fresh()->load([
                'table.diningArea',
                'waiter',
                'items',
                'kotRounds.tickets.station',
                'kotRounds.tickets.items',
                'kitchenTickets.station',
            ]);
        });
    }

    private function event(
        Order $order,
        TenantUser $actor,
        string $eventType,
        ?string $fromStatus,
        ?string $toStatus,
        array $payload = [],
    ): void {
        $order->events()->create([
            'actor_user_id' => $actor->getKey(),
            'event_type' => $eventType,
            'from_status' => $fromStatus,
            'to_status' => $toStatus,
            'payload' => $payload ?: null,
            'occurred_at' => now(),
        ]);
    }

    private function multiplyMoney(string $amount, int $quantity): string
    {
        [$whole, $fraction] = array_pad(explode('.', $amount, 2), 2, '0');
        $fraction = substr(str_pad($fraction, 2, '0'), 0, 2);
        $minor = ((int) $whole * 100) + (int) $fraction;
        $total = $minor * $quantity;

        return intdiv($total, 100).'.'.str_pad((string) ($total % 100), 2, '0', STR_PAD_LEFT);
    }
}
