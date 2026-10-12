<?php

namespace App\Services\Tenant;

use App\Models\DiningTable;
use App\Models\KitchenTicketItem;
use App\Models\Order;
use App\Models\OrderItem;
use App\Models\TenantUser;
use App\Support\Money;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;

class OrderOperationsService
{
    public function __construct(
        private readonly KitchenService $kitchen,
    ) {}

    public function transferTable(
        Order $order,
        DiningTable $targetTable,
        TenantUser $actor,
    ): Order {
        return DB::connection('tenant')->transaction(function () use ($order, $targetTable, $actor): Order {
            $order = Order::query()
                ->with(['table.diningArea', 'items'])
                ->lockForUpdate()
                ->findOrFail($order->getKey());

            if ($order->service_type !== Order::SERVICE_DINE_IN || ! $order->dining_table_id) {
                throw ValidationException::withMessages([
                    'order' => 'Only dine-in orders can be transferred between tables.',
                ]);
            }

            if (! in_array($order->status, Order::EDITABLE_STATUSES, true)) {
                throw ValidationException::withMessages([
                    'order' => 'This order can no longer be transferred.',
                ]);
            }

            $target = DiningTable::query()
                ->with('diningArea')
                ->lockForUpdate()
                ->findOrFail($targetTable->getKey());

            if (! $target->is_active || $target->status !== DiningTable::STATUS_AVAILABLE) {
                throw ValidationException::withMessages([
                    'target_table_id' => 'The target table is not available.',
                ]);
            }

            $sourceTable = DiningTable::query()
                ->lockForUpdate()
                ->findOrFail($order->dining_table_id);

            if ($target->diningArea->branch_id !== $order->branch_id) {
                throw ValidationException::withMessages([
                    'target_table_id' => 'Cross-branch table transfers are not allowed.',
                ]);
            }

            $order->update(['dining_table_id' => $target->id]);
            $this->releaseTableIfIdle($sourceTable);
            $target->update(['status' => DiningTable::STATUS_OCCUPIED]);

            $this->event($order, $actor, 'order.table_transferred', [
                'from_table_id' => $sourceTable->id,
                'to_table_id' => $target->id,
            ]);

            return $order->fresh()->load(['branch', 'table.diningArea', 'items']);
        });
    }

    public function moveUnsentItem(
        Order $source,
        OrderItem $line,
        Order $target,
        TenantUser $actor,
        int $quantity,
        ?string $targetClientLineId = null,
    ): array {
        return DB::connection('tenant')->transaction(function () use (
            $source,
            $line,
            $target,
            $actor,
            $quantity,
            $targetClientLineId,
        ): array {
            $source = Order::query()->lockForUpdate()->findOrFail($source->getKey());
            $target = Order::query()->lockForUpdate()->findOrFail($target->getKey());

            if ($source->id === $target->id) {
                throw ValidationException::withMessages([
                    'target_order_id' => 'Source and target orders must be different.',
                ]);
            }

            if (
                ! in_array($source->status, Order::EDITABLE_STATUSES, true)
                || ! in_array($target->status, Order::EDITABLE_STATUSES, true)
            ) {
                throw ValidationException::withMessages([
                    'order' => 'Both orders must still be operationally open.',
                ]);
            }

            if ($source->branch_id !== $target->branch_id) {
                throw ValidationException::withMessages([
                    'target_order_id' => 'Unsent items cannot be moved across branches.',
                ]);
            }

            $line = OrderItem::query()
                ->where('order_id', $source->id)
                ->lockForUpdate()
                ->findOrFail($line->getKey());

            $unsent = (int) $line->quantity - (int) $line->dispatched_quantity;

            if ($quantity < 1 || $quantity > $unsent) {
                throw ValidationException::withMessages([
                    'quantity' => 'Only unsent quantity can be moved to another order.',
                ]);
            }

            $targetLine = $target->items()->create([
                'menu_item_id' => $line->menu_item_id,
                'client_line_id' => $targetClientLineId ?: (string) Str::ulid(),
                'item_name' => $line->item_name,
                'unit_price' => $line->unit_price,
                'quantity' => $quantity,
                'dispatched_quantity' => 0,
                'line_total' => Money::fromMinor(Money::toMinor((string) $line->unit_price) * $quantity),
                'notes' => $line->notes,
                'seat_number' => $line->seat_number,
                'course_number' => $line->course_number,
                'course_name' => $line->course_name,
                'course_state' => $line->course_state === 'held' ? 'held' : 'open',
                'modifiers_snapshot' => $line->modifiers_snapshot,
                'allergy_instructions' => $line->allergy_instructions,
                'kitchen_instructions' => $line->kitchen_instructions,
                'status' => 'pending',
            ]);

            $remaining = (int) $line->quantity - $quantity;

            if ($remaining === 0) {
                $line->delete();
            } else {
                $line->update([
                    'quantity' => $remaining,
                    'line_total' => Money::fromMinor(
                        Money::toMinor((string) $line->unit_price) * $remaining,
                    ),
                ]);
            }

            $this->recalculate($source);
            $this->recalculate($target);

            $payload = [
                'source_order_id' => $source->id,
                'target_order_id' => $target->id,
                'source_order_item_id' => $line->id,
                'target_order_item_id' => $targetLine->id,
                'quantity' => $quantity,
            ];

            $this->event($source, $actor, 'order.item_moved_out', $payload);
            $this->event($target, $actor, 'order.item_moved_in', $payload);

            return [
                'source' => $source->fresh()->load('items'),
                'target' => $target->fresh()->load('items'),
                'target_line' => $targetLine->fresh(),
            ];
        });
    }

