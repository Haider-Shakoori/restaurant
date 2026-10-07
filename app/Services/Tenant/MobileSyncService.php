<?php

namespace App\Services\Tenant;

use App\Models\DeviceActivation;
use App\Models\DiningArea;
use App\Models\DiningTable;
use App\Models\KitchenStation;
use App\Models\KitchenTicketItem;
use App\Models\MenuCategory;
use App\Models\MenuItem;
use App\Models\MenuItemKitchenRoute;
use App\Models\Order;
use App\Models\RestaurantBranch;
use App\Models\SyncChange;
use App\Models\SyncMutation;
use App\Models\TenantUser;
use Illuminate\Database\Eloquent\ModelNotFoundException;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Validator;
use Illuminate\Validation\ValidationException;

class MobileSyncService
{
    public const OP_ORDER_OPEN = 'order.open';

    public const OP_ORDER_ITEM_ADD = 'order.item.add';

    public const OP_ORDER_SUBMIT = 'order.submit';

    public const OP_ORDER_KOT_SEND = 'order.kot.send';

    public const OP_ORDER_COURSE_FIRE = 'order.course.fire';

    public const OP_ORDER_ITEM_VOID = 'order.item.void';

    public const OP_ORDER_ITEM_REFIRE = 'order.item.refire';

    public const OP_ORDER_ITEM_RECALL = 'order.item.recall';

    public const OP_ORDER_TABLE_TRANSFER = 'order.table.transfer';

    public const OP_ORDER_ITEM_MOVE = 'order.item.move';

    public const OP_ORDER_MERGE = 'order.merge';

    public function __construct(
        private readonly OrderService $orders,
        private readonly OrderOperationsService $orderOperations,
        private readonly KitchenService $kitchen,
        private readonly SyncDeviceService $devices,
        private readonly RestaurantSettingsService $settings,
    ) {}

    public function bootstrap(TenantUser $user, DeviceActivation $device): array
    {
        $state = $this->devices->state($device, $user);
        $cursor = (int) (SyncChange::query()->max('sequence') ?? 0);

        $state->update([
            'last_pull_cursor' => $cursor,
            'last_pull_at' => now(),
            'last_seen_at' => now(),
        ]);

        return [
            'schema_version' => 1,
            'server_time' => now()->utc()->toIso8601String(),
            'cursor' => $cursor,
            'sync_batch_size' => max(1, (int) config('restaurant.performance.sync_batch_size', 100)),
            'tenant_id' => tenant('id'),
            'device_id' => $device->id,
            'user' => $this->userSnapshot($user),
            'branches' => $this->branchSnapshot(),
            'staff' => $this->staffSnapshot(),
            'menu' => $this->menuSnapshot(),
            'kitchen' => $this->kitchenSnapshot(),
            'tables' => $this->tableSnapshot(),
            'orders' => $this->activeOrderSnapshot($user),
            'restaurant_settings' => $this->settings->all(),
        ];
    }

    public function push(
        TenantUser $user,
        DeviceActivation $device,
        array $mutations,
    ): array {
        $results = [];

        foreach ($mutations as $mutation) {
            $results[] = $this->processMutation($user, $device, $mutation);
        }

        $state = $this->devices->state($device, $user);
        $state->update([
            'last_push_at' => now(),
            'last_seen_at' => now(),
        ]);

        return [
            'server_time' => now()->utc()->toIso8601String(),
            'results' => $results,
            'pull_cursor' => (int) (SyncChange::query()->max('sequence') ?? 0),
        ];
    }

    public function pull(
        TenantUser $user,
        DeviceActivation $device,
        int $cursor,
        int $limit,
    ): array {
        $limit = max(1, min(
            $limit,
            max(1, (int) config('restaurant.performance.sync_batch_size', 100)),
        ));

        $changes = SyncChange::query()
            ->where('sequence', '>', $cursor)
            ->orderBy('sequence')
            ->limit($limit)
            ->get();

        $lastScanned = $changes->isEmpty()
            ? $cursor
            : (int) $changes->last()->sequence;

        $visible = [];

        foreach ($changes as $change) {
            $snapshot = $this->visibleChange($user, $change);

            if ($snapshot !== null) {
                $visible[] = $snapshot;
            }
        }

        $hasMore = SyncChange::query()
            ->where('sequence', '>', $lastScanned)
            ->exists();

        $state = $this->devices->state($device, $user);
        $state->update([
            'last_pull_cursor' => $lastScanned,
            'last_pull_at' => now(),
            'last_seen_at' => now(),
        ]);

        return [
            'server_time' => now()->utc()->toIso8601String(),
            'cursor' => $lastScanned,
            'has_more' => $hasMore,
            'changes' => $visible,
        ];
    }

