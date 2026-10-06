<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;

#[Fillable([
    'central_device_id',
    'entity_type',
    'local_entity_id',
    'cloud_entity_id',
])]
class DesktopEntityLink extends Model
{
    use HasUlids;

    protected $connection = 'tenant';
}
