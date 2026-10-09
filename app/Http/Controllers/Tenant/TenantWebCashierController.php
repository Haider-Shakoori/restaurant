<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\Bill;
use App\Models\CashierSession;
use App\Models\Order;
use App\Models\RestaurantBranch;
use App\Services\Tenant\BillingService;
use App\Services\Tenant\CashierService;
use App\Services\Tenant\KitchenService;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;

class TenantWebCashierController extends Controller
{
    public function serve(Request $request, Order $order, KitchenService $kitchen): RedirectResponse
    {
        $this->authorizeWaiterOrder($request, $order);
        $kitchen->serve($order, $request->user('tenant'));

        return redirect('/orders')->with('status', 'Order marked served. You can now issue its bill.');
    }

    public function bill(Request $request, Order $order, BillingService $billing): RedirectResponse
    {
        $billing->createBill($order, $request->user('tenant'));

        return redirect('/orders')->with('status', 'Bill issued. Collect the balance in Orders or POS.');
    }

    public function openSession(Request $request, CashierService $cashiers): RedirectResponse
    {
        $data = $request->validate([
            'branch_id' => ['required', 'exists:branches,id'],
            'opening_cash' => ['required', 'decimal:0,2', 'min:0'],
        ]);

        $cashiers->openSession(
            RestaurantBranch::query()->findOrFail($data['branch_id']),
            $request->user('tenant'),
            (string) $data['opening_cash'],
        );

        return redirect('/pos')->with('status', 'Cashier session opened.');
    }

    public function pay(Request $request, Bill $bill, BillingService $billing): RedirectResponse
    {
        $data = $request->validate([
            'cashier_session_id' => ['required', 'exists:cashier_sessions,id'],
            'client_payment_id' => ['required', 'string', 'max:50'],
            'method' => ['required', 'in:cash,card,bank,mobile_money,other'],
            'amount' => ['required', 'decimal:0,2', 'gt:0'],
            'reference' => ['nullable', 'string', 'max:255'],
        ]);

        $payment = $billing->addPayment(
            $bill,
            CashierSession::query()->findOrFail($data['cashier_session_id']),
            $request->user('tenant'),
            $data,
        );

        return redirect('/orders')->with('status',
            $payment->bill->status === Bill::STATUS_PAID
                ? 'Bill paid. The order is closed and its table is available.'
                : 'Partial payment recorded. The table remains occupied until the bill is settled.');
    }

    private function authorizeWaiterOrder(Request $request, Order $order): void
    {
        $user = $request->user('tenant');

        abort_if($user?->role === 'waiter' && (int) $order->waiter_id !== (int) $user->id, 403);
    }
}
