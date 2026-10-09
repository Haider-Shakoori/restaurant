<?php

namespace App\Services\Tenant;

use App\Models\KitchenTicketItem;
use App\Models\Order;
use App\Models\WaiterPushDevice;
use Illuminate\Support\Facades\DB;

class WaiterPickupPushOutbox
{
    /**
     * Write a durable per-device notification while the kitchen state change
     * is in its tenant transaction. A rollback must also erase this event.
     */
    public function recordReady(KitchenTicketItem $item): void
    {
        $item->loadMissing('ticket.order');
        $order = $item->ticket?->order;
        if (! $order || ! $order->waiter_id || $order->status === Order::STATUS_SERVED) {
            return;
        }

        $devices = WaiterPushDevice::query()
            ->where('tenant_user_id', $order->waiter_id)
            ->where('enabled', true)
            ->pluck('central_device_id');

        foreach ($devices as $deviceId) {
            DB::connection('tenant')->table('waiter_push_deliveries')->insertOrIgnore([
                'central_device_id' => $deviceId,
                'ready_item_id' => $item->id,
                'order_id' => $order->id,
                'event_key' => 'kot-ready:'.$item->id,
                'expires_at' => now()->addHours(2),
                'created_at' => now(),
                'updated_at' => now(),
            ]);
        }
    }
}
