<?php

namespace App\Services\Tenant;

use App\Models\Bill;
use App\Models\CashierSession;
use App\Models\DailyClosing;
use App\Models\DesktopEntityLink;
use App\Models\DesktopOperationalRecord;
use App\Models\DeviceActivation;
use App\Models\DiningTable;
use App\Models\GoodsReceipt;
use App\Models\InventoryBalance;
use App\Models\InventoryItem;
use App\Models\MenuItem;
use App\Models\Order;
use App\Models\PurchaseOrder;
use App\Models\Recipe;
use App\Models\RestaurantBranch;
use App\Models\StockMovement;
use App\Models\Supplier;
use App\Models\SyncChange;
use App\Models\SyncMutation;
use App\Models\TenantUser;
use Illuminate\Database\Eloquent\ModelNotFoundException;
use Illuminate\Support\Facades\DB;
use Illuminate\Validation\ValidationException;

class DesktopReconciliationService
{
    public function __construct(
        private readonly OrderService $orders,
        private readonly KitchenService $kitchen,
        private readonly CashierService $cashier,
        private readonly BillingService $billing,
        private readonly InventoryService $inventory,
        private readonly RecipeService $recipes,
        private readonly ProcurementService $procurement,
        private readonly DailyClosingService $closings,
    ) {}

    public function push(
        TenantUser $requestUser,
        DeviceActivation $device,
        array $mutations,
    ): array {
        $results = [];

        foreach ($mutations as $mutation) {
            $results[] = $this->processMutation($requestUser, $device, $mutation);
        }

        return [
            'server_time' => now()->utc()->toIso8601String(),
            'results' => $results,
            'pull_cursor' => (int) (SyncChange::query()->max('sequence') ?? 0),
        ];
    }

    public function pull(
        DeviceActivation $device,
        int $cursor,
        int $limit,
    ): array {
        $limit = max(1, min($limit, 250));

        $changes = SyncChange::query()
            ->where('sequence', '>', max(0, $cursor))
            ->orderBy('sequence')
            ->limit($limit)
            ->get();

        $lastScanned = $changes->isEmpty()
            ? max(0, $cursor)
            : (int) $changes->last()->sequence;

        $visible = $changes
            ->map(fn (SyncChange $change): array => [
                'sequence' => (int) $change->sequence,
                'entity_type' => $change->entity_type,
                'entity_id' => $change->entity_id,
                'operation' => $change->operation,
                'payload' => $this->changeSnapshot($change),
                'occurred_at' => $change->occurred_at?->utc()->toIso8601String(),
                'local_links' => DesktopEntityLink::query()
                    ->where('central_device_id', $device->id)
                    ->where('entity_type', $change->entity_type)
                    ->where('cloud_entity_id', $change->entity_id)
                    ->pluck('local_entity_id')
                    ->values()
                    ->all(),
            ])
            ->all();

        return [
            'server_time' => now()->utc()->toIso8601String(),
            'cursor' => $lastScanned,
            'has_more' => SyncChange::query()->where('sequence', '>', $lastScanned)->exists(),
            'changes' => $visible,
        ];
    }