    private function processMutation(
        TenantUser $user,
        DeviceActivation $device,
        array $mutation,
    ): array {
        $mutationId = (string) $mutation['mutation_id'];
        $operation = (string) $mutation['operation'];
        $payload = (array) $mutation['payload'];
        $requestHash = $this->mutationHash($operation, $payload);

        $existing = SyncMutation::query()
            ->where('central_device_id', $device->id)
            ->where('mutation_id', $mutationId)
            ->first();

        if ($existing) {
            if (! hash_equals($existing->request_hash, $requestHash)) {
                return [
                    'mutation_id' => $mutationId,
                    'status' => SyncMutation::STATUS_REJECTED,
                    'code' => 'mutation_id_reused',
                    'message' => 'This mutation ID was already used with different content.',
                ];
            }

            return $existing->response ?? [
                'mutation_id' => $mutationId,
                'status' => $existing->status,
                'code' => $existing->error_code,
                'message' => $existing->error_message,
            ];
        }

        try {
            $result = DB::connection('tenant')->transaction(function () use (
                $user,
                $device,
                $mutation,
                $mutationId,
                $operation,
                $payload,
                $requestHash,
            ): array {
                $accepted = $this->dispatchMutation($user, $operation, $payload, $mutationId);

                $response = [
                    'mutation_id' => $mutationId,
                    'status' => SyncMutation::STATUS_ACCEPTED,
                    'operation' => $operation,
                    ...$accepted,
                ];

                SyncMutation::query()->create([
                    'central_device_id' => $device->id,
                    'tenant_user_id' => $user->getKey(),
                    'mutation_id' => $mutationId,
                    'operation' => $operation,
                    'entity_type' => $accepted['entity_type'] ?? null,
                    'entity_id' => $accepted['entity_id'] ?? null,
                    'status' => SyncMutation::STATUS_ACCEPTED,
                    'request_hash' => $requestHash,
                    'response' => $response,
                    'client_occurred_at' => $mutation['occurred_at'] ?? null,
                    'processed_at' => now(),
                ]);

                return $response;
            });

            return $result;
        } catch (ValidationException $exception) {
            return $this->recordFailure(
                $user,
                $device,
                $mutation,
                $requestHash,
                SyncMutation::STATUS_CONFLICT,
                $this->conflictCode($exception),
                collect($exception->errors())->flatten()->first() ?: 'Synchronization conflict.',
            );
        } catch (ModelNotFoundException) {
            return $this->recordFailure(
                $user,
                $device,
                $mutation,
                $requestHash,
                SyncMutation::STATUS_CONFLICT,
                'dependency_missing',
                'A referenced server record does not exist yet.',
            );
        }
    }

    private function dispatchMutation(TenantUser $user, string $operation, array $payload, string $mutationId): array
    {
        return match ($operation) {
            self::OP_ORDER_OPEN => $this->openOrder($user, $payload),
            self::OP_ORDER_ITEM_ADD => $this->addOrderItem($user, $payload),
            self::OP_ORDER_SUBMIT, self::OP_ORDER_KOT_SEND => $this->submitOrder($user, $payload, $mutationId),
            self::OP_ORDER_COURSE_FIRE => $this->fireCourse($user, $payload, $mutationId),
            self::OP_ORDER_ITEM_VOID => $this->voidProduction($user, $payload),
            self::OP_ORDER_ITEM_REFIRE => $this->refireProduction($user, $payload, $mutationId),
            self::OP_ORDER_ITEM_RECALL => $this->recallProduction($user, $payload),
            self::OP_ORDER_TABLE_TRANSFER => $this->transferTable($user, $payload),
            self::OP_ORDER_ITEM_MOVE => $this->moveOrderItem($user, $payload),
            self::OP_ORDER_MERGE => $this->mergeOrders($user, $payload),
            default => throw ValidationException::withMessages([
                'operation' => 'Unsupported offline operation.',
            ]),
        };
    }

