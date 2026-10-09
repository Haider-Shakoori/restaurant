<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Model;

#[Fillable(['central_device_id', 'tenant_user_id', 'fcm_token', 'platform', 'enabled'])]
class WaiterPushDevice extends Model
{
    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'fcm_token' => 'encrypted',
            'enabled' => 'boolean',
        ];
    }
}