    public function mergeOrders(
        Order $source,
        Order $target,
        TenantUser $actor,
    ): Order {
        return DB::connection('tenant')->transaction(function () use ($source, $target, $actor): Order {
            $source = Order::query()->with(['items', 'table'])->lockForUpdate()->findOrFail($source->getKey());
            $target = Order::query()->with('items')->lockForUpdate()->findOrFail($target->getKey());

            if ($source->id === $target->id) {
                throw ValidationException::withMessages([
                    'target_order_id' => 'Source and target orders must be different.',
                ]);
            }

            if (
                ! in_array($source->status, Order::EDITABLE_STATUSES, true)
                || ! in_array($target->status, Order::EDITABLE_STATUSES, true)
            ) {
                throw ValidationException::withMessages([
                    'order' => 'Both orders must still be operationally open.',
                ]);
            }

            if ($source->branch_id !== $target->branch_id) {
                throw ValidationException::withMessages([
                    'target_order_id' => 'Orders cannot be merged across branches.',
                ]);
            }

            if ($source->items->contains(fn (OrderItem $item) => (int) $item->dispatched_quantity > 0)) {
                throw ValidationException::withMessages([
                    'order' => 'An order with dispatched kitchen production cannot be fully merged. Move only its unsent items instead.',
                ]);
            }

            foreach ($source->items as $line) {
                $target->items()->create([
                    'menu_item_id' => $line->menu_item_id,
                    'client_line_id' => $line->client_line_id ?: (string) Str::ulid(),
                    'item_name' => $line->item_name,
                    'unit_price' => $line->unit_price,
                    'quantity' => $line->quantity,
                    'dispatched_quantity' => 0,
                    'line_total' => $line->line_total,
                    'notes' => $line->notes,
                    'seat_number' => $line->seat_number,
                    'course_number' => $line->course_number,
                    'course_name' => $line->course_name,
                    'course_state' => $line->course_state,
                    'modifiers_snapshot' => $line->modifiers_snapshot,
                    'allergy_instructions' => $line->allergy_instructions,
                    'kitchen_instructions' => $line->kitchen_instructions,
                    'status' => 'pending',
                ]);
            }

            $source->items()->delete();
            $this->recalculate($target);

            if ($source->table) {
                $this->releaseTableIfIdle($source->table, $source->id);
            }

            $source->update([
                'status' => Order::STATUS_CANCELLED,
                'subtotal' => '0.00',
                'total' => '0.00',
                'closed_at' => now(),
            ]);

            $payload = [
                'source_order_id' => $source->id,
                'target_order_id' => $target->id,
            ];

            $this->event($source, $actor, 'order.merged_into', $payload);
            $this->event($target, $actor, 'order.merged_from', $payload);

            return $target->fresh()->load(['branch', 'table.diningArea', 'items']);
        });
    }