    private function openOrder(TenantUser $user, array $payload): array
    {
        $data = Validator::make($payload, [
            'client_order_id' => ['required', 'string', 'max:40'],
            'branch_id' => ['nullable', 'string', 'max:40'],
            'service_type' => ['nullable', 'string', 'max:24'],
            'service_reference' => ['nullable', 'string', 'max:120'],
            'dining_table_id' => ['nullable', 'string', 'max:40'],
            'guest_count' => ['nullable', 'integer', 'min:1', 'max:100'],
            'notes' => ['nullable', 'string', 'max:2000'],
        ])->validate();

        $order = $this->orders->open($user, $data);
        $this->authorizeOrder($user, $order);

        return [
            'entity_type' => 'order',
            'entity_id' => $order->id,
            'client_entity_id' => $order->client_order_id,
            'data' => $this->orderSnapshot($order->fresh()),
        ];
    }

    private function addOrderItem(TenantUser $user, array $payload): array
    {
        $data = Validator::make($payload, [
            'client_order_id' => ['required', 'string', 'max:40'],
            'client_line_id' => ['required', 'string', 'max:40'],
            'menu_item_id' => ['required', 'string', 'max:40'],
            'quantity' => ['required', 'integer', 'min:1', 'max:999'],
            'notes' => ['nullable', 'string', 'max:1000'],
            'seat_number' => ['nullable', 'integer', 'min:1', 'max:999'],
            'course_number' => ['nullable', 'integer', 'min:1', 'max:99'],
            'course_name' => ['nullable', 'string', 'max:80'],
            'modifiers' => ['nullable', 'array', 'max:50'],
            'modifiers.*.option_id' => ['required', 'string', 'max:40'],
            'allergy_instructions' => ['nullable', 'string', 'max:1000'],
            'kitchen_instructions' => ['nullable', 'string', 'max:1000'],
        ])->validate();

        $order = Order::query()
            ->where('client_order_id', $data['client_order_id'])
            ->firstOrFail();

        $this->authorizeOrder($user, $order);

        $line = $this->orders->addItem($order, $user, $data);

        return [
            'entity_type' => 'order_item',
            'entity_id' => $line->id,
            'client_entity_id' => $line->client_line_id,
            'data' => [
                'line' => $line->toArray(),
                'order' => $this->orderSnapshot($order->fresh()),
            ],
        ];
    }

    private function submitOrder(TenantUser $user, array $payload, string $mutationId): array
    {
        $data = Validator::make($payload, [
            'client_order_id' => ['required', 'string', 'max:40'],
        ])->validate();

        $order = Order::query()
            ->where('client_order_id', $data['client_order_id'])
            ->firstOrFail();

        $this->authorizeOrder($user, $order);

        $order = $this->orders->submit($order, $user, $mutationId);

        return [
            'entity_type' => 'order',
            'entity_id' => $order->id,
            'client_entity_id' => $order->client_order_id,
            'data' => $this->orderSnapshot($order),
        ];
    }

    private function fireCourse(TenantUser $user, array $payload, string $mutationId): array
    {
        $data = Validator::make($payload, [
            'client_order_id' => ['required', 'string', 'max:40'],
            'course_number' => ['required', 'integer', 'min:1', 'max:99'],
            'priority' => ['nullable', 'in:normal,rush'],
        ])->validate();

        $order = Order::query()
            ->where('client_order_id', $data['client_order_id'])
            ->firstOrFail();

        $this->authorizeOrder($user, $order);

        $order = $this->orders->fireCourse(
            $order,
            $user,
            (int) $data['course_number'],
            $mutationId,
            (string) ($data['priority'] ?? 'normal'),
        );

        return [
            'entity_type' => 'order',
            'entity_id' => $order->id,
            'client_entity_id' => $order->client_order_id,
            'data' => $this->orderSnapshot($order),
        ];
    }

    private function voidProduction(TenantUser $user, array $payload): array
    {
        $data = Validator::make($payload, [
            'kitchen_ticket_item_id' => ['required', 'string', 'max:40'],
            'reason' => ['required', 'string', 'max:1000'],
        ])->validate();

        $item = KitchenTicketItem::query()
            ->with('ticket.order')
            ->findOrFail($data['kitchen_ticket_item_id']);

        $this->authorizeOrder($user, $item->ticket->order);
        $item = $this->kitchen->voidItem($item, $user, $data['reason']);

        return [
            'entity_type' => 'kitchen_ticket_item',
            'entity_id' => $item->id,
            'data' => [
                'item' => $item->toArray(),
                'order' => $this->orderSnapshot($item->ticket->order->fresh()),
            ],
        ];
    }

