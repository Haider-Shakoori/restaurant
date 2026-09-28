<?php

namespace App\Services\Tenant;

use App\Models\ChartAccount;
use App\Models\OperatingExpense;
use App\Models\RestaurantBranch;
use App\Models\TenantUser;
use App\Support\Money;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;

class ExpenseService
{
    public function __construct(
        private readonly AccountingService $accounting,
    ) {}

    public function post(
        RestaurantBranch $branch,
        TenantUser $actor,
        ChartAccount $expenseAccount,
        ChartAccount $paymentAccount,
        array $data,
    ): OperatingExpense {
        return DB::connection('tenant')->transaction(function () use (
            $branch,
            $actor,
            $expenseAccount,
            $paymentAccount,
            $data,
        ): OperatingExpense {
            if (! empty($data['client_expense_id'])) {
                $existing = OperatingExpense::query()
                    ->where('client_expense_id', $data['client_expense_id'])
                    ->first();

                if ($existing) {
                    return $existing;
                }
            }

            if ($expenseAccount->type !== ChartAccount::TYPE_EXPENSE || $expenseAccount->normal_balance !== 'debit') {
                throw ValidationException::withMessages([
                    'expense_account_id' => 'Select an active debit-normal expense account.',
                ]);
            }

            if ($paymentAccount->type !== ChartAccount::TYPE_ASSET) {
                throw ValidationException::withMessages([
                    'payment_account_id' => 'Expense payment account must be an asset account.',
                ]);
            }

            $amount = Money::fromMinor(Money::toMinor((string) $data['amount']));

            if (Money::toMinor($amount) <= 0) {
                throw ValidationException::withMessages([
                    'amount' => 'Expense amount must be greater than zero.',
                ]);
            }

            $expense = OperatingExpense::query()->create([
                'branch_id' => $branch->id,
                'expense_account_id' => $expenseAccount->id,
                'payment_account_id' => $paymentAccount->id,
                'created_by_user_id' => $actor->getKey(),
                'expense_number' => 'EXP-'.strtoupper((string) Str::ulid()),
                'client_expense_id' => $data['client_expense_id'] ?? null,
                'description' => $data['description'],
                'amount' => $amount,
                'expense_date' => $data['expense_date'],
                'reference' => $data['reference'] ?? null,
                'status' => 'posted',
            ]);

            $this->accounting->post(
                $branch->id,
                $actor,
                'operating_expense',
                $expense->id,
                'posted',
                $expense->description,
                $expense->expense_date->format('Y-m-d'),
                [
                    [
                        'account_id' => $expenseAccount->id,
                        'debit' => $amount,
                    ],
                    [
                        'account_id' => $paymentAccount->id,
                        'credit' => $amount,
                    ],
                ],
                'operating-expense:'.$expense->id.':posted',
            );

            return $expense->load(['branch', 'expenseAccount', 'paymentAccount']);
        });
    }
}
