<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;

#[Fillable([
    'central_device_id',
    'record_type',
    'local_entity_id',
    'actor_user_id',
    'payload',
    'occurred_at',
])]
class DesktopOperationalRecord extends Model
{
    use HasUlids;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'payload' => 'array',
            'occurred_at' => 'datetime',
        ];
    }
}