    private function processMutation(
        TenantUser $requestUser,
        DeviceActivation $device,
        array $mutation,
    ): array {
        $mutationId = trim((string) ($mutation['mutation_id'] ?? ''));
        $operation = trim((string) ($mutation['operation'] ?? ''));
        $entityType = trim((string) ($mutation['entity_type'] ?? ''));
        $localEntityId = trim((string) ($mutation['local_entity_id'] ?? ''));
        $payload = (array) ($mutation['payload'] ?? []);
        $actorPublicId = trim((string) ($mutation['actor_public_id'] ?? ''));

        if ($mutationId === '' || $operation === '' || $localEntityId === '') {
            return $this->basicResult($mutationId, 'rejected', $operation, 'invalid_payload', 'Mutation ID, operation and local entity ID are required.');
        }

        $hash = hash('sha256', json_encode([
            'operation' => $operation,
            'entity_type' => $entityType,
            'local_entity_id' => $localEntityId,
            'payload' => $payload,
        ], JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE));

        $existing = SyncMutation::query()
            ->where('central_device_id', $device->id)
            ->where('mutation_id', $mutationId)
            ->first();

        if ($existing) {
            if (! hash_equals($existing->request_hash, $hash)) {
                return $this->basicResult($mutationId, 'rejected', $operation, 'mutation_id_reused', 'This mutation ID was already used with different content.');
            }

            return $existing->response ?? $this->basicResult(
                $mutationId,
                $existing->status,
                $operation,
                $existing->error_code,
                $existing->error_message,
            );
        }

        try {
            return DB::connection('tenant')->transaction(function () use (
                $requestUser,
                $device,
                $mutation,
                $mutationId,
                $operation,
                $entityType,
                $localEntityId,
                $payload,
                $actorPublicId,
                $hash,
            ): array {
                $actor = $this->actor($requestUser, $actorPublicId);
                $data = isset($payload['mutation_payload']) && is_array($payload['mutation_payload'])
                    ? $payload['mutation_payload']
                    : $payload;

                $accepted = $this->dispatch(
                    $device,
                    $actor,
                    $operation,
                    $entityType,
                    $localEntityId,
                    $data,
                );

                $response = [
                    'mutation_id' => $mutationId,
                    'status' => 'accepted',
                    'operation' => $operation,
                    ...$accepted,
                ];

                SyncMutation::query()->create([
                    'central_device_id' => $device->id,
                    'tenant_user_id' => $actor->getKey(),
                    'mutation_id' => $mutationId,
                    'operation' => $operation,
                    'entity_type' => $accepted['entity_type'] ?? $entityType,
                    'entity_id' => $accepted['entity_id'] ?? null,
                    'status' => SyncMutation::STATUS_ACCEPTED,
                    'request_hash' => $hash,
                    'response' => $response,
                    'client_occurred_at' => $mutation['occurred_at'] ?? null,
                    'processed_at' => now(),
                ]);

                return $response;
            });
        } catch (ValidationException $exception) {
            return $this->failure(
                $requestUser,
                $device,
                $mutation,
                $hash,
                SyncMutation::STATUS_CONFLICT,
                $this->conflictCode($exception),
                collect($exception->errors())->flatten()->first() ?: 'Synchronization conflict.',
            );
        } catch (ModelNotFoundException) {
            return $this->failure(
                $requestUser,
                $device,
                $mutation,
                $hash,
                SyncMutation::STATUS_CONFLICT,
                'dependency_missing',
                'A referenced cloud record does not exist yet.',
            );
        }
    }

    private function dispatch(
        DeviceActivation $device,
        TenantUser $actor,
        string $operation,
        string $entityType,
        string $localEntityId,
        array $payload,
    ): array {
        return match ($operation) {
            'order.open' => $this->openOrder($device, $actor, $localEntityId, $payload),
            'order.item.add' => $this->addOrderItem($device, $actor, $localEntityId, $payload),
            'order.submit' => $this->submitOrder($device, $actor, $localEntityId, $payload),
            'order.serve' => $this->serveOrder($device, $actor, $localEntityId, $payload),
            'cashier.session.open' => $this->openCashierSession($device, $actor, $localEntityId, $payload),
            'cashier.session.close' => $this->closeCashierSession($device, $actor, $localEntityId, $payload),
            'bill.issue' => $this->issueBill($device, $actor, $localEntityId, $payload),
            'bill.discount' => $this->discountBill($device, $actor, $localEntityId, $payload),
            'payment.post' => $this->postPayment($device, $actor, $localEntityId, $payload),
            'inventory.item.create' => $this->createInventoryItem($device, $localEntityId, $payload),
            'inventory.adjust' => $this->adjustInventory($device, $actor, $localEntityId, $payload),
            'supplier.create' => $this->createSupplier($device, $localEntityId, $payload),
            'recipe.version.create' => $this->createRecipe($device, $localEntityId, $payload),
            'purchase_order.create' => $this->createPurchaseOrder($device, $actor, $localEntityId, $payload),
            'goods_receipt.post' => $this->postGoodsReceipt($device, $actor, $localEntityId, $payload),
            'daily_closing.finalize' => $this->finalizeClosing($device, $actor, $localEntityId, $payload),
            'daily_closing.reopen' => $this->reopenClosing($device, $actor, $localEntityId, $payload),
            'shift.upsert', 'audit.append' => $this->storeOperationalRecord(
                $device,
                $actor,
                $operation,
                $localEntityId,
                $payload,
            ),
            default => throw ValidationException::withMessages([
                'operation' => 'Unsupported desktop reconciliation operation.',
            ]),
        };
    }