    private function refireProduction(TenantUser $user, array $payload, string $mutationId): array
    {
        $data = Validator::make($payload, [
            'kitchen_ticket_item_id' => ['required', 'string', 'max:40'],
            'reason' => ['required', 'string', 'max:1000'],
        ])->validate();

        $item = KitchenTicketItem::query()
            ->with('ticket.order')
            ->findOrFail($data['kitchen_ticket_item_id']);

        $this->authorizeOrder($user, $item->ticket->order);
        $refire = $this->kitchen->refireItem($item, $user, $data['reason'], $mutationId);

        $refire->loadMissing('ticket.order');

        return [
            'entity_type' => 'kitchen_ticket_item',
            'entity_id' => $refire->id,
            'data' => [
                'item' => $refire->toArray(),
                'order' => $this->orderSnapshot($refire->ticket->order->fresh()),
            ],
        ];
    }

    private function recallProduction(TenantUser $user, array $payload): array
    {
        $data = Validator::make($payload, [
            'kitchen_ticket_item_id' => ['required', 'string', 'max:40'],
            'reason' => ['required', 'string', 'max:1000'],
        ])->validate();

        $item = KitchenTicketItem::query()
            ->with('ticket.order')
            ->findOrFail($data['kitchen_ticket_item_id']);

        $this->authorizeOrder($user, $item->ticket->order);
        $recalled = $this->kitchen->recallItem($item, $user, $data['reason']);

        $recalled->loadMissing('ticket.order');

        return [
            'entity_type' => 'kitchen_ticket_item',
            'entity_id' => $recalled->id,
            'data' => [
                'item' => $recalled->toArray(),
                'order' => $this->orderSnapshot($recalled->ticket->order->fresh()),
            ],
        ];
    }

    private function transferTable(TenantUser $user, array $payload): array
    {
        $data = Validator::make($payload, [
            'client_order_id' => ['required', 'string', 'max:40'],
            'target_table_id' => ['required', 'string', 'max:40'],
        ])->validate();

        $order = Order::query()
            ->where('client_order_id', $data['client_order_id'])
            ->firstOrFail();
        $target = DiningTable::query()->findOrFail($data['target_table_id']);

        $this->authorizeOrder($user, $order);
        $order = $this->orderOperations->transferTable($order, $target, $user);

        return [
            'entity_type' => 'order',
            'entity_id' => $order->id,
            'client_entity_id' => $order->client_order_id,
            'data' => $this->orderSnapshot($order),
        ];
    }

    private function moveOrderItem(TenantUser $user, array $payload): array
    {
        $data = Validator::make($payload, [
            'source_client_order_id' => ['required', 'string', 'max:40'],
            'target_client_order_id' => ['required', 'string', 'max:40'],
            'client_line_id' => ['required', 'string', 'max:40'],
            'target_client_line_id' => ['required', 'string', 'max:40'],
            'quantity' => ['required', 'integer', 'min:1'],
        ])->validate();

        $source = Order::query()
            ->where('client_order_id', $data['source_client_order_id'])
            ->firstOrFail();
        $target = Order::query()
            ->where('client_order_id', $data['target_client_order_id'])
            ->firstOrFail();
        $line = $source->items()
            ->where('client_line_id', $data['client_line_id'])
            ->firstOrFail();

        $this->authorizeOrder($user, $source);
        $this->authorizeOrder($user, $target);

        $result = $this->orderOperations->moveUnsentItem(
            $source,
            $line,
            $target,
            $user,
            (int) $data['quantity'],
            $data['target_client_line_id'],
        );

        return [
            'entity_type' => 'order',
            'entity_id' => $source->id,
            'client_entity_id' => $source->client_order_id,
            'data' => [
                'source' => $this->orderSnapshot($result['source']),
                'target' => $this->orderSnapshot($result['target']),
            ],
        ];
    }

    private function mergeOrders(TenantUser $user, array $payload): array
    {
        $data = Validator::make($payload, [
            'source_client_order_id' => ['required', 'string', 'max:40'],
            'target_client_order_id' => ['required', 'string', 'max:40'],
        ])->validate();

        $source = Order::query()
            ->where('client_order_id', $data['source_client_order_id'])
            ->firstOrFail();
        $target = Order::query()
            ->where('client_order_id', $data['target_client_order_id'])
            ->firstOrFail();

        $this->authorizeOrder($user, $source);
        $this->authorizeOrder($user, $target);

        $target = $this->orderOperations->mergeOrders($source, $target, $user);

        return [
            'entity_type' => 'order',
            'entity_id' => $target->id,
            'client_entity_id' => $target->client_order_id,
            'data' => [
                'source' => $this->orderSnapshot($source->fresh()),
                'target' => $this->orderSnapshot($target),
            ],
        ];
    }

