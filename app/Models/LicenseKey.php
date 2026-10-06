<?php

namespace App\Models;

use App\Enums\LicenseStatus;
use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;
use Stancl\Tenancy\Database\Concerns\CentralConnection;

#[Fillable([
    'business_id',
    'generated_by_admin_id',
    'version',
    'key_hash',
    'key_prefix',
    'key_last4',
    'status',
    'max_devices_snapshot',
    'max_mobile_devices_snapshot',
    'generated_at',
    'last_used_at',
    'revoked_at',
    'metadata',
])]
class LicenseKey extends Model
{
    use CentralConnection, HasUlids;

    protected function casts(): array
    {
        return [
            'status' => LicenseStatus::class,
            'generated_at' => 'datetime',
            'last_used_at' => 'datetime',
            'revoked_at' => 'datetime',
            'metadata' => 'array',
        ];
    }

    public function business(): BelongsTo
    {
        return $this->belongsTo(Business::class);
    }

    public function generatedBy(): BelongsTo
    {
        return $this->belongsTo(AdminUser::class, 'generated_by_admin_id');
    }

    public function devices(): HasMany
    {
        return $this->hasMany(DeviceActivation::class);
    }

    public function leases(): HasMany
    {
        return $this->hasMany(OfflineLease::class);
    }

    public function masked(): string
    {
        return $this->key_prefix.'-****-****-****-'.$this->key_last4;
    }
}