    private function openOrder(DeviceActivation $device, TenantUser $actor, string $localId, array $payload): array
    {
        $order = $this->orders->open($actor, $payload);
        $this->link($device, 'order', $localId, $order->id);

        return ['entity_type' => 'order', 'entity_id' => $order->id];
    }

    private function addOrderItem(DeviceActivation $device, TenantUser $actor, string $localId, array $payload): array
    {
        $order = Order::query()->where('client_order_id', $payload['client_order_id'] ?? null)->firstOrFail();
        $line = $this->orders->addItem($order, $actor, $payload);
        $this->link($device, 'order_item', $localId, $line->id);

        return ['entity_type' => 'order_item', 'entity_id' => $line->id];
    }

    private function submitOrder(DeviceActivation $device, TenantUser $actor, string $localId, array $payload): array
    {
        $order = Order::query()->where('client_order_id', $payload['client_order_id'] ?? null)->firstOrFail();
        $order = $this->orders->submit($order, $actor);
        $this->link($device, 'order', $localId, $order->id);

        return ['entity_type' => 'order', 'entity_id' => $order->id];
    }

    private function serveOrder(DeviceActivation $device, TenantUser $actor, string $localId, array $payload): array
    {
        $order = Order::query()
            ->where('client_order_id', $payload['client_order_id'] ?? null)
            ->firstOrFail();

        if ($order->status === Order::STATUS_DRAFT) {
            $order = $this->orders->submit($order, $actor);
        }

        $order->load('kitchenTickets');

        foreach ($order->kitchenTickets as $ticket) {
            if (in_array($ticket->status, ['queued', 'preparing'], true)) {
                $this->kitchen->ready($ticket, $actor);
            }
        }

        if (! in_array($order->fresh()->status, [Order::STATUS_SERVED, Order::STATUS_BILLED, Order::STATUS_CLOSED], true)) {
            $order = $this->kitchen->serve($order->fresh(), $actor);
        }

        $this->link($device, 'order', $localId, $order->id);

        return ['entity_type' => 'order', 'entity_id' => $order->id];
    }

    private function openCashierSession(DeviceActivation $device, TenantUser $actor, string $localId, array $payload): array
    {
        $branch = RestaurantBranch::query()->findOrFail($payload['branch_id'] ?? null);

        $existingCloudId = $this->resolve($device, 'cashier_session', $localId);
        $session = $existingCloudId
            ? CashierSession::query()->findOrFail($existingCloudId)
            : $this->cashier->openSession($branch, $actor, (string) ($payload['opening_cash'] ?? '0'));

        $this->link($device, 'cashier_session', $localId, $session->id);

        return ['entity_type' => 'cashier_session', 'entity_id' => $session->id];
    }

    private function closeCashierSession(DeviceActivation $device, TenantUser $actor, string $localId, array $payload): array
    {
        $session = CashierSession::query()->findOrFail(
            $this->resolveRequired($device, 'cashier_session', $localId)
        );
        $session = $this->cashier->closeSession($session, $actor, (string) ($payload['declared_cash'] ?? '0'));

        return ['entity_type' => 'cashier_session', 'entity_id' => $session->id];
    }

    private function issueBill(DeviceActivation $device, TenantUser $actor, string $localId, array $payload): array
    {
        $order = Order::query()
            ->where('client_order_id', $payload['client_order_id'] ?? null)
            ->firstOrFail();
        $bill = $this->billing->createBill($order, $actor);
        $this->link($device, 'bill', $localId, $bill->id);

        return ['entity_type' => 'bill', 'entity_id' => $bill->id];
    }

    private function discountBill(DeviceActivation $device, TenantUser $actor, string $localId, array $payload): array
    {
        $bill = Bill::query()->findOrFail($this->resolveRequired($device, 'bill', $localId));
        $bill = $this->billing->applyDiscount(
            $bill,
            $actor,
            (string) ($payload['type'] ?? ''),
            (string) ($payload['value'] ?? '0'),
            $payload['reason'] ?? null,
        );

        return ['entity_type' => 'bill', 'entity_id' => $bill->id];
    }

