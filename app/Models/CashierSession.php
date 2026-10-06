<?php

namespace App\Models;

use App\Models\Concerns\RecordsSyncChanges;
use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;

#[Fillable([
    'branch_id',
    'cashier_user_id',
    'status',
    'opening_cash',
    'expected_cash',
    'declared_cash',
    'cash_variance',
    'opened_at',
    'closed_at',
])]
class CashierSession extends Model
{
    use HasUlids, RecordsSyncChanges;

    public const STATUS_OPEN = 'open';

    public const STATUS_CLOSED = 'closed';

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'opening_cash' => 'decimal:2',
            'expected_cash' => 'decimal:2',
            'declared_cash' => 'decimal:2',
            'cash_variance' => 'decimal:2',
            'opened_at' => 'datetime',
            'closed_at' => 'datetime',
        ];
    }

    public function branch(): BelongsTo
    {
        return $this->belongsTo(RestaurantBranch::class, 'branch_id');
    }

    public function cashier(): BelongsTo
    {
        return $this->belongsTo(TenantUser::class, 'cashier_user_id');
    }

    public function payments(): HasMany
    {
        return $this->hasMany(TenantPayment::class);
    }
}