    private function recordFailure(
        TenantUser $user,
        DeviceActivation $device,
        array $mutation,
        string $requestHash,
        string $status,
        string $code,
        string $message,
    ): array {
        $response = [
            'mutation_id' => (string) $mutation['mutation_id'],
            'status' => $status,
            'operation' => (string) $mutation['operation'],
            'code' => $code,
            'message' => $message,
        ];

        SyncMutation::query()->create([
            'central_device_id' => $device->id,
            'tenant_user_id' => $user->getKey(),
            'mutation_id' => (string) $mutation['mutation_id'],
            'operation' => (string) $mutation['operation'],
            'status' => $status,
            'request_hash' => $requestHash,
            'response' => $response,
            'error_code' => $code,
            'error_message' => $message,
            'client_occurred_at' => $mutation['occurred_at'] ?? null,
            'processed_at' => now(),
        ]);

        return $response;
    }

    private function conflictCode(ValidationException $exception): string
    {
        $key = array_key_first($exception->errors());

        return match ($key) {
            'dining_table_id' => 'table_busy',
            'order' => 'order_state_conflict',
            'menu_item_id' => 'menu_unavailable',
            'operation' => 'unsupported_operation',
            default => 'invalid_payload',
        };
    }

    private function authorizeOrder(TenantUser $user, Order $order): void
    {
        if ($user->role === 'waiter' && (int) $order->waiter_id !== (int) $user->id) {
            throw ValidationException::withMessages([
                'order' => 'This order belongs to another waiter.',
            ]);
        }
    }

    private function visibleChange(TenantUser $user, SyncChange $change): ?array
    {
        if ($change->entity_type === 'order') {
            $order = Order::query()->find($change->entity_id);

            if (! $order) {
                return [
                    'sequence' => $change->sequence,
                    'entity_type' => 'order',
                    'entity_id' => $change->entity_id,
                    'operation' => 'delete',
                    'data' => null,
                ];
            }

            if ($user->role === 'waiter' && (int) $order->waiter_id !== (int) $user->id) {
                return null;
            }

            return [
                'sequence' => $change->sequence,
                'entity_type' => 'order',
                'entity_id' => $order->id,
                'operation' => 'upsert',
                'data' => $this->orderSnapshot($order),
            ];
        }

        if ($change->entity_type === 'dining_table') {
            $table = DiningTable::query()
                ->with('diningArea.branch')
                ->find($change->entity_id);

            return [
                'sequence' => $change->sequence,
                'entity_type' => 'dining_table',
                'entity_id' => $change->entity_id,
                'operation' => $table ? 'upsert' : 'delete',
                'data' => $table ? $this->singleTableSnapshot($table) : null,
            ];
        }

        if ($change->entity_type === 'menu_item') {
            $item = MenuItem::query()->find($change->entity_id);

            return [
                'sequence' => $change->sequence,
                'entity_type' => 'menu_item',
                'entity_id' => $change->entity_id,
                'operation' => $item ? 'upsert' : 'delete',
                'data' => $item?->toArray(),
            ];
        }

        if ($change->entity_type === 'menu_category') {
            $category = MenuCategory::query()->find($change->entity_id);

            return [
                'sequence' => $change->sequence,
                'entity_type' => 'menu_category',
                'entity_id' => $change->entity_id,
                'operation' => $category ? 'upsert' : 'delete',
                'data' => $category?->toArray(),
            ];
        }

        if ($change->entity_type === 'dining_area') {
            $area = DiningArea::query()->find($change->entity_id);

            return [
                'sequence' => $change->sequence,
                'entity_type' => 'dining_area',
                'entity_id' => $change->entity_id,
                'operation' => $area ? 'upsert' : 'delete',
                'data' => $area?->toArray(),
            ];
        }

        return null;
    }

    private function activeOrderSnapshot(TenantUser $user): array
    {
        return Order::query()
            ->whereIn('status', Order::ACTIVE_STATUSES)
            ->when($user->role === 'waiter', fn ($query) => $query->where('waiter_id', $user->id))
            ->orderBy('opened_at')
            ->get()
            ->map(fn (Order $order) => $this->orderSnapshot($order))
            ->all();
    }

