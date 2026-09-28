<?php

namespace App\Services\Tenant;

use App\Models\Bill;
use App\Models\CashierSession;
use App\Models\DailyClosing;
use App\Models\RestaurantBranch;
use App\Models\TenantPayment;
use App\Models\TenantUser;
use App\Support\Money;
use Illuminate\Support\Facades\DB;
use Illuminate\Validation\ValidationException;

class DailyClosingService
{
    public function finalize(
        RestaurantBranch $branch,
        string $businessDate,
        TenantUser $actor,
    ): DailyClosing {
        return DB::connection('tenant')->transaction(function () use ($branch, $businessDate, $actor): DailyClosing {
            $openSessions = CashierSession::query()
                ->where('branch_id', $branch->id)
                ->where('status', CashierSession::STATUS_OPEN)
                ->whereDate('opened_at', '<=', $businessDate)
                ->exists();

            if ($openSessions) {
                throw ValidationException::withMessages([
                    'closing' => 'Close all cashier sessions for this business date before finalizing.',
                ]);
            }

            $openBills = Bill::query()
                ->where('branch_id', $branch->id)
                ->where('status', Bill::STATUS_OPEN)
                ->whereDate('issued_at', '<=', $businessDate)
                ->exists();

            if ($openBills) {
                throw ValidationException::withMessages([
                    'closing' => 'Settle all open bills for this business date before finalizing.',
                ]);
            }

            $closing = DailyClosing::query()->firstOrCreate(
                [
                    'branch_id' => $branch->id,
                    'business_date' => $businessDate,
                ],
                [
                    'status' => DailyClosing::STATUS_OPEN,
                    'created_by_user_id' => $actor->getKey(),
                ],
            );

            $closing = DailyClosing::query()->lockForUpdate()->findOrFail($closing->id);

            if ($closing->status === DailyClosing::STATUS_FINALIZED) {
                return $closing->load(['branch', 'snapshots', 'events']);
            }

            $bills = Bill::query()
                ->where('branch_id', $branch->id)
                ->where('status', Bill::STATUS_PAID)
                ->whereDate('paid_at', $businessDate)
                ->get();

            $payments = TenantPayment::query()
                ->where('status', TenantPayment::STATUS_POSTED)
                ->whereDate('received_at', $businessDate)
                ->whereHas('bill', fn ($query) => $query->where('branch_id', $branch->id))
                ->get();

            $sessions = CashierSession::query()
                ->where('branch_id', $branch->id)
                ->where('status', CashierSession::STATUS_CLOSED)
                ->whereDate('closed_at', $businessDate)
                ->get();

            $gross = $this->sum($bills->pluck('subtotal')->all());
            $discounts = $this->sum($bills->pluck('discount_amount')->all());
            $net = $this->sum($bills->pluck('total')->all());
            $paymentsTotal = $this->sum($payments->pluck('amount')->all());

            $method = fn (string $name): string => $this->sum(
                $payments->where('method', $name)->pluck('amount')->all()
            );

            $expected = $this->sum($sessions->pluck('expected_cash')->filter()->all());
            $declared = $this->sum($sessions->pluck('declared_cash')->filter()->all());
            $variance = $this->sum($sessions->pluck('cash_variance')->filter()->all());

            $version = ((int) $closing->snapshots()->max('version')) + 1;

            $closing->snapshots()->create([
                'version' => $version,
                'finalized_by_user_id' => $actor->getKey(),
                'bill_count' => $bills->count(),
                'payment_count' => $payments->count(),
                'cashier_session_count' => $sessions->count(),
                'gross_sales' => $gross,
                'discounts' => $discounts,
                'net_sales' => $net,
                'payments_total' => $paymentsTotal,
                'cash_payments' => $method('cash'),
                'card_payments' => $method('card'),
                'bank_payments' => $method('bank'),
                'mobile_money_payments' => $method('mobile_money'),
                'other_payments' => $method('other'),
                'expected_cash' => $expected,
                'declared_cash' => $declared,
                'cash_variance' => $variance,
                'finalized_at' => now(),
            ]);

            $closing->update([
                'status' => DailyClosing::STATUS_FINALIZED,
                'finalized_at' => now(),
                'reopened_at' => null,
            ]);

            $this->event($closing, $actor, 'daily_closing.finalized', [
                'version' => $version,
                'net_sales' => $net,
                'payments_total' => $paymentsTotal,
            ]);

            return $closing->fresh()->load(['branch', 'snapshots', 'events']);
        });
    }

    public function reopen(
        DailyClosing $closing,
        TenantUser $actor,
        string $reason,
    ): DailyClosing {
        return DB::connection('tenant')->transaction(function () use ($closing, $actor, $reason): DailyClosing {
            $closing = DailyClosing::query()->lockForUpdate()->findOrFail($closing->getKey());

            if ($closing->status !== DailyClosing::STATUS_FINALIZED) {
                throw ValidationException::withMessages([
                    'closing' => 'Only a finalized daily closing can be reopened.',
                ]);
            }

            $closing->update([
                'status' => DailyClosing::STATUS_REOPENED,
                'reopened_at' => now(),
            ]);

            $this->event($closing, $actor, 'daily_closing.reopened', [
                'reason' => $reason,
            ]);

            return $closing->fresh()->load(['branch', 'snapshots', 'events']);
        });
    }

    private function sum(array $amounts): string
    {
        $minor = 0;

        foreach ($amounts as $amount) {
            $minor += Money::toMinor((string) $amount);
        }

        return Money::fromMinor($minor);
    }

    private function event(
        DailyClosing $closing,
        TenantUser $actor,
        string $eventType,
        array $payload = [],
    ): void {
        $closing->events()->create([
            'actor_user_id' => $actor->getKey(),
            'event_type' => $eventType,
            'payload' => $payload ?: null,
            'occurred_at' => now(),
        ]);
    }
}
