<?php

namespace App\Services\Tenant;

use App\Models\DiningTable;
use App\Models\Order;
use App\Models\OrderItem;
use App\Models\TenantUser;
use App\Support\Money;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;

class OrderOperationsService
{
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
            $sourceTable->update(['status' => DiningTable::STATUS_AVAILABLE]);
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
    ): array {
        return DB::connection('tenant')->transaction(function () use (
            $source,
            $line,
            $target,
            $actor,
            $quantity,
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
                'client_line_id' => (string) Str::ulid(),
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
                    'client_line_id' => (string) Str::ulid(),
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
                $source->table->update(['status' => DiningTable::STATUS_AVAILABLE]);
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

    private function recalculate(Order $order): void
    {
        $subtotalMinor = $order->items()
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