    private function postPayment(DeviceActivation $device, TenantUser $actor, string $localId, array $payload): array
    {
        $bill = Bill::query()->findOrFail(
            $this->resolveRequired($device, 'bill', (string) ($payload['local_bill_id'] ?? ''))
        );
        $session = CashierSession::query()->findOrFail(
            $this->resolveRequired($device, 'cashier_session', (string) ($payload['local_cashier_session_id'] ?? ''))
        );

        $payment = $this->billing->addPayment($bill, $session, $actor, [
            'client_payment_id' => $payload['client_payment_id'] ?? $localId,
            'method' => $payload['method'] ?? 'cash',
            'amount' => (string) ($payload['amount'] ?? '0'),
            'reference' => $payload['reference'] ?? null,
        ]);

        $this->link($device, 'payment', $localId, $payment->id);

        return ['entity_type' => 'payment', 'entity_id' => $payment->id];
    }

    private function createInventoryItem(DeviceActivation $device, string $localId, array $payload): array
    {
        $sku = strtoupper(trim((string) ($payload['sku'] ?? '')));
        $item = InventoryItem::query()->firstOrCreate(
            ['sku' => $sku],
            [
                'name' => $payload['name'] ?? $sku,
                'base_unit' => strtolower((string) ($payload['base_unit'] ?? 'unit')),
                'purchase_unit' => isset($payload['purchase_unit']) ? strtolower((string) $payload['purchase_unit']) : null,
                'purchase_to_base_factor' => (string) ($payload['purchase_to_base_factor'] ?? '1'),
                'reorder_level' => (string) ($payload['reorder_level'] ?? '0'),
                'is_active' => true,
            ],
        );

        $this->link($device, 'inventory_item', $localId, $item->id);

        return ['entity_type' => 'inventory_item', 'entity_id' => $item->id];
    }

    private function adjustInventory(DeviceActivation $device, TenantUser $actor, string $localId, array $payload): array
    {
        $branch = RestaurantBranch::query()->findOrFail($payload['branch_id'] ?? null);
        $item = InventoryItem::query()->findOrFail(
            $this->resolveRequired($device, 'inventory_item', (string) ($payload['local_inventory_item_id'] ?? ''))
        );

        $movement = $this->inventory->adjust(
            $branch,
            $item,
            $actor,
            (string) ($payload['quantity_delta'] ?? '0'),
            (string) ($payload['client_adjustment_id'] ?? $localId),
            (string) ($payload['reason'] ?? 'Desktop reconciliation'),
        );

        $this->link($device, 'stock_movement', $localId, $movement->id);

        return ['entity_type' => 'stock_movement', 'entity_id' => $movement->id];
    }

    private function createSupplier(DeviceActivation $device, string $localId, array $payload): array
    {
        $code = strtoupper(trim((string) ($payload['code'] ?? '')));
        $supplier = Supplier::query()->firstOrCreate(
            ['code' => $code],
            [
                'name' => $payload['name'] ?? $code,
                'phone' => $payload['phone'] ?? null,
                'email' => $payload['email'] ?? null,
                'address' => $payload['address'] ?? null,
                'is_active' => true,
            ],
        );

        $this->link($device, 'supplier', $localId, $supplier->id);

        return ['entity_type' => 'supplier', 'entity_id' => $supplier->id];
    }

    private function createRecipe(DeviceActivation $device, string $localId, array $payload): array
    {
        $branch = RestaurantBranch::query()->findOrFail($payload['branch_id'] ?? null);
        $menuItem = MenuItem::query()->findOrFail($payload['menu_item_id'] ?? null);

        $items = collect($payload['items'] ?? [])->map(function (array $item) use ($device): array {
            return [
                'inventory_item_id' => $this->resolveRequired(
                    $device,
                    'inventory_item',
                    (string) ($item['local_inventory_item_id'] ?? '')
                ),
                'quantity_base' => (string) ($item['quantity_base'] ?? '0'),
            ];
        })->all();

        $recipe = $this->recipes->createVersion($branch, $menuItem, [
            'name' => $payload['name'] ?? null,
            'items' => $items,
        ]);

        $this->link($device, 'recipe', $localId, $recipe->id);

        return ['entity_type' => 'recipe', 'entity_id' => $recipe->id];
    }

