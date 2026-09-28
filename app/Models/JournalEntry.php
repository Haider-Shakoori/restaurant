<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;

#[Fillable([
    'branch_id',
    'posted_by_user_id',
    'entry_number',
    'source_type',
    'source_id',
    'source_action',
    'idempotency_key',
    'description',
    'entry_date',
    'status',
    'reversal_of_id',
    'posted_at',
])]
class JournalEntry extends Model
{
    use HasUlids;

    public const STATUS_POSTED = 'posted';
    public const STATUS_REVERSED = 'reversed';

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'entry_date' => 'date:Y-m-d',
            'posted_at' => 'datetime',
        ];
    }

    public function branch(): BelongsTo
    {
        return $this->belongsTo(RestaurantBranch::class, 'branch_id');
    }

    public function postedBy(): BelongsTo
    {
        return $this->belongsTo(TenantUser::class, 'posted_by_user_id');
    }

    public function lines(): HasMany
    {
        return $this->hasMany(JournalLine::class);
    }

    public function reversalOf(): BelongsTo
    {
        return $this->belongsTo(self::class, 'reversal_of_id');
    }
}