    /**
     * Cancel an open order, voiding any outstanding kitchen production and
     * releasing the table only when no other active order still needs it.
     */
    public function cancel(Order $order, TenantUser $actor, string $reason): Order
    {
        return DB::connection('tenant')->transaction(function () use ($order, $actor, $reason): Order {
            $order = Order::query()
                ->with(['table', 'kitchenTickets.items'])
                ->lockForUpdate()
                ->findOrFail($order->getKey());

            if ($order->status === Order::STATUS_CANCELLED) {
                return $order->fresh()->load(['branch', 'table.diningArea', 'items']);
            }

            if (in_array($order->status, [Order::STATUS_BILLED, Order::STATUS_CLOSED], true)) {
                throw ValidationException::withMessages([
                    'order' => 'Billed or closed orders cannot be cancelled.',
                ]);
            }

            foreach ($order->kitchenTickets as $ticket) {
                foreach ($ticket->items as $item) {
                    if (! in_array($item->status, [
                        KitchenTicketItem::STATUS_VOIDED,
                        KitchenTicketItem::STATUS_CANCELLED,
                    ], true)) {
                        $this->kitchen->voidItem($item, $actor, $reason);
                    }
                }
            }

            $table = $order->table;
            $order->update([
                'status' => Order::STATUS_CANCELLED,
                'subtotal' => '0.00',
                'total' => '0.00',
                'closed_at' => now(),
            ]);

            if ($table !== null) {
                $this->releaseTableIfIdle($table, (string) $order->id);
            }

            $this->event($order, $actor, 'order.cancelled', ['reason' => $reason]);

            return $order->fresh()->load(['branch', 'table.diningArea', 'items']);
        });
    }

    /**
     * Late arrivals join the same occupied table and order without touching
     * existing lines, KOT rounds or payments.
     */
    public function updateGuests(Order $order, TenantUser $actor, int $guestCount): Order
    {
        return DB::connection('tenant')->transaction(function () use ($order, $actor, $guestCount): Order {
            $order = Order::query()->with('table')->lockForUpdate()->findOrFail($order->getKey());

            if ($guestCount < 1 || $guestCount > 100) {
                throw ValidationException::withMessages([
                    'guest_count' => 'Guest count must be between 1 and 100.',
                ]);
            }

            if ($order->service_type !== Order::SERVICE_DINE_IN) {
                throw ValidationException::withMessages([
                    'guest_count' => 'Guests can only be changed on dine-in orders.',
                ]);
            }

            if (! in_array($order->status, Order::EDITABLE_STATUSES, true)) {
                throw ValidationException::withMessages([
                    'guest_count' => 'Guests can only be changed before the order is billed.',
                ]);
            }

            $capacity = $order->table?->capacity;
            if ($capacity !== null && $guestCount > (int) $capacity) {
                throw ValidationException::withMessages([
                    'guest_count' => "This table seats {$capacity}; move the party before adding more guests.",
                ]);
            }

            $previous = (int) $order->guest_count;
            if ($previous !== $guestCount) {
                $order->update(['guest_count' => $guestCount]);
                $this->event($order, $actor, 'order.guests_updated', [
                    'old_guest_count' => $previous,
                    'new_guest_count' => $guestCount,
                ]);
            }

            return $order->fresh()->load(['branch', 'table.diningArea', 'items']);
        });
    }

    /**
     * Single occupancy source of truth: a table is available only when no
     * active (unsettled) order still references it.
     */
    public function releaseTableIfIdle(DiningTable $table, ?string $exceptOrderId = null): void
    {
        $table = DiningTable::query()->lockForUpdate()->findOrFail($table->getKey());

        $busy = Order::query()
            ->where('dining_table_id', $table->id)
            ->whereIn('status', Order::ACTIVE_STATUSES)
            ->when($exceptOrderId !== null, fn ($query) => $query->whereKeyNot($exceptOrderId))
            ->exists();

        $table->update([
            'status' => $busy ? DiningTable::STATUS_OCCUPIED : DiningTable::STATUS_AVAILABLE,
        ]);
    }