    private function createPurchaseOrder(DeviceActivation $device, TenantUser $actor, string $localId, array $payload): array
    {
        $branch = RestaurantBranch::query()->findOrFail($payload['branch_id'] ?? null);
        $supplier = Supplier::query()->findOrFail(
            $this->resolveRequired($device, 'supplier', (string) ($payload['local_supplier_id'] ?? ''))
        );

        $localLines = collect($payload['lines'] ?? []);
        $data = [
            'notes' => $payload['notes'] ?? null,
            'lines' => $localLines->map(fn (array $line): array => [
                'inventory_item_id' => $this->resolveRequired(
                    $device,
                    'inventory_item',
                    (string) ($line['local_inventory_item_id'] ?? '')
                ),
                'purchase_quantity' => (string) ($line['purchase_quantity'] ?? '0'),
                'unit_cost' => (string) ($line['unit_cost'] ?? '0'),
            ])->all(),
        ];

        $po = $this->procurement->createPurchaseOrder($branch, $supplier, $actor, $data);
        $this->link($device, 'purchase_order', $localId, $po->id);

        $cloudLines = $po->lines->values();
        foreach ($localLines->values() as $index => $line) {
            if (isset($cloudLines[$index])) {
                $this->link(
                    $device,
                    'purchase_order_line',
                    (string) ($line['local_line_id'] ?? ''),
                    $cloudLines[$index]->id,
                );
            }
        }

        return ['entity_type' => 'purchase_order', 'entity_id' => $po->id];
    }

    private function postGoodsReceipt(DeviceActivation $device, TenantUser $actor, string $localId, array $payload): array
    {
        $po = PurchaseOrder::query()->findOrFail(
            $this->resolveRequired($device, 'purchase_order', (string) ($payload['local_purchase_order_id'] ?? ''))
        );

        $data = [
            'client_receipt_id' => $payload['client_receipt_id'] ?? $localId,
            'notes' => $payload['notes'] ?? null,
            'lines' => collect($payload['lines'] ?? [])->map(fn (array $line): array => [
                'purchase_order_line_id' => $this->resolveRequired(
                    $device,
                    'purchase_order_line',
                    (string) ($line['local_purchase_order_line_id'] ?? '')
                ),
                'purchase_quantity' => (string) ($line['purchase_quantity'] ?? '0'),
            ])->all(),
        ];

        $receipt = $this->procurement->receive($po, $actor, $data);
        $this->link($device, 'goods_receipt', $localId, $receipt->id);

        return ['entity_type' => 'goods_receipt', 'entity_id' => $receipt->id];
    }

    private function finalizeClosing(DeviceActivation $device, TenantUser $actor, string $localId, array $payload): array
    {
        $branch = RestaurantBranch::query()->findOrFail($payload['branch_id'] ?? null);
        $closing = $this->closings->finalize(
            $branch,
            (string) ($payload['business_date'] ?? ''),
            $actor,
        );
        $this->link($device, 'daily_closing', $localId, $closing->id);

        return ['entity_type' => 'daily_closing', 'entity_id' => $closing->id];
    }

    private function reopenClosing(DeviceActivation $device, TenantUser $actor, string $localId, array $payload): array
    {
        $closing = DailyClosing::query()->findOrFail(
            $this->resolveRequired($device, 'daily_closing', $localId)
        );
        $closing = $this->closings->reopen(
            $closing,
            $actor,
            (string) ($payload['reason'] ?? 'Desktop reconciliation'),
        );

        return ['entity_type' => 'daily_closing', 'entity_id' => $closing->id];
    }

    private function storeOperationalRecord(
        DeviceActivation $device,
        TenantUser $actor,
        string $recordType,
        string $localId,
        array $payload,
    ): array {
        $record = DesktopOperationalRecord::query()->updateOrCreate(
            [
                'central_device_id' => $device->id,
                'record_type' => $recordType,
                'local_entity_id' => $localId,
            ],
            [
                'actor_user_id' => $actor->getKey(),
                'payload' => $payload,
                'occurred_at' => now(),
            ],
        );

        return ['entity_type' => 'desktop_operational_record', 'entity_id' => $record->id];
    }

    private function link(DeviceActivation $device, string $type, string $localId, string $cloudId): void
    {
        if ($localId === '') {
            return;
        }

        DesktopEntityLink::query()->updateOrCreate(
            [
                'central_device_id' => $device->id,
                'entity_type' => $type,
                'local_entity_id' => $localId,
            ],
            ['cloud_entity_id' => $cloudId],
        );
    }

    private function resolve(DeviceActivation $device, string $type, string $localId): ?string
    {
        return DesktopEntityLink::query()
            ->where('central_device_id', $device->id)
            ->where('entity_type', $type)
            ->where('local_entity_id', $localId)
            ->value('cloud_entity_id');
    }