    private function orderSnapshot(Order $order): array
    {
        $order->loadMissing([
            'table.diningArea.branch',
            'waiter',
            'items',
            'kotRounds.tickets.station',
            'kotRounds.tickets.items',
            'kitchenTickets.station',
        ]);

        return [
            'id' => $order->id,
            'client_order_id' => $order->client_order_id,
            'status' => $order->status,
            'branch_id' => $order->branch_id,
            'service_type' => $order->service_type,
            'service_reference' => $order->service_reference,
            'guest_count' => $order->guest_count,
            'notes' => $order->notes,
            'subtotal' => $order->subtotal,
            'total' => $order->total,
            'opened_at' => $order->opened_at?->toIso8601String(),
            'submitted_at' => $order->submitted_at?->toIso8601String(),
            'served_at' => $order->served_at?->toIso8601String(),
            'closed_at' => $order->closed_at?->toIso8601String(),
            'table' => $order->table ? $this->singleTableSnapshot($order->table) : null,
            'waiter' => [
                'id' => $order->waiter->id,
                'public_id' => $order->waiter->public_id,
                'name' => $order->waiter->name,
            ],
            'items' => $order->items->map(fn ($item) => [
                'id' => $item->id,
                'client_line_id' => $item->client_line_id,
                'menu_item_id' => $item->menu_item_id,
                'item_name' => $item->item_name,
                'unit_price' => $item->unit_price,
                'quantity' => $item->quantity,
                'line_total' => $item->line_total,
                'notes' => $item->notes,
                'seat_number' => $item->seat_number,
                'course_number' => $item->course_number,
                'course_name' => $item->course_name,
                'course_state' => $item->course_state,
                'modifiers_snapshot' => $item->modifiers_snapshot,
                'allergy_instructions' => $item->allergy_instructions,
                'kitchen_instructions' => $item->kitchen_instructions,
                'dispatched_quantity' => $item->dispatched_quantity,
                'status' => $item->status,
            ])->all(),
            'kot_rounds' => $order->kotRounds->map(fn ($round) => [
                'id' => $round->id,
                'sequence' => $round->sequence,
                'kot_number' => $round->kot_number,
                'priority' => $round->priority,
                'sent_at' => $round->sent_at?->toIso8601String(),
                'tickets' => $round->tickets->map(fn ($ticket) => [
                    'id' => $ticket->id,
                    'status' => $ticket->status,
                    'station' => [
                        'id' => $ticket->station->id,
                        'name' => $ticket->station->name,
                    ],
                    'items' => $ticket->items->map(fn ($item) => [
                        'id' => $item->id,
                        'order_item_id' => $item->order_item_id,
                        'item_name' => $item->item_name,
                        'quantity' => $item->quantity,
                        'status' => $item->status,
                        'seat_number' => $item->seat_number,
                        'course_number' => $item->course_number,
                        'course_name' => $item->course_name,
                        'refire_of_kitchen_ticket_item_id' => $item->refire_of_kitchen_ticket_item_id,
                        'production_reason' => $item->production_reason,
                        'recalled_at' => $item->recalled_at?->toIso8601String(),
                    ])->all(),
                ])->all(),
            ])->all(),
            'kitchen_tickets' => $order->kitchenTickets->map(fn ($ticket) => [
                'id' => $ticket->id,
                'ticket_number' => $ticket->ticket_number,
                'status' => $ticket->status,
                'station' => [
                    'id' => $ticket->station->id,
                    'name' => $ticket->station->name,
                ],
            ])->all(),
        ];
    }

    private function branchSnapshot(): array
    {
        return RestaurantBranch::query()
            ->where('is_active', true)
            ->orderBy('name')
            ->get()
            ->map(fn (RestaurantBranch $branch) => [
                'id' => $branch->id,
                'code' => $branch->code,
                'name' => $branch->name,
                'is_active' => $branch->is_active,
            ])
            ->all();
    }

    private function staffSnapshot(): array
    {
        return TenantUser::query()
            ->where('is_active', true)
            ->orderBy('name')
            ->get()
            ->map(fn (TenantUser $staff) => [
                'id' => $staff->id,
                'public_id' => $staff->public_id,
                'name' => $staff->name,
                'email' => $staff->email,
                'role' => $staff->role,
                'is_active' => $staff->is_active,
            ])
            ->all();
    }