    /**
     * Authorized manual override for stale occupancy. Refuses to run while an
     * active or unpaid order still references the table.
     */
    public function releaseTableForManagement(DiningTable $table, TenantUser $actor, string $reason): DiningTable
    {
        if (! in_array($actor->role, ['owner', 'admin', 'manager'], true)) {
            throw ValidationException::withMessages([
                'table' => 'Only management can override table occupancy.',
            ]);
        }

        return DB::connection('tenant')->transaction(function () use ($table, $reason): DiningTable {
            $table = DiningTable::query()->lockForUpdate()->findOrFail($table->getKey());

            $active = Order::query()
                ->where('dining_table_id', $table->id)
                ->whereIn('status', Order::ACTIVE_STATUSES)
                ->count();

            if ($active > 0) {
                throw ValidationException::withMessages([
                    'table' => 'This table has an active or unpaid order. Settle, transfer or explicitly cancel it before marking the table available. No order was changed.',
                ]);
            }

            // Reason is currently folded into the table state payload so the
            // desk audit trail keeps the operator explanation.
            $table->update(['status' => DiningTable::STATUS_AVAILABLE]);

            return $table->refresh();
        });
    }

    /**
     * Void one order line: outstanding kitchen production for the line is
     * voided through the existing kitchen workflow and the order totals are
     * recalculated so cloud figures match the desktop ledger.
     */
    public function voidOrderItem(Order $order, OrderItem $line, TenantUser $actor, string $reason): OrderItem
    {
        return DB::connection('tenant')->transaction(function () use ($order, $line, $actor, $reason): OrderItem {
            $order = Order::query()->lockForUpdate()->findOrFail($order->getKey());
            $line = OrderItem::query()->lockForUpdate()->findOrFail($line->getKey());

            if ($line->order_id !== $order->id) {
                throw ValidationException::withMessages([
                    'order' => 'The order item does not belong to this order.',
                ]);
            }

            if (in_array($order->status, [Order::STATUS_BILLED, Order::STATUS_CLOSED, Order::STATUS_CANCELLED], true)) {
                throw ValidationException::withMessages([
                    'order' => 'Items cannot be voided after the order is billed, closed or cancelled.',
                ]);
            }

            if ($line->status === 'voided') {
                return $line;
            }

            $production = \App\Models\KitchenTicketItem::query()
                ->where('order_item_id', $line->id)
                ->get();

            foreach ($production as $kitchenItem) {
                if (! in_array($kitchenItem->status, [
                    \App\Models\KitchenTicketItem::STATUS_VOIDED,
                    \App\Models\KitchenTicketItem::STATUS_CANCELLED,
                ], true)) {
                    $this->kitchen->voidItem($kitchenItem, $actor, $reason);
                }
            }

            $line->update(['status' => 'voided']);
            $this->recalculate($order);
            $this->event($order, $actor, 'order.item_voided', [
                'order_item_id' => $line->id,
                'reason' => $reason,
            ]);

            return $line->fresh();
        });
    }

    /**
     * Close an order that no longer holds lines or kitchen production, and
     * release its table only when no other active order needs it. This mirrors
     * the desktop move/split behavior on the tenant master.
     */
    public function closeIfEmpty(Order $order, TenantUser $actor): Order
    {
        return DB::connection('tenant')->transaction(function () use ($order, $actor): Order {
            $order = Order::query()->with('table')->lockForUpdate()->findOrFail($order->getKey());

            if (in_array($order->status, [Order::STATUS_CLOSED, Order::STATUS_CANCELLED], true)) {
                return $order;
            }

            $hasLines = $order->items()
                ->whereNotIn('status', ['voided', 'cancelled'])
                ->exists();

            if ($hasLines || $order->kitchenTickets()->exists()) {
                return $order;
            }

            $order->update([
                'status' => Order::STATUS_CLOSED,
                'subtotal' => '0.00',
                'total' => '0.00',
                'closed_at' => now(),
            ]);

            if ($order->table !== null) {
                $this->releaseTableIfIdle($order->table, (string) $order->id);
            }

            $this->event($order, $actor, 'order.closed_empty', []);

            return $order->fresh();
        });
    }

    private function recalculate(Order $order): void
    {
        $subtotalMinor = $order->items()
            ->whereNotIn('status', ['voided', 'cancelled'])
            ->get()
            ->sum(fn (OrderItem $item) => Money::toMinor((string) $item->line_total));

        $order->update([
            'subtotal' => Money::fromMinor((int) $subtotalMinor),
            'total' => Money::fromMinor((int) $subtotalMinor),
        ]);
    }

    private function event(
        Order $order,
        TenantUser $actor,
        string $type,
        array $payload,
    ): void {
        $order->events()->create([
            'actor_user_id' => $actor->getKey(),
            'event_type' => $type,
            'from_status' => $order->status,
            'to_status' => $order->status,
            'payload' => $payload,
            'occurred_at' => now(),
        ]);
    }
}
