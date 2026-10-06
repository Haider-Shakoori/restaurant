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
    'business_date',
    'status',
    'created_by_user_id',
    'finalized_at',
    'reopened_at',
])]
class DailyClosing extends Model
{
    use HasUlids, RecordsSyncChanges;

    public const STATUS_OPEN = 'open';

    public const STATUS_FINALIZED = 'finalized';

    public const STATUS_REOPENED = 'reopened';

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'business_date' => 'date:Y-m-d',
            'finalized_at' => 'datetime',
            'reopened_at' => 'datetime',
        ];
    }

    public function branch(): BelongsTo
    {
        return $this->belongsTo(RestaurantBranch::class, 'branch_id');
    }

    public function snapshots(): HasMany
    {
        return $this->hasMany(DailyClosingSnapshot::class)->orderBy('version');
    }

    public function events(): HasMany
    {
        return $this->hasMany(DailyClosingEvent::class)->orderBy('occurred_at');
    }
}
