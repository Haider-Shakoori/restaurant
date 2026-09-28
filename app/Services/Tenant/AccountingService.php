<?php

namespace App\Services\Tenant;

use App\Models\Bill;
use App\Models\ChartAccount;
use App\Models\GoodsReceipt;
use App\Models\InventoryConsumption;
use App\Models\JournalEntry;
use App\Models\JournalLine;
use App\Models\StockMovement;
use App\Models\TenantPayment;
use App\Models\TenantUser;
use App\Support\Money;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;

class AccountingService
{
    public const SYSTEM_ACCOUNTS = [
        'cash' => ['1000', 'Cash', ChartAccount::TYPE_ASSET, 'debit', false],
        'card' => ['1010', 'Card Clearing', ChartAccount::TYPE_ASSET, 'debit', false],
        'bank' => ['1020', 'Bank', ChartAccount::TYPE_ASSET, 'debit', false],
        'mobile_money' => ['1030', 'Mobile Money', ChartAccount::TYPE_ASSET, 'debit', false],
        'other_payment' => ['1040', 'Other Payment Clearing', ChartAccount::TYPE_ASSET, 'debit', false],
        'accounts_receivable' => ['1100', 'Accounts Receivable', ChartAccount::TYPE_ASSET, 'debit', false],
        'inventory_asset' => ['1200', 'Inventory Asset', ChartAccount::TYPE_ASSET, 'debit', false],
        'accounts_payable' => ['2000', 'Accounts Payable', ChartAccount::TYPE_LIABILITY, 'credit', false],
        'owner_equity' => ['3000', 'Owner Equity', ChartAccount::TYPE_EQUITY, 'credit', false],
        'sales_revenue' => ['4000', 'Sales Revenue', ChartAccount::TYPE_REVENUE, 'credit', false],
        'sales_discounts' => ['4010', 'Sales Discounts', ChartAccount::TYPE_REVENUE, 'debit', true],
        'cost_of_goods_sold' => ['5000', 'Cost of Goods Sold', ChartAccount::TYPE_EXPENSE, 'debit', false],
        'operating_expense' => ['6000', 'Operating Expenses', ChartAccount::TYPE_EXPENSE, 'debit', false],
        'cash_over_short' => ['6100', 'Cash Over / Short', ChartAccount::TYPE_EXPENSE, 'debit', false],
        'inventory_adjustment' => ['6110', 'Inventory Adjustment', ChartAccount::TYPE_EXPENSE, 'debit', false],
    ];

    public function ensureSystemAccounts(): void
    {
        foreach (self::SYSTEM_ACCOUNTS as $key => [$code, $name, $type, $normalBalance, $isContra]) {
            ChartAccount::query()->firstOrCreate(
                ['system_key' => $key],
                [
                    'code' => $code,
                    'name' => $name,
                    'type' => $type,
                    'normal_balance' => $normalBalance,
                    'is_contra' => $isContra,
                    'is_active' => true,
                ],
            );
        }
    }

    public function systemAccount(string $key): ChartAccount
    {
        $this->ensureSystemAccounts();

        return ChartAccount::query()
            ->where('system_key', $key)
            ->firstOrFail();
    }

    public function post(
        ?string $branchId,
        TenantUser $actor,
        string $sourceType,
        string $sourceId,
        string $sourceAction,
        string $description,
        string $entryDate,
        array $lines,
        string $idempotencyKey,
    ): JournalEntry {
        return DB::connection('tenant')->transaction(function () use (
            $branchId,
            $actor,
            $sourceType,
            $sourceId,
            $sourceAction,
            $description,
            $entryDate,
            $lines,
            $idempotencyKey,
        ): JournalEntry {
            $existing = JournalEntry::query()
                ->where('idempotency_key', $idempotencyKey)
                ->first();

            if ($existing) {
                return $existing->load('lines.account');
            }

            $debitMinor = 0;
            $creditMinor = 0;

            foreach ($lines as $line) {
                $debit = Money::toMinor((string) ($line['debit'] ?? '0.00'));
                $credit = Money::toMinor((string) ($line['credit'] ?? '0.00'));

                if ($debit < 0 || $credit < 0 || ($debit > 0 && $credit > 0) || ($debit === 0 && $credit === 0)) {
                    throw ValidationException::withMessages([
                        'journal' => 'Each journal line must contain exactly one positive debit or credit.',
                    ]);
                }

                $debitMinor += $debit;
                $creditMinor += $credit;
            }

            if ($debitMinor !== $creditMinor || $debitMinor === 0) {
                throw ValidationException::withMessages([
                    'journal' => 'Journal entry is not balanced.',
                ]);
            }

            $entry = JournalEntry::query()->create([
                'branch_id' => $branchId,
                'posted_by_user_id' => $actor->getKey(),
                'entry_number' => 'JE-'.strtoupper((string) Str::ulid()),
                'source_type' => $sourceType,
                'source_id' => $sourceId,
                'source_action' => $sourceAction,
                'idempotency_key' => $idempotencyKey,
                'description' => $description,
                'entry_date' => $entryDate,
                'status' => JournalEntry::STATUS_POSTED,
                'posted_at' => now(),
            ]);

            foreach ($lines as $line) {
                $entry->lines()->create([
                    'account_id' => $line['account_id'],
                    'debit' => $line['debit'] ?? '0.00',
                    'credit' => $line['credit'] ?? '0.00',
                    'memo' => $line['memo'] ?? null,
                    'counterparty_type' => $line['counterparty_type'] ?? null,
                    'counterparty_id' => $line['counterparty_id'] ?? null,
                ]);
            }

            return $entry->load('lines.account');
        });
    }

