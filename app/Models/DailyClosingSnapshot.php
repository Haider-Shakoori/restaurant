<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

#[Fillable([
    'daily_closing_id',
    'version',
    'finalized_by_user_id',
    'bill_count',
    'payment_count',
    'cashier_session_count',
    'gross_sales',
    'discounts',
    'net_sales',
    'payments_total',
    'cash_payments',
    'card_payments',
    'bank_payments',
    'mobile_money_payments',
    'other_payments',
    'expected_cash',
    'declared_cash',
    'cash_variance',
    'finalized_at',
])]
class DailyClosingSnapshot extends Model
{
    use HasUlids;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'gross_sales' => 'decimal:2',
            'discounts' => 'decimal:2',
            'net_sales' => 'decimal:2',
            'payments_total' => 'decimal:2',
            'cash_payments' => 'decimal:2',
            'card_payments' => 'decimal:2',
            'bank_payments' => 'decimal:2',
            'mobile_money_payments' => 'decimal:2',
            'other_payments' => 'decimal:2',
            'expected_cash' => 'decimal:2',
            'declared_cash' => 'decimal:2',
            'cash_variance' => 'decimal:2',
            'finalized_at' => 'datetime',
        ];
    }

    public function closing(): BelongsTo
    {
        return $this->belongsTo(DailyClosing::class, 'daily_closing_id');
    }
}
