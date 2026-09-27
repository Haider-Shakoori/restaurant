<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Stancl\Tenancy\Database\Concerns\CentralConnection;

#[Fillable([
    'business_id',
    'license_key_id',
    'device_activation_id',
    'admin_user_id',
    'event',
    'message',
    'context',
    'occurred_at',
])]
class LicenseEvent extends Model
{
    use CentralConnection, HasUlids;

    public const UPDATED_AT = null;

    protected function casts(): array
    {
        return [
            'context' => 'array',
            'occurred_at' => 'datetime',
        ];
    }

    public function business(): BelongsTo
    {
        return $this->belongsTo(Business::class);
    }

    public function licenseKey(): BelongsTo
    {
        return $this->belongsTo(LicenseKey::class);
    }

    public function device(): BelongsTo
    {
        return $this->belongsTo(DeviceActivation::class, 'device_activation_id');
    }

    public function adminUser(): BelongsTo
    {
        return $this->belongsTo(AdminUser::class);
    }
}