    public function counterpartyBalanceMinor(
        string $systemKey,
        string $counterpartyType,
        string $counterpartyId,
        ?string $branchId = null,
    ): int {
        $account = $this->systemAccount($systemKey);

        $totals = JournalLine::query()
            ->join('journal_entries', 'journal_entries.id', '=', 'journal_lines.journal_entry_id')
            ->where('journal_entries.status', JournalEntry::STATUS_POSTED)
            ->where('journal_lines.account_id', $account->id)
            ->where('journal_lines.counterparty_type', $counterpartyType)
            ->where('journal_lines.counterparty_id', $counterpartyId)
            ->when($branchId, fn ($query) => $query->where('journal_entries.branch_id', $branchId))
            ->selectRaw('COALESCE(SUM(journal_lines.debit), 0) as debits, COALESCE(SUM(journal_lines.credit), 0) as credits')
            ->first();

        $debits = Money::toMinor((string) $totals->debits);
        $credits = Money::toMinor((string) $totals->credits);

        return $account->normal_balance === 'debit'
            ? $debits - $credits
            : $credits - $debits;
    }

    public function postBillIssued(Bill $bill, TenantUser $actor): JournalEntry
    {
        return $this->post(
            $bill->branch_id,
            $actor,
            'bill',
            $bill->id,
            'issued',
            'Restaurant bill '.$bill->bill_number,
            $bill->issued_at->format('Y-m-d'),
            [
                [
                    'account_id' => $this->systemAccount('accounts_receivable')->id,
                    'debit' => $bill->subtotal,
                    'counterparty_type' => 'bill',
                    'counterparty_id' => $bill->id,
                ],
                [
                    'account_id' => $this->systemAccount('sales_revenue')->id,
                    'credit' => $bill->subtotal,
                    'counterparty_type' => 'bill',
                    'counterparty_id' => $bill->id,
                ],
            ],
            'bill:'.$bill->id.':issued',
        );
    }

    public function postDiscountDelta(
        Bill $bill,
        TenantUser $actor,
        string $oldDiscount,
        string $newDiscount,
        string $eventId,
    ): ?JournalEntry {
        $deltaMinor = Money::toMinor($newDiscount) - Money::toMinor($oldDiscount);

        if ($deltaMinor === 0) {
            return null;
        }

        $amount = Money::fromMinor(abs($deltaMinor));
        $ar = $this->systemAccount('accounts_receivable');
        $discount = $this->systemAccount('sales_discounts');

        return $this->post(
            $bill->branch_id,
            $actor,
            'bill_event',
            $eventId,
            'discount',
            'Discount adjustment for '.$bill->bill_number,
            now()->format('Y-m-d'),
            $deltaMinor > 0
                ? [
                    [
                        'account_id' => $discount->id,
                        'debit' => $amount,
                        'counterparty_type' => 'bill',
                        'counterparty_id' => $bill->id,
                    ],
                    [
                        'account_id' => $ar->id,
                        'credit' => $amount,
                        'counterparty_type' => 'bill',
                        'counterparty_id' => $bill->id,
                    ],
                ]
                : [
                    [
                        'account_id' => $ar->id,
                        'debit' => $amount,
                        'counterparty_type' => 'bill',
                        'counterparty_id' => $bill->id,
                    ],
                    [
                        'account_id' => $discount->id,
                        'credit' => $amount,
                        'counterparty_type' => 'bill',
                        'counterparty_id' => $bill->id,
                    ],
                ],
            'bill-discount-event:'.$eventId,
        );
    }

