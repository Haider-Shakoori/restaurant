<?php

namespace App\Models;

use App\Models\Concerns\RecordsSyncChanges;
use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;

#[Fillable([
    'order_id',
    'branch_id',
    'created_by_user_id',
    'bill_number',
    'status',
    'subtotal',
    'discount_type',
    'discount_value',
    'discount_amount',
    'discount_reason',
    'total',
    'paid_amount',
    'balance_due',
    'issued_at',
    'paid_at',
    'voided_at',
])]
class Bill extends Model
{
    use HasUlids, RecordsSyncChanges;

    public const STATUS_OPEN = 'open';

    public const STATUS_PAID = 'paid';

    public const STATUS_VOID = 'void';

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'subtotal' => 'decimal:2',
            'discount_value' => 'decimal:4',
            'discount_amount' => 'decimal:2',
            'total' => 'decimal:2',
            'paid_amount' => 'decimal:2',
            'balance_due' => 'decimal:2',
            'issued_at' => 'datetime',
            'paid_at' => 'datetime',
            'voided_at' => 'datetime',
        ];
    }

    public function order(): BelongsTo
    {
        return $this->belongsTo(Order::class);
    }

    public function branch(): BelongsTo
    {
        return $this->belongsTo(RestaurantBranch::class, 'branch_id');
    }

    public function lines(): HasMany
    {
        return $this->hasMany(BillLine::class);
    }

    public function payments(): HasMany
    {
        return $this->hasMany(TenantPayment::class);
    }

    public function events(): HasMany
    {
        return $this->hasMany(BillEvent::class)->orderBy('occurred_at');
    }
}
