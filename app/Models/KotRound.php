<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;

#[Fillable([
    'order_id',
    'branch_id',
    'submitted_by_user_id',
    'round_number',
    'display_number',
    'business_date',
    'client_dispatch_id',
    'dispatched_at',
])]
class KotRound extends Model
{
    use HasUlids;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'round_number' => 'integer',
            'display_number' => 'integer',
            'business_date' => 'date',
            'dispatched_at' => 'datetime',
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

    public function submittedBy(): BelongsTo
    {
        return $this->belongsTo(TenantUser::class, 'submitted_by_user_id');
    }

    public function tickets(): HasMany
    {
        return $this->hasMany(KitchenTicket::class)->orderBy('queued_at');
    }

    public function getKotNumberAttribute(): string
    {
        return 'KOT-'.str_pad((string) $this->display_number, 4, '0', STR_PAD_LEFT);
    }
}
