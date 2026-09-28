<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

#[Fillable([
    'central_device_id',
    'tenant_user_id',
    'device_uid',
    'last_pull_cursor',
    'last_push_at',
    'last_pull_at',
    'last_seen_at',
])]
class SyncDeviceState extends Model
{
    use HasUlids;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'last_pull_cursor' => 'integer',
            'last_push_at' => 'datetime',
            'last_pull_at' => 'datetime',
            'last_seen_at' => 'datetime',
        ];
    }

    public function user(): BelongsTo
    {
        return $this->belongsTo(TenantUser::class, 'tenant_user_id');
    }
}
