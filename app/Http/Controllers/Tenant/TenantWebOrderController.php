<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\TakeOrderRequest;
use App\Models\TenantUser;
use App\Services\Tenant\OrderService;
use Illuminate\Http\RedirectResponse;

class TenantWebOrderController extends Controller
{
    public function store(TakeOrderRequest $request, OrderService $orders): RedirectResponse
    {
        /** @var TenantUser $user */
        $user = $request->user();

        $order = $orders->take($user, $request->validated());

        $message = $order->status === 'draft'
            ? 'Order saved as draft.'
            : 'Order submitted to Kitchen successfully.';

        return redirect('/orders')
            ->with('status', $message)
            ->with('created_order_id', $order->id);
    }
}
