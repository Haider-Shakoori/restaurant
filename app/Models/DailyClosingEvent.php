<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

#[Fillable(['daily_closing_id', 'actor_user_id', 'event_type', 'payload', 'occurred_at'])]
class DailyClosingEvent extends Model
{
    use HasUlids;

    public $timestamps = false;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'payload' => 'array',
            'occurred_at' => 'datetime',
        ];
    }

    public function closing(): BelongsTo
    {
        return $this->belongsTo(DailyClosing::class, 'daily_closing_id');
    }
}
