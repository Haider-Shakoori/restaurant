<?php

namespace App\Services\Tenant;

use App\Models\Bill;
use App\Models\ChartAccount;
use App\Models\DailyClosing;
use App\Models\JournalLine;
use App\Models\Supplier;
use App\Support\Money;
use Illuminate\Database\Eloquent\Builder;
use Illuminate\Support\Collection;

class ReportingService
{
    public function __construct(
        private readonly AccountingService $accounting,
    ) {}

    public function trialBalance(?string $branchId, string $from, string $to): array
    {
        $this->accounting->ensureSystemAccounts();

        $accounts = ChartAccount::query()->where('is_active', true)->orderBy('code')->get();

        $lines = $this->lines($branchId, $from, $to)
            ->selectRaw('journal_lines.account_id, SUM(journal_lines.debit) as debit_total, SUM(journal_lines.credit) as credit_total')
            ->groupBy('journal_lines.account_id')
            ->get()
            ->keyBy('account_id');

        return $accounts->map(function (ChartAccount $account) use ($lines): array {
            $totals = $lines->get($account->id);
            $debit = (string) ($totals?->debit_total ?? '0.00');
            $credit = (string) ($totals?->credit_total ?? '0.00');
            $balance = $account->normal_balance === 'debit'
                ? Money::subtract($debit, $credit)
                : Money::subtract($credit, $debit);

            return [
                'account_id' => $account->id,
                'code' => $account->code,
                'name' => $account->name,
                'type' => $account->type,
                'debit' => Money::fromMinor(Money::toMinor($debit)),
                'credit' => Money::fromMinor(Money::toMinor($credit)),
                'balance' => $balance,
            ];
        })->all();
    }

    public function incomeStatement(?string $branchId, string $from, string $to): array
    {
        $trial = collect($this->trialBalance($branchId, $from, $to));
        $revenueAccounts = $trial->where('type', ChartAccount::TYPE_REVENUE);
        $expenseAccounts = $trial->where('type', ChartAccount::TYPE_EXPENSE);

        $revenue = 0;
        $expenses = 0;

        foreach ($revenueAccounts as $line) {
            $account = ChartAccount::query()->findOrFail($line['account_id']);
            $amount = Money::toMinor($line['balance']);
            $revenue += $account->is_contra ? -$amount : $amount;
        }

        foreach ($expenseAccounts as $line) {
            $expenses += Money::toMinor($line['balance']);
        }

        return [
            'from' => $from,
            'to' => $to,
            'branch_id' => $branchId,
            'revenue' => Money::fromMinor($revenue),
            'expenses' => Money::fromMinor($expenses),
            'net_profit' => Money::fromMinor($revenue - $expenses),
            'revenue_accounts' => $revenueAccounts->values()->all(),
            'expense_accounts' => $expenseAccounts->values()->all(),
        ];
    }

    public function balanceSheet(?string $branchId, string $asOf): array
    {
        $trial = collect($this->trialBalance($branchId, '1900-01-01', $asOf));
        $assets = $this->sumType($trial, ChartAccount::TYPE_ASSET);
        $liabilities = $this->sumType($trial, ChartAccount::TYPE_LIABILITY);
        $equity = $this->sumType($trial, ChartAccount::TYPE_EQUITY);
        $income = $this->incomeStatement($branchId, '1900-01-01', $asOf);
        $retainedEarnings = Money::toMinor($income['net_profit']);

        return [
            'as_of' => $asOf,
            'branch_id' => $branchId,
            'assets' => Money::fromMinor($assets),
            'liabilities' => Money::fromMinor($liabilities),
            'equity_before_current_profit' => Money::fromMinor($equity),
            'current_profit' => $income['net_profit'],
            'equity_including_current_profit' => Money::fromMinor($equity + $retainedEarnings),
            'liabilities_and_equity' => Money::fromMinor($liabilities + $equity + $retainedEarnings),
            'accounts' => $trial
                ->whereIn('type', [
                    ChartAccount::TYPE_ASSET,
                    ChartAccount::TYPE_LIABILITY,
                    ChartAccount::TYPE_EQUITY,
                ])
                ->values()
                ->all(),
        ];
    }

