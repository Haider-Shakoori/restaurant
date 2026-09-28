<?php

namespace App\Services\Tenant;

use App\Models\CashierSession;
use App\Models\RestaurantBranch;
use App\Models\TenantPayment;
use App\Models\TenantUser;
use App\Support\Money;
use Illuminate\Support\Facades\DB;
use Illuminate\Validation\ValidationException;

class CashierService
{
    public function __construct(
        private readonly AccountingService $accounting,
    ) {}

    public function openSession(
        RestaurantBranch $branch,
        TenantUser $cashier,
        string $openingCash,
    ): CashierSession {
        return DB::connection('tenant')->transaction(function () use ($branch, $cashier, $openingCash): CashierSession {
            $existing = CashierSession::query()
                ->where('cashier_user_id', $cashier->getKey())
                ->where('status', CashierSession::STATUS_OPEN)
                ->lockForUpdate()
                ->first();

            if ($existing) {
                throw ValidationException::withMessages([
                    'session' => 'This cashier already has an open session.',
                ]);
            }

            if (! $branch->is_active) {
                throw ValidationException::withMessages([
                    'branch_id' => 'The selected branch is inactive.',
                ]);
            }

            if (Money::toMinor($openingCash) < 0) {
                throw ValidationException::withMessages([
                    'opening_cash' => 'Opening cash cannot be negative.',
                ]);
            }

            return CashierSession::query()->create([
                'branch_id' => $branch->getKey(),
                'cashier_user_id' => $cashier->getKey(),
                'status' => CashierSession::STATUS_OPEN,
                'opening_cash' => Money::fromMinor(Money::toMinor($openingCash)),
                'opened_at' => now(),
            ])->load(['branch', 'cashier']);
        });
    }

    public function closeSession(
        CashierSession $session,
        TenantUser $actor,
        string $declaredCash,
    ): CashierSession {
        return DB::connection('tenant')->transaction(function () use ($session, $actor, $declaredCash): CashierSession {
            $session = CashierSession::query()->lockForUpdate()->findOrFail($session->getKey());

            if ($session->status === CashierSession::STATUS_CLOSED) {
                return $session->load(['branch', 'cashier']);
            }

            $this->authorizeSession($session, $actor);

            if (Money::toMinor($declaredCash) < 0) {
                throw ValidationException::withMessages([
                    'declared_cash' => 'Declared cash cannot be negative.',
                ]);
            }

            $cashCollected = (string) TenantPayment::query()
                ->where('cashier_session_id', $session->id)
                ->where('status', TenantPayment::STATUS_POSTED)
                ->where('method', 'cash')
                ->sum('amount');

            $expected = Money::add((string) $session->opening_cash, $cashCollected);
            $declared = Money::fromMinor(Money::toMinor($declaredCash));
            $variance = Money::subtract($declared, $expected);

            $session->update([
                'status' => CashierSession::STATUS_CLOSED,
                'expected_cash' => $expected,
                'declared_cash' => $declared,
                'cash_variance' => $variance,
                'closed_at' => now(),
            ]);

            $this->accounting->postCashVariance(
                $session->branch_id,
                $actor,
                $session->id,
                $variance,
                now()->format('Y-m-d'),
            );

            return $session->fresh()->load(['branch', 'cashier']);
        });
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
}
