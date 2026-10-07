<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\TakeOrderRequest;
use App\Models\Order;
use App\Models\TenantUser;
use App\Services\Tenant\OrderService;
use Illuminate\Http\RedirectResponse;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;

class TenantWebOrderController extends Controller
{
    public function store(TakeOrderRequest $request, OrderService $orders): RedirectResponse
    {
        /** @var TenantUser $user */
        $user = $request->user();
        $data = $request->validated();

        if (! empty($data['existing_order_id'])) {
            $order = Order::query()->findOrFail($data['existing_order_id']);
            OrderController::authorizeOrder($request, $order);

            $order = DB::connection('tenant')->transaction(function () use ($orders, $order, $user, $data): Order {
                foreach ($data['lines'] as $line) {
                    $orders->addItem($order, $user, [
                        ...$line,
                        'client_line_id' => $line['client_line_id'] ?? (string) Str::ulid(),
                    ]);
                }

                if (($data['submit_action'] ?? 'kitchen') === 'kitchen') {
                    return $orders->submit(
                        $order->fresh(),
                        $user,
                        $data['client_mutation_id'] ?? (string) Str::uuid(),
                    );
                }

                return $order->fresh()->load(['items', 'kotRounds.tickets']);
            });

            return redirect('/orders')
                ->with(
                    'status',
                    ($data['submit_action'] ?? 'kitchen') === 'kitchen'
                        ? 'New items sent as another KOT round.'
                        : 'New items saved on the active order.',
                )
                ->with('created_order_id', $order->id);
        }

        $order = $orders->take($user, $data);

        $message = $order->status === 'draft'
            ? 'Order saved as draft.'
            : 'Order submitted to Kitchen successfully.';

        return redirect('/orders')
            ->with('status', $message)
            ->with('created_order_id', $order->id);
    }
}