    private function resolveRequired(DeviceActivation $device, string $type, string $localId): string
    {
        return $this->resolve($device, $type, $localId)
            ?? throw ValidationException::withMessages([
                'dependency' => "The cloud mapping for {$type} is not available yet.",
            ]);
    }

    private function actor(TenantUser $requestUser, string $publicId): TenantUser
    {
        if ($publicId === '' || $publicId === $requestUser->public_id) {
            return $requestUser;
        }

        return TenantUser::query()
            ->where('public_id', $publicId)
            ->where('is_active', true)
            ->firstOrFail();
    }

    private function changeSnapshot(SyncChange $change): ?array
    {
        if ($change->operation === 'delete') {
            return $change->payload;
        }

        return match ($change->entity_type) {
            'order' => Order::query()
                ->with(['table.diningArea.branch', 'waiter', 'items', 'kitchenTickets.station'])
                ->find($change->entity_id)
                ?->toArray(),
            'bill' => Bill::query()
                ->with(['lines', 'payments', 'order.table'])
                ->find($change->entity_id)
                ?->toArray(),
            'cashier_session' => CashierSession::query()
                ->with(['branch', 'cashier'])
                ->find($change->entity_id)
                ?->toArray(),
            'inventory_item' => InventoryItem::query()
                ->with('balances')
                ->find($change->entity_id)
                ?->toArray(),
            'inventory_balance' => InventoryBalance::query()
                ->with(['branch', 'item'])
                ->find($change->entity_id)
                ?->toArray(),
            'stock_movement' => StockMovement::query()
                ->with(['branch', 'item'])
                ->find($change->entity_id)
                ?->toArray(),
            'supplier' => Supplier::query()
                ->find($change->entity_id)
                ?->toArray(),
            'recipe' => Recipe::query()
                ->with(['branch', 'menuItem', 'items.inventoryItem'])
                ->find($change->entity_id)
                ?->toArray(),
            'purchase_order' => PurchaseOrder::query()
                ->with(['branch', 'supplier', 'lines.item', 'receipts.lines.item'])
                ->find($change->entity_id)
                ?->toArray(),
            'goods_receipt' => GoodsReceipt::query()
                ->with(['purchaseOrder', 'supplier', 'branch', 'lines.item'])
                ->find($change->entity_id)
                ?->toArray(),
            'daily_closing' => DailyClosing::query()
                ->with(['branch', 'snapshots', 'events'])
                ->find($change->entity_id)
                ?->toArray(),
            'dining_table' => DiningTable::query()
                ->with('diningArea.branch')
                ->find($change->entity_id)
                ?->toArray(),
            'menu_item' => MenuItem::query()
                ->find($change->entity_id)
                ?->toArray(),
            default => $change->payload,
        };
    }

    private function failure(
        TenantUser $requestUser,
        DeviceActivation $device,
        array $mutation,
        string $hash,
        string $status,
        string $code,
        string $message,
    ): array {
        $response = $this->basicResult(
            (string) ($mutation['mutation_id'] ?? ''),
            $status,
            (string) ($mutation['operation'] ?? ''),
            $code,
            $message,
        );

        SyncMutation::query()->create([
            'central_device_id' => $device->id,
            'tenant_user_id' => $requestUser->getKey(),
            'mutation_id' => (string) ($mutation['mutation_id'] ?? ''),
            'operation' => (string) ($mutation['operation'] ?? ''),
            'entity_type' => $mutation['entity_type'] ?? null,
            'entity_id' => null,
            'status' => $status,
            'request_hash' => $hash,
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
        return match (array_key_first($exception->errors())) {
            'dining_table_id' => 'table_busy',
            'order', 'bill', 'closing', 'purchase_order', 'session' => 'state_conflict',
            'dependency' => 'dependency_missing',
            'menu_item_id' => 'menu_unavailable',
            'operation' => 'unsupported_operation',
            default => 'invalid_payload',
        };
    }

    private function basicResult(
        string $mutationId,
        string $status,
        string $operation,
        ?string $code,
        ?string $message,
    ): array {
        return [
            'mutation_id' => $mutationId,
            'status' => $status,
            'operation' => $operation,
            'code' => $code,
            'message' => $message,
        ];
    }
}
