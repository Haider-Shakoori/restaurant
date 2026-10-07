<?php

namespace App\Services\Tenant;

use App\Models\Bill;
use App\Models\BillEvent;
use App\Models\CashierSession;
use App\Models\DiningTable;
use App\Models\Order;
use App\Models\TenantPayment;
use App\Models\TenantUser;
use App\Support\Money;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;

class BillingService
{
    public function __construct(
        private readonly AccountingService $accounting,
    ) {}

    public function createBill(Order $order, TenantUser $actor): Bill
    {
        return DB::connection('tenant')->transaction(function () use ($order, $actor): Bill {
            $order = Order::query()
                ->with(['branch', 'table.diningArea.branch', 'items', 'bill'])
                ->lockForUpdate()
                ->findOrFail($order->getKey());

            if ($order->bill) {
                return $order->bill->load(['lines', 'payments', 'order.table']);
            }

            if ($order->status !== Order::STATUS_SERVED) {
                throw ValidationException::withMessages([
                    'order' => 'A bill can only be issued after the order has been served.',
                ]);
            }

            $subtotalMinor = 0;

            foreach ($order->items as $item) {
                $subtotalMinor += Money::toMinor((string) $item->line_total);
            }

            $subtotal = Money::fromMinor($subtotalMinor);

            $bill = Bill::query()->create([
                'order_id' => $order->id,
                'branch_id' => $order->branch_id ?? $order->table?->diningArea?->branch_id,
                'created_by_user_id' => $actor->getKey(),
                'bill_number' => 'BILL-'.strtoupper((string) Str::ulid()),
                'status' => Bill::STATUS_OPEN,
                'subtotal' => $subtotal,
                'discount_amount' => '0.00',
                'total' => $subtotal,
                'paid_amount' => '0.00',
                'balance_due' => $subtotal,
                'issued_at' => now(),
            ]);

            foreach ($order->items as $item) {
                $bill->lines()->create([
                    'order_item_id' => $item->id,
                    'item_name' => $item->item_name,
                    'quantity' => $item->quantity,
                    'unit_price' => $item->unit_price,
                    'line_total' => $item->line_total,
                ]);
            }

            $order->update(['status' => Order::STATUS_BILLED]);

            $this->billEvent($bill, $actor, 'bill.issued', [
                'subtotal' => $subtotal,
                'order_id' => $order->id,
            ]);

            $this->accounting->postBillIssued($bill->fresh(), $actor);

            return $bill->load(['lines', 'payments', 'order.table']);
        });
    }

    public function applyDiscount(
        Bill $bill,
        TenantUser $actor,
        string $type,
        string $value,
        ?string $reason,
    ): Bill {
        return DB::connection('tenant')->transaction(function () use ($bill, $actor, $type, $value, $reason): Bill {
            $bill = Bill::query()->lockForUpdate()->findOrFail($bill->getKey());

            if ($bill->status !== Bill::STATUS_OPEN) {
                throw ValidationException::withMessages([
                    'bill' => 'Discounts can only be changed on an open bill.',
                ]);
            }

            if ($bill->payments()->where('status', TenantPayment::STATUS_POSTED)->exists()) {
                throw ValidationException::withMessages([
                    'discount' => 'Discount cannot be changed after a payment has been posted.',
                ]);
            }

            if (! in_array($type, ['fixed', 'percent'], true)) {
                throw ValidationException::withMessages([
                    'discount_type' => 'Discount type must be fixed or percent.',
                ]);
            }

            $oldDiscount = (string) $bill->discount_amount;
            $subtotal = (string) $bill->subtotal;

            if ($type === 'fixed') {
                $discountMinor = Money::toMinor($value);

                if ($discountMinor < 0 || $discountMinor > Money::toMinor($subtotal)) {
                    throw ValidationException::withMessages([
                        'discount_value' => 'Fixed discount must be between zero and the bill subtotal.',
                    ]);
                }

                $discount = Money::fromMinor($discountMinor);
            } else {
                $percentHundredths = $this->percentHundredths($value);

                if ($percentHundredths < 0 || $percentHundredths > 10000) {
                    throw ValidationException::withMessages([
                        'discount_value' => 'Percentage discount must be between 0 and 100.',
                    ]);
                }

                $discount = Money::percentage($subtotal, $value);
            }

            $total = Money::subtract($subtotal, $discount);

            $bill->update([
                'discount_type' => $type,
                'discount_value' => $value,
                'discount_amount' => $discount,
                'discount_reason' => $reason,
                'total' => $total,
                'balance_due' => $total,
            ]);

            $event = $this->billEvent($bill, $actor, 'bill.discount_applied', [
                'type' => $type,
                'value' => $value,
                'amount' => $discount,
                'reason' => $reason,
            ]);

            $this->accounting->postDiscountDelta(
                $bill->fresh(),
                $actor,
                $oldDiscount,
                $discount,
                $event->id,
            );

            if (Money::toMinor($total) === 0) {
                $this->settleBill($bill, $actor);
            }

            return $bill->fresh()->load(['lines', 'payments', 'order.table']);
        });
    }

