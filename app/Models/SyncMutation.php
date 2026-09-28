<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

#[Fillable([
    'central_device_id',
    'tenant_user_id',
    'mutation_id',
    'operation',
    'entity_type',
    'entity_id',
    'status',
    'request_hash',
    'response',
    'error_code',
    'error_message',
    'client_occurred_at',
    'processed_at',
])]
class SyncMutation extends Model
{
    use HasUlids;

    public const STATUS_ACCEPTED = 'accepted';

    public const STATUS_CONFLICT = 'conflict';

    public const STATUS_REJECTED = 'rejected';

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'response' => 'array',
            'client_occurred_at' => 'datetime',
            'processed_at' => 'datetime',
        ];
    }

    public function user(): BelongsTo
    {
        return $this->belongsTo(TenantUser::class, 'tenant_user_id');
    }
}