    public function postPayment(TenantPayment $payment, TenantUser $actor): JournalEntry
    {
        $paymentAccount = match ($payment->method) {
            'cash' => $this->systemAccount('cash'),
            'card' => $this->systemAccount('card'),
            'bank' => $this->systemAccount('bank'),
            'mobile_money' => $this->systemAccount('mobile_money'),
            default => $this->systemAccount('other_payment'),
        };

        $bill = $payment->bill;

        return $this->post(
            $bill->branch_id,
            $actor,
            'tenant_payment',
            $payment->id,
            'posted',
            'Payment for '.$bill->bill_number,
            $payment->received_at->format('Y-m-d'),
            [
                [
                    'account_id' => $paymentAccount->id,
                    'debit' => $payment->amount,
                    'counterparty_type' => 'bill',
                    'counterparty_id' => $bill->id,
                ],
                [
                    'account_id' => $this->systemAccount('accounts_receivable')->id,
                    'credit' => $payment->amount,
                    'counterparty_type' => 'bill',
                    'counterparty_id' => $bill->id,
                ],
            ],
            'payment:'.$payment->id.':posted',
        );
    }

    public function postGoodsReceipt(
        GoodsReceipt $receipt,
        TenantUser $actor,
        string $total,
    ): JournalEntry {
        return $this->post(
            $receipt->branch_id,
            $actor,
            'goods_receipt',
            $receipt->id,
            'posted',
            'Goods receipt '.$receipt->receipt_number,
            $receipt->received_at->format('Y-m-d'),
            [
                [
                    'account_id' => $this->systemAccount('inventory_asset')->id,
                    'debit' => $total,
                    'counterparty_type' => 'supplier',
                    'counterparty_id' => $receipt->supplier_id,
                ],
                [
                    'account_id' => $this->systemAccount('accounts_payable')->id,
                    'credit' => $total,
                    'counterparty_type' => 'supplier',
                    'counterparty_id' => $receipt->supplier_id,
                ],
            ],
            'goods-receipt:'.$receipt->id.':posted',
        );
    }

    public function postInventoryConsumption(
        InventoryConsumption $consumption,
        TenantUser $actor,
        string $cost,
    ): ?JournalEntry {
        if (Money::toMinor($cost) <= 0) {
            return null;
        }

        return $this->post(
            $consumption->branch_id,
            $actor,
            'inventory_consumption',
            $consumption->id,
            'posted',
            'Recipe consumption for order '.$consumption->order_id,
            $consumption->consumed_at->format('Y-m-d'),
            [
                [
                    'account_id' => $this->systemAccount('cost_of_goods_sold')->id,
                    'debit' => $cost,
                    'counterparty_type' => 'order',
                    'counterparty_id' => $consumption->order_id,
                ],
                [
                    'account_id' => $this->systemAccount('inventory_asset')->id,
                    'credit' => $cost,
                    'counterparty_type' => 'order',
                    'counterparty_id' => $consumption->order_id,
                ],
            ],
            'inventory-consumption:'.$consumption->id.':posted',
        );
    }

    public function postInventoryAdjustment(
        StockMovement $movement,
        TenantUser $actor,
        string $valueDelta,
    ): ?JournalEntry {
        $minor = Money::toMinor($valueDelta);

        if ($minor === 0) {
            return null;
        }

        $amount = Money::fromMinor(abs($minor));
        $inventory = $this->systemAccount('inventory_asset');
        $adjustment = $this->systemAccount('inventory_adjustment');

        return $this->post(
            $movement->branch_id,
            $actor,
            'stock_movement',
            $movement->id,
            'adjustment',
            'Inventory adjustment for '.$movement->item->name,
            $movement->occurred_at->format('Y-m-d'),
            $minor > 0
                ? [
                    ['account_id' => $inventory->id, 'debit' => $amount],
                    ['account_id' => $adjustment->id, 'credit' => $amount],
                ]
                : [
                    ['account_id' => $adjustment->id, 'debit' => $amount],
                    ['account_id' => $inventory->id, 'credit' => $amount],
                ],
            'stock-movement:'.$movement->id.':valuation-adjustment',
        );
    }

    public function postCashVariance(
        string $branchId,
        TenantUser $actor,
        string $sessionId,
        string $variance,
        string $date,
    ): ?JournalEntry {
        $minor = Money::toMinor($variance);

        if ($minor === 0) {
            return null;
        }

        $amount = Money::fromMinor(abs($minor));
        $cash = $this->systemAccount('cash');
        $overShort = $this->systemAccount('cash_over_short');

        return $this->post(
            $branchId,
            $actor,
            'cashier_session',
            $sessionId,
            'variance',
            'Cashier session cash variance',
            $date,
            $minor < 0
                ? [
                    ['account_id' => $overShort->id, 'debit' => $amount],
                    ['account_id' => $cash->id, 'credit' => $amount],
                ]
                : [
                    ['account_id' => $cash->id, 'debit' => $amount],
                    ['account_id' => $overShort->id, 'credit' => $amount],
                ],
            'cashier-session:'.$sessionId.':variance',
        );
    }
}
