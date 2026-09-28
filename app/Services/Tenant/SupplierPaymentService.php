<?php

namespace App\Services\Tenant;

use App\Models\ChartAccount;
use App\Models\RestaurantBranch;
use App\Models\Supplier;
use App\Models\SupplierPayment;
use App\Models\TenantUser;
use App\Support\Money;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;

class SupplierPaymentService
{
    public function __construct(
        private readonly AccountingService $accounting,
    ) {}

    public function post(
        RestaurantBranch $branch,
        Supplier $supplier,
        ChartAccount $paymentAccount,
        TenantUser $actor,
        array $data,
    ): SupplierPayment {
        return DB::connection('tenant')->transaction(function () use (
            $branch,
            $supplier,
            $paymentAccount,
            $actor,
            $data,
        ): SupplierPayment {
            if (! empty($data['client_supplier_payment_id'])) {
                $existing = SupplierPayment::query()
                    ->where('client_supplier_payment_id', $data['client_supplier_payment_id'])
                    ->first();

                if ($existing) {
                    return $existing;
                }
            }

            if ($paymentAccount->type !== ChartAccount::TYPE_ASSET) {
                throw ValidationException::withMessages([
                    'payment_account_id' => 'Supplier payment account must be an asset account.',
                ]);
            }

            $amount = Money::fromMinor(Money::toMinor((string) $data['amount']));
            $payableMinor = $this->accounting->counterpartyBalanceMinor(
                'accounts_payable',
                'supplier',
                $supplier->id,
                $branch->id,
            );

            if (Money::toMinor($amount) <= 0 || Money::toMinor($amount) > $payableMinor) {
                throw ValidationException::withMessages([
                    'amount' => 'Supplier payment must be positive and cannot exceed the payable balance.',
                ]);
            }

            $payment = SupplierPayment::query()->create([
                'branch_id' => $branch->id,
                'supplier_id' => $supplier->id,
                'payment_account_id' => $paymentAccount->id,
                'paid_by_user_id' => $actor->getKey(),
                'payment_number' => 'SP-'.strtoupper((string) Str::ulid()),
                'client_supplier_payment_id' => $data['client_supplier_payment_id'] ?? null,
                'amount' => $amount,
                'payment_date' => $data['payment_date'],
                'reference' => $data['reference'] ?? null,
                'status' => 'posted',
            ]);

            $this->accounting->post(
                $branch->id,
                $actor,
                'supplier_payment',
                $payment->id,
                'posted',
                'Supplier payment '.$payment->payment_number,
                $payment->payment_date->format('Y-m-d'),
                [
                    [
                        'account_id' => $this->accounting->systemAccount('accounts_payable')->id,
                        'debit' => $amount,
                        'counterparty_type' => 'supplier',
                        'counterparty_id' => $supplier->id,
                    ],
                    [
                        'account_id' => $paymentAccount->id,
                        'credit' => $amount,
                        'counterparty_type' => 'supplier',
                        'counterparty_id' => $supplier->id,
                    ],
                ],
                'supplier-payment:'.$payment->id.':posted',
            );

            return $payment->load(['branch', 'supplier', 'paymentAccount']);
        });
    }
}
