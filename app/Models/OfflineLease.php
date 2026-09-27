<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Stancl\Tenancy\Database\Concerns\CentralConnection;

#[Fillable([
    'id',
    'business_id',
    'license_key_id',
    'device_activation_id',
    'subscription_id',
    'key_id',
    'schema_version',
    'payload_hash',
    'issued_at',
    'expires_at',
])]
class OfflineLease extends Model
{
    use CentralConnection, HasUlids;

    protected function casts(): array
    {
        return [
            'issued_at' => 'datetime',
            'expires_at' => 'datetime',
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

    public function subscription(): BelongsTo
    {
        return $this->belongsTo(Subscription::class);
    }
}
