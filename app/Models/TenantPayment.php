<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

#[Fillable([
    'bill_id',
    'cashier_session_id',
    'received_by_user_id',
    'client_payment_id',
    'method',
    'amount',
    'reference',
    'status',
    'received_at',
    'reversed_at',
    'reversal_reason',
])]
class TenantPayment extends Model
{
    use HasUlids;

    public const STATUS_POSTED = 'posted';

    public const STATUS_REVERSED = 'reversed';

    public const METHODS = ['cash', 'card', 'bank', 'mobile_money', 'other'];

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'amount' => 'decimal:2',
            'received_at' => 'datetime',
            'reversed_at' => 'datetime',
        ];
    }

    public function bill(): BelongsTo
    {
        return $this->belongsTo(Bill::class);
    }

    public function session(): BelongsTo
    {
        return $this->belongsTo(CashierSession::class, 'cashier_session_id');
    }

    public function receivedBy(): BelongsTo
    {
        return $this->belongsTo(TenantUser::class, 'received_by_user_id');
    }
}
