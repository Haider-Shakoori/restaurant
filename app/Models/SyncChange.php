<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Model;

#[Fillable([
    'entity_type',
    'entity_id',
    'operation',
    'payload',
    'occurred_at',
])]
class SyncChange extends Model
{
    public $timestamps = false;

    public $incrementing = true;

    protected $primaryKey = 'sequence';

    protected $keyType = 'int';

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'sequence' => 'integer',
            'payload' => 'array',
            'occurred_at' => 'datetime',
        ];
    }
}
