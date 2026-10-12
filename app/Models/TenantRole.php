<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Model;

#[Fillable(['public_id', 'key', 'name', 'description', 'is_system', 'permissions'])]
class TenantRole extends Model
{
    protected $connection = 'tenant';

    protected $table = 'roles';

    protected function casts(): array
    {
        return [
            'is_system' => 'boolean',
            'permissions' => 'array',
        ];
    }
}