    private function menuSnapshot(): array
    {
        return MenuCategory::query()
            ->where('is_active', true)
            ->with(['items' => fn ($query) => $query
                ->where('is_available', true)
                ->with(['modifierGroups' => fn ($groups) => $groups
                    ->where('menu_modifier_groups.is_active', true)
                    ->with(['options' => fn ($options) => $options
                        ->where('is_active', true)])])
                ->orderBy('sort_order')
                ->orderBy('name')])
            ->orderBy('sort_order')
            ->orderBy('name')
            ->get()
            ->map(fn (MenuCategory $category) => [
                'id' => $category->id,
                'name' => $category->name,
                'sort_order' => $category->sort_order,
                'items' => $category->items->map(fn (MenuItem $item) => [
                    'id' => $item->id,
                    'menu_category_id' => $item->menu_category_id,
                    'sku' => $item->sku,
                    'name' => $item->name,
                    'description' => $item->description,
                    'image_url' => $item->image_url,
                    'price' => $item->price,
                    'currency' => 'AFN',
                    'sort_order' => $item->sort_order,
                    'modifier_groups' => $item->modifierGroups->map(fn ($group) => [
                        'id' => $group->id,
                        'name' => $group->name,
                        'min_selections' => $group->min_selections,
                        'max_selections' => $group->max_selections,
                        'sort_order' => $group->pivot->sort_order ?? $group->sort_order,
                        'options' => $group->options->map(fn ($option) => [
                            'id' => $option->id,
                            'name' => $option->name,
                            'price_delta' => $option->price_delta,
                            'sort_order' => $option->sort_order,
                        ])->all(),
                    ])->all(),
                ])->all(),
            ])->all();
    }

    private function kitchenSnapshot(): array
    {
        return [
            'stations' => KitchenStation::query()
                ->where('is_active', true)
                ->orderBy('branch_id')
                ->orderBy('sort_order')
                ->orderBy('name')
                ->get()
                ->map(fn (KitchenStation $station) => [
                    'id' => $station->id,
                    'branch_id' => $station->branch_id,
                    'code' => $station->code,
                    'name' => $station->name,
                    'sort_order' => $station->sort_order,
                    'is_active' => $station->is_active,
                ])->all(),
            'routes' => MenuItemKitchenRoute::query()
                ->orderBy('branch_id')
                ->orderBy('menu_item_id')
                ->get()
                ->map(fn (MenuItemKitchenRoute $route) => [
                    'id' => $route->id,
                    'menu_item_id' => $route->menu_item_id,
                    'branch_id' => $route->branch_id,
                    'kitchen_station_id' => $route->kitchen_station_id,
                ])->all(),
        ];
    }

    private function tableSnapshot(): array
    {
        return DiningTable::query()
            ->with('diningArea.branch')
            ->where('is_active', true)
            ->orderBy('code')
            ->get()
            ->map(fn (DiningTable $table) => $this->singleTableSnapshot($table))
            ->all();
    }

    private function singleTableSnapshot(DiningTable $table): array
    {
        $table->loadMissing('diningArea.branch');

        return [
            'id' => $table->id,
            'code' => $table->code,
            'name' => $table->name,
            'capacity' => $table->capacity,
            'status' => $table->status,
            'is_active' => $table->is_active,
            'area' => [
                'id' => $table->diningArea->id,
                'name' => $table->diningArea->name,
            ],
            'branch' => [
                'id' => $table->diningArea->branch->id,
                'name' => $table->diningArea->branch->name,
            ],
        ];
    }

    private function userSnapshot(TenantUser $user): array
    {
        return [
            'id' => $user->id,
            'public_id' => $user->public_id,
            'name' => $user->name,
            'email' => $user->email,
            'role' => $user->role,
        ];
    }

    private function mutationHash(string $operation, array $payload): string
    {
        $canonical = [
            'operation' => $operation,
            'payload' => $this->canonicalize($payload),
        ];

        return hash(
            'sha256',
            json_encode(
                $canonical,
                JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE,
            ),
        );
    }

    private function canonicalize(array $value): array
    {
        if (! array_is_list($value)) {
            ksort($value);
        }

        foreach ($value as $key => $item) {
            if (is_array($item)) {
                $value[$key] = $this->canonicalize($item);
            }
        }

        return $value;
    }
}
