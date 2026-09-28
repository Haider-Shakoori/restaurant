<?php

namespace App\Models;

use App\Enums\BusinessStatus;
use App\Enums\ProvisioningState;
use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;
use Stancl\Tenancy\Database\Concerns\CentralConnection;

#[Fillable([
    'tenant_id',
    'plan_id',
    'assigned_operator_id',
    'name',
    'requested_subdomain',
    'contact_name',
    'phone',
    'whatsapp',
    'email',
    'location',
    'status',
    'provisioning_state',
    'provisioning_error',
    'last_health_at',
])]
class Business extends Model
{
    use CentralConnection, HasUlids;

    protected function casts(): array
    {
        return [
            'status' => BusinessStatus::class,
            'provisioning_state' => ProvisioningState::class,
            'last_health_at' => 'datetime',
        ];
    }

    public function tenant(): BelongsTo
    {
        return $this->belongsTo(Tenant::class);
    }

    public function plan(): BelongsTo
    {
        return $this->belongsTo(Plan::class);
    }

    public function assignedOperator(): BelongsTo
    {
        return $this->belongsTo(AdminUser::class, 'assigned_operator_id');
    }

    public function provisioningEvents(): HasMany
    {
        return $this->hasMany(ProvisioningEvent::class)->latest('occurred_at');
    }
}
