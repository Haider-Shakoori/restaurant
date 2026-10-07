<?php

namespace App\Services\Tenant;

use App\Models\DiningTable;
use App\Models\MenuItem;
use App\Models\Order;
use App\Models\OrderItem;
use App\Models\RestaurantBranch;
use App\Models\TenantUser;
use Illuminate\Support\Facades\DB;
use Illuminate\Validation\ValidationException;

class OrderService
{
    public function __construct(
        private readonly KitchenService $kitchen,
        private readonly RestaurantSettingsService $settings,
    ) {}

    public function take(TenantUser $actor, array $data): Order
    {
        return DB::connection('tenant')->transaction(function () use ($actor, $data): Order {
            $order = $this->open($actor, [
                'client_order_id' => $data['client_order_id'] ?? null,
                'branch_id' => $data['branch_id'] ?? null,
                'service_type' => $data['service_type'] ?? Order::SERVICE_DINE_IN,
                'service_reference' => $data['service_reference'] ?? null,
                'dining_table_id' => $data['dining_table_id'] ?? null,
                'guest_count' => $data['guest_count'] ?? 1,
                'notes' => $data['notes'] ?? null,
            ]);

            foreach ($data['lines'] as $line) {
                $this->addItem($order, $actor, [
                    'client_line_id' => $line['client_line_id'] ?? null,
                    'menu_item_id' => $line['menu_item_id'],
                    'quantity' => $line['quantity'],
                    'notes' => $line['notes'] ?? null,
                    'seat_number' => $line['seat_number'] ?? null,
                    'course_number' => $line['course_number'] ?? null,
                    'course_name' => $line['course_name'] ?? null,
                    'hold_for_course' => $line['hold_for_course'] ?? false,
                    'modifiers' => $line['modifiers'] ?? [],
                    'allergy_instructions' => $line['allergy_instructions'] ?? null,
                    'kitchen_instructions' => $line['kitchen_instructions'] ?? null,
                ]);
            }

            if (($data['submit_action'] ?? 'kitchen') === 'kitchen') {
                return $this->submit($order, $actor, $data['client_mutation_id'] ?? null);
            }

            return $order->fresh()->load([
                'branch',
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
                    return $existing->load(['branch', 'table.diningArea', 'items', 'kotRounds.tickets.station']);
                }
            }

            $serviceType = $data['service_type'] ?? Order::SERVICE_DINE_IN;

            if (! in_array($serviceType, Order::SERVICE_TYPES, true)) {
                throw ValidationException::withMessages([
                    'service_type' => 'The selected restaurant service type is invalid.',
                ]);
            }

            $table = null;
            $branchId = $data['branch_id'] ?? null;

            if ($serviceType === Order::SERVICE_DINE_IN) {
                if (empty($data['dining_table_id'])) {
                    throw ValidationException::withMessages([
                        'dining_table_id' => 'A dining table is required for dine-in orders.',
                    ]);
                }

                $table = DiningTable::query()
                    ->with('diningArea')
                    ->lockForUpdate()
                    ->findOrFail($data['dining_table_id']);

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

                $branchId = $table->diningArea->branch_id;
            } else {
                if (empty($branchId)) {
                    throw ValidationException::withMessages([
                        'branch_id' => 'A branch is required for takeaway, delivery and counter orders.',
                    ]);
                }

                RestaurantBranch::query()
                    ->whereKey($branchId)
                    ->where('is_active', true)
                    ->firstOrFail();
            }

            $order = Order::query()->create([
                'client_order_id' => $data['client_order_id'] ?? null,
                'branch_id' => $branchId,
                'service_type' => $serviceType,
                'service_reference' => $data['service_reference'] ?? null,
                'dining_table_id' => $table?->id,
                'waiter_id' => $waiter->getKey(),
                'status' => Order::STATUS_DRAFT,
                'guest_count' => $data['guest_count'] ?? 1,
                'notes' => $data['notes'] ?? null,
                'subtotal' => '0.00',
                'total' => '0.00',
                'opened_at' => now(),
            ]);

            if ($table) {
                $table->update(['status' => DiningTable::STATUS_OCCUPIED]);
            }

            $this->event($order, $waiter, 'order.opened', null, Order::STATUS_DRAFT, [
                'branch_id' => $branchId,
                'table_id' => $table?->id,
                'service_type' => $serviceType,
                'service_reference' => $order->service_reference,
            ]);

            return $order->load(['branch', 'table.diningArea', 'items']);
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
                ->with(['modifierGroups.options'])
                ->whereKey($data['menu_item_id'])
                ->where('is_available', true)
                ->first();

            if (! $menuItem) {
                throw ValidationException::withMessages([
                    'menu_item_id' => 'The selected menu item is unavailable.',
                ]);
            }

            [$modifierSnapshot, $modifierDeltaMinor] = $this->resolveModifiers(
                $menuItem,
                (array) ($data['modifiers'] ?? []),
            );

            $order->loadMissing('table.diningArea');
            $branchId = $order->branch_id ?? $order->table?->diningArea?->branch_id;
            $restaurantSettings = $this->settings->all($branchId);
            $courseState = (
                ($restaurantSettings['courses_enabled'] ?? false)
                && ($data['hold_for_course'] ?? false)
            ) ? 'held' : 'open';

            $quantity = (int) $data['quantity'];
            $unitPrice = $this->fromMinor($this->toMinor((string) $menuItem->price) + $modifierDeltaMinor);
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
                'seat_number' => $data['seat_number'] ?? null,
                'course_number' => $data['course_number'] ?? null,
                'course_name' => $data['course_name'] ?? null,
                'course_state' => $courseState,
                'modifiers_snapshot' => $modifierSnapshot ?: null,
                'allergy_instructions' => $data['allergy_instructions'] ?? null,
                'kitchen_instructions' => $data['kitchen_instructions'] ?? null,
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
                'seat_number' => $line->seat_number,
                'course_number' => $line->course_number,
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
                ->where('course_state', '!=', 'held')
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
                'branch',
                'table.diningArea',
                'waiter',
                'items',
                'kotRounds.tickets.station',
                'kotRounds.tickets.items',
                'kitchenTickets.station',
            ]);
        });
    }

    public function fireCourse(
        Order $order,
        TenantUser $actor,
        int $courseNumber,
        string $mutationId,
        string $priority = 'normal',
    ): Order {
        return DB::connection('tenant')->transaction(function () use (
            $order,
            $actor,
            $courseNumber,
            $mutationId,
            $priority,
        ): Order {
            $order = Order::query()
                ->with('table.diningArea')
                ->lockForUpdate()
                ->findOrFail($order->getKey());

            if (! in_array($order->status, Order::EDITABLE_STATUSES, true)) {
                throw ValidationException::withMessages([
                    'order' => 'This order can no longer fire a course.',
                ]);
            }

            $branchId = $order->branch_id ?? $order->table?->diningArea?->branch_id;
            $settings = $this->settings->all($branchId);

            if (! ($settings['courses_enabled'] ?? false)) {
                throw ValidationException::withMessages([
                    'course' => 'Course sequencing is disabled for this restaurant.',
                ]);
            }

            $held = $order->items()
                ->where('course_number', $courseNumber)
                ->where('course_state', 'held')
                ->lockForUpdate()
                ->get();

            if ($held->isNotEmpty()) {
                foreach ($held as $item) {
                    $item->update(['course_state' => 'fired']);
                }

                $this->event(
                    $order,
                    $actor,
                    'order.course_fired',
                    $order->status,
                    $order->status,
                    [
                        'course_number' => $courseNumber,
                        'item_ids' => $held->pluck('id')->all(),
                        'mutation_id' => $mutationId,
                    ],
                );
            }

            return $this->submit($order->fresh(), $actor, $mutationId, $priority);
        });
    }

    private function resolveModifiers(MenuItem $menuItem, array $selections): array
    {
        $selectedIds = collect($selections)
            ->pluck('option_id')
            ->filter()
            ->map(fn ($id) => (string) $id)
            ->unique()
            ->values();

        $matched = collect();
        $snapshot = [];
        $deltaMinor = 0;

        foreach ($menuItem->modifierGroups as $group) {
            if (! $group->is_active) {
                continue;
            }

            $selected = $group->options
                ->where('is_active', true)
                ->whereIn('id', $selectedIds)
                ->values();

            $count = $selected->count();

            if ($count < $group->min_selections || $count > $group->max_selections) {
                throw ValidationException::withMessages([
                    'modifiers' => "Select between {$group->min_selections} and {$group->max_selections} options for {$group->name}.",
                ]);
            }

            if ($count === 0) {
                continue;
            }

            foreach ($selected as $option) {
                $matched->push((string) $option->id);
                $deltaMinor += $this->toMinor((string) $option->price_delta);
            }

            $snapshot[] = [
                'group_id' => $group->id,
                'group_name' => $group->name,
                'options' => $selected->map(fn ($option) => [
                    'option_id' => $option->id,
                    'option_name' => $option->name,
                    'price_delta' => $option->price_delta,
                ])->all(),
            ];
        }

        if ($matched->unique()->count() !== $selectedIds->count()) {
            throw ValidationException::withMessages([
                'modifiers' => 'One or more selected modifiers are invalid or unavailable for this menu item.',
            ]);
        }

        return [$snapshot, $deltaMinor];
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
        return $this->fromMinor($this->toMinor($amount) * $quantity);
    }

    private function toMinor(string $amount): int
    {
        [$whole, $fraction] = array_pad(explode('.', $amount, 2), 2, '0');
        $fraction = substr(str_pad($fraction, 2, '0'), 0, 2);

        return ((int) $whole * 100) + (int) $fraction;
    }

    private function fromMinor(int $amount): string
    {
        $sign = $amount < 0 ? '-' : '';
        $absolute = abs($amount);

        return $sign.intdiv($absolute, 100).'.'.str_pad((string) ($absolute % 100), 2, '0', STR_PAD_LEFT);
    }
}
