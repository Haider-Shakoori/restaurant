<?php

namespace App\Console\Commands;

use App\Models\KitchenTicketItem;
use App\Models\Order;
use App\Models\Tenant;
use App\Models\WaiterPushDevice;
use App\Services\Tenant\FirebaseCloudMessaging;
use Illuminate\Console\Command;
use Illuminate\Support\Facades\DB;
use Throwable;

class DeliverWaiterReadyPush extends Command
{
    protected $signature = 'restaurant:deliver-ready-push {--limit=80}';

    protected $description = 'Retry authorized waiter pickup pushes across restaurant tenants';

    public function handle(FirebaseCloudMessaging $fcm): int
    {
        if (! $fcm->configured()) {
            $this->components->warn('FCM service account not configured; queued alerts preserved.');
            return self::SUCCESS;
        }

        Tenant::query()->orderBy('id')->chunk(50, function ($tenants) use ($fcm): void {
            foreach ($tenants as $tenant) {
                try {
                    tenancy()->initialize($tenant);
                    $this->deliverTenant($fcm);
                } catch (Throwable $e) {
                    // No tokens, request bodies or private keys in operational logs.
                    report($e);
                } finally {
                    if (tenancy()->initialized) {
                        tenancy()->end();
                    }
                }
            }
        });

        return self::SUCCESS;
    }

    private function deliverTenant(FirebaseCloudMessaging $fcm): void
    {
        $limit = min(500, max(1, (int) $this->option('limit')));
        $rows = DB::connection('tenant')->table('waiter_push_deliveries')
            ->whereNull('sent_at')
            ->where('expires_at', '>', now())
            ->where(function ($query): void {
                $query->whereNull('next_attempt_at')
                    ->orWhere('next_attempt_at', '<=', now());
            })
            ->orderBy('id')
            ->limit($limit)->get();

        foreach ($rows as $row) {
            $device = WaiterPushDevice::query()
                ->where('central_device_id', $row->central_device_id)
                ->where('enabled', true)
                ->first();
            $item = KitchenTicketItem::query()
                ->with('ticket.order.table')
                ->find($row->ready_item_id);
            $order = $item?->ticket?->order;
            if (! $device || ! $order || $order->id !== $row->order_id ||
                (string) $order->waiter_id !== (string) $device->tenant_user_id ||
                $item->status !== KitchenTicketItem::STATUS_READY ||
                in_array($order->status, [
                    Order::STATUS_SERVED, Order::STATUS_BILLED,
                    Order::STATUS_CLOSED, Order::STATUS_CANCELLED,
                ], true)) {
                $this->finish($row->id, 'stale');
                continue;
            }

            try {
                $reference = $order->table?->name ??
                    ($order->service_reference ?: strtoupper((string) $order->service_type));
                $status = $fcm->send($device->fcm_token, $reference, $order->id, $item->id);
                if ($status === 'invalid') {
                    $device->update(['enabled' => false]);
                }
                $this->finish($row->id, $status);
            } catch (Throwable $e) {
                $attempts = $row->attempts + 1;
                DB::connection('tenant')->table('waiter_push_deliveries')
                    ->where('id', $row->id)
                    ->update([
                        'attempts' => $attempts,
                        'last_error' => substr($e instanceof \RuntimeException ?
                            $e->getMessage() : 'push_temporarily_unavailable', 0, 80),
                        'next_attempt_at' => now()->addSeconds(min(900, 30 * (2 ** min(5, $attempts)))),
                        'updated_at' => now(),
                    ]);
            }
        }
    }

    private function finish(int $id, string $status): void
    {
        DB::connection('tenant')->table('waiter_push_deliveries')->where('id', $id)
            ->update([
                'sent_at' => now(),
                'last_error' => $status === 'sent' ? null : $status,
                'updated_at' => now(),
            ]);
    }
}