    public function addPayment(
        Bill $bill,
        CashierSession $session,
        TenantUser $actor,
        array $data,
    ): TenantPayment {
        return DB::connection('tenant')->transaction(function () use ($bill, $session, $actor, $data): TenantPayment {
            if (! empty($data['client_payment_id'])) {
                $existing = TenantPayment::query()
                    ->where('client_payment_id', $data['client_payment_id'])
                    ->first();

                if ($existing) {
                    abort_unless($existing->bill_id === $bill->id, 409);

                    return $existing->load(['bill', 'session']);
                }
            }

            $bill = Bill::query()->with('order.table')->lockForUpdate()->findOrFail($bill->getKey());
            $session = CashierSession::query()->lockForUpdate()->findOrFail($session->getKey());

            if ($bill->status !== Bill::STATUS_OPEN) {
                throw ValidationException::withMessages([
                    'bill' => 'This bill is not open for payment.',
                ]);
            }

            if ($session->status !== CashierSession::STATUS_OPEN) {
                throw ValidationException::withMessages([
                    'cashier_session_id' => 'The cashier session is closed.',
                ]);
            }

            if ($bill->branch_id !== $session->branch_id) {
                throw ValidationException::withMessages([
                    'cashier_session_id' => 'The cashier session belongs to another branch.',
                ]);
            }

            $this->authorizeSession($session, $actor);

            if (! in_array($data['method'], TenantPayment::METHODS, true)) {
                throw ValidationException::withMessages([
                    'method' => 'Unsupported payment method.',
                ]);
            }

            $amountMinor = Money::toMinor((string) $data['amount']);

            if ($amountMinor <= 0 || $amountMinor > Money::toMinor((string) $bill->balance_due)) {
                throw ValidationException::withMessages([
                    'amount' => 'Payment must be positive and cannot exceed the remaining balance.',
                ]);
            }

            $payment = TenantPayment::query()->create([
                'bill_id' => $bill->id,
                'cashier_session_id' => $session->id,
                'received_by_user_id' => $actor->getKey(),
                'client_payment_id' => $data['client_payment_id'] ?? null,
                'method' => $data['method'],
                'amount' => Money::fromMinor($amountMinor),
                'reference' => $data['reference'] ?? null,
                'status' => TenantPayment::STATUS_POSTED,
                'received_at' => now(),
            ]);

            $paid = (string) $bill->payments()
                ->where('status', TenantPayment::STATUS_POSTED)
                ->sum('amount');

            $balance = Money::subtract((string) $bill->total, $paid);

            $bill->update([
                'paid_amount' => $paid,
                'balance_due' => $balance,
            ]);

            $this->billEvent($bill, $actor, 'payment.posted', [
                'payment_id' => $payment->id,
                'method' => $payment->method,
                'amount' => $payment->amount,
            ]);

            $this->accounting->postPayment($payment->load('bill'), $actor);

            if (Money::toMinor($balance) === 0) {
                $this->settleBill($bill, $actor);
            }

            return $payment->fresh()->load(['bill.order.table', 'session']);
        });
    }

    private function settleBill(Bill $bill, TenantUser $actor): void
    {
        $bill->update([
            'status' => Bill::STATUS_PAID,
            'paid_at' => now(),
            'balance_due' => '0.00',
        ]);

        $order = Order::query()->lockForUpdate()->findOrFail($bill->order_id);
        $table = $order->dining_table_id
            ? DiningTable::query()->lockForUpdate()->find($order->dining_table_id)
            : null;

        $order->update([
            'status' => Order::STATUS_CLOSED,
            'closed_at' => now(),
        ]);

        $table?->update(['status' => DiningTable::STATUS_AVAILABLE]);

        $this->billEvent($bill, $actor, 'bill.paid', [
            'paid_amount' => $bill->fresh()->paid_amount,
            'table_id' => $table?->id,
            'service_type' => $order->service_type,
        ]);

        $order->events()->create([
            'actor_user_id' => $actor->getKey(),
            'event_type' => 'order.closed',
            'from_status' => Order::STATUS_BILLED,
            'to_status' => Order::STATUS_CLOSED,
            'occurred_at' => now(),
        ]);
    }

    private function billEvent(
        Bill $bill,
        TenantUser $actor,
        string $eventType,
        array $payload = [],
    ): BillEvent {
        return $bill->events()->create([
            'actor_user_id' => $actor->getKey(),
            'event_type' => $eventType,
            'payload' => $payload ?: null,
            'occurred_at' => now(),
        ]);
    }

    private function authorizeSession(CashierSession $session, TenantUser $actor): void
    {
        if ((int) $session->cashier_user_id === (int) $actor->getKey()) {
            return;
        }

        if ($actor->hasAnyRole(['owner', 'admin', 'manager'])) {
            return;
        }

        abort(403);
    }

    private function percentHundredths(string $value): int
    {
        $normalized = trim($value);

        if (! preg_match('/^\d+(?:\.\d{1,2})?$/', $normalized)) {
            throw ValidationException::withMessages([
                'discount_value' => 'Percentage discount may have at most two decimal places.',
            ]);
        }

        [$whole, $fraction] = array_pad(explode('.', $normalized, 2), 2, '');
        $fraction = str_pad($fraction, 2, '0');

        return ((int) $whole * 100) + (int) $fraction;
    }
}
