<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

#[Fillable([
    'branch_id',
    'expense_account_id',
    'payment_account_id',
    'created_by_user_id',
    'expense_number',
    'client_expense_id',
    'description',
    'amount',
    'expense_date',
    'reference',
    'status',
])]
class OperatingExpense extends Model
{
    use HasUlids;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'amount' => 'decimal:2',
            'expense_date' => 'date:Y-m-d',
        ];
    }

    public function branch(): BelongsTo
    {
        return $this->belongsTo(RestaurantBranch::class, 'branch_id');
    }

    public function expenseAccount(): BelongsTo
    {
        return $this->belongsTo(ChartAccount::class, 'expense_account_id');
    }

    public function paymentAccount(): BelongsTo
    {
        return $this->belongsTo(ChartAccount::class, 'payment_account_id');
    }
}