    public function receivables(?string $branchId): array
    {
        $query = Bill::query()
            ->with(['branch', 'order.table'])
            ->where('balance_due', '>', 0)
            ->where('status', Bill::STATUS_OPEN)
            ->when($branchId, fn ($query) => $query->where('branch_id', $branchId))
            ->orderBy('issued_at')
            ->get();

        return [
            'total' => $this->sumValues($query->pluck('balance_due')),
            'bills' => $query->all(),
        ];
    }

    public function payables(?string $branchId, string $asOf): array
    {
        $ap = $this->accounting->systemAccount('accounts_payable');

        $lines = $this->lines($branchId, '1900-01-01', $asOf)
            ->where('journal_lines.account_id', $ap->id)
            ->where('journal_lines.counterparty_type', 'supplier')
            ->selectRaw('journal_lines.counterparty_id, SUM(journal_lines.credit) as credits, SUM(journal_lines.debit) as debits')
            ->groupBy('journal_lines.counterparty_id')
            ->get();

        $supplierIds = $lines->pluck('counterparty_id')->filter()->all();
        $suppliers = Supplier::query()->whereIn('id', $supplierIds)->get()->keyBy('id');
        $rows = [];
        $totalMinor = 0;

        foreach ($lines as $line) {
            $balance = Money::toMinor((string) $line->credits) - Money::toMinor((string) $line->debits);

            if ($balance <= 0) {
                continue;
            }

            $totalMinor += $balance;
            $rows[] = [
                'supplier_id' => $line->counterparty_id,
                'supplier_name' => $suppliers->get($line->counterparty_id)?->name,
                'balance' => Money::fromMinor($balance),
            ];
        }

        return [
            'as_of' => $asOf,
            'total' => Money::fromMinor($totalMinor),
            'suppliers' => $rows,
        ];
    }

    public function managementSummary(?string $branchId, string $from, string $to): array
    {
        $income = $this->incomeStatement($branchId, $from, $to);
        $receivables = $this->receivables($branchId);
        $payables = $this->payables($branchId, $to);
        $closings = DailyClosing::query()
            ->when($branchId, fn ($query) => $query->where('branch_id', $branchId))
            ->whereBetween('business_date', [$from, $to])
            ->where('status', DailyClosing::STATUS_FINALIZED)
            ->with('snapshots')
            ->get();

        $netSales = 0;
        $cashVariance = 0;

        foreach ($closings as $closing) {
            $snapshot = $closing->snapshots->last();

            if (! $snapshot) {
                continue;
            }

            $netSales += Money::toMinor((string) $snapshot->net_sales);
            $cashVariance += Money::toMinor((string) $snapshot->cash_variance);
        }

        return [
            'from' => $from,
            'to' => $to,
            'branch_id' => $branchId,
            'journal_net_profit' => $income['net_profit'],
            'daily_closing_net_sales' => Money::fromMinor($netSales),
            'cash_variance' => Money::fromMinor($cashVariance),
            'receivables' => $receivables['total'],
            'payables' => $payables['total'],
            'finalized_days' => $closings->count(),
        ];
    }

    private function lines(?string $branchId, string $from, string $to): Builder
    {
        return JournalLine::query()
            ->join('journal_entries', 'journal_entries.id', '=', 'journal_lines.journal_entry_id')
            ->where('journal_entries.status', 'posted')
            ->whereBetween('journal_entries.entry_date', [$from, $to])
            ->when($branchId, fn ($query) => $query->where('journal_entries.branch_id', $branchId));
    }

    private function sumType(Collection $trial, string $type): int
    {
        $sum = 0;

        foreach ($trial->where('type', $type) as $line) {
            $sum += Money::toMinor($line['balance']);
        }

        return $sum;
    }

    private function sumValues(Collection $values): string
    {
        $sum = 0;

        foreach ($values as $value) {
            $sum += Money::toMinor((string) $value);
        }

        return Money::fromMinor($sum);
    }
}
