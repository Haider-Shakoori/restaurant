<?php

namespace App\Models;

use App\Enums\DeviceStatus;
use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;
use Stancl\Tenancy\Database\Concerns\CentralConnection;

#[Fillable([
    'business_id',
    'license_key_id',
    'device_uid',
    'device_name',
    'platform',
    'app_version',
    'credential_hash',
    'credential_last4',
    'status',
    'activated_at',
    'last_seen_at',
    'last_verified_at',
    'revoked_at',
    'metadata',
])]
class DeviceActivation extends Model
{
    use CentralConnection, HasUlids;

    protected function casts(): array
    {
        return [
            'status' => DeviceStatus::class,
            'activated_at' => 'datetime',
            'last_seen_at' => 'datetime',
            'last_verified_at' => 'datetime',
            'revoked_at' => 'datetime',
            'metadata' => 'array',
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

    public function leases(): HasMany
    {
        return $this->hasMany(OfflineLease::class);
    }
}
