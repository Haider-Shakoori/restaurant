<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;

#[Fillable([
    'order_id',
    'sequence',
    'submitted_by_user_id',
    'client_mutation_id',
    'kot_number',
    'priority',
    'workflow_snapshot',
    'service_context',
    'course_context',
    'sent_at',
])]
class KotDispatchRound extends Model
{
    use HasUlids;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'sequence' => 'integer',
            'workflow_snapshot' => 'array',
            'service_context' => 'array',
            'course_context' => 'array',
            'sent_at' => 'datetime',
        ];
    }

    public function order(): BelongsTo
    {
        return $this->belongsTo(Order::class);
    }

    public function submittedBy(): BelongsTo
    {
        return $this->belongsTo(TenantUser::class, 'submitted_by_user_id');
    }

    public function tickets(): HasMany
    {
        return $this->hasMany(KitchenTicket::class)->orderBy('queued_at');
    }
}
