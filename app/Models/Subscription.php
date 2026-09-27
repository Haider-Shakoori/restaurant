<?php

namespace App\Models;

use App\Enums\BillingCycle;
use App\Enums\SubscriptionSource;
use App\Enums\SubscriptionStatus;
use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;
use Stancl\Tenancy\Database\Concerns\CentralConnection;

#[Fillable([
    'business_id',
    'plan_id',
    'plan_price_id',
    'created_by_admin_id',
    'source',
    'status',
    'billing_cycle',
    'interval_months',
    'custom_days',
    'price_snapshot',
    'currency',
    'plan_code_snapshot',
    'plan_name_snapshot',
    'features_snapshot',
    'starts_at',
    'ends_at',
    'activated_at',
    'cancelled_at',
    'metadata',
])]
class Subscription extends Model
{
    use CentralConnection, HasUlids;

    protected function casts(): array
    {
        return [
            'source' => SubscriptionSource::class,
            'status' => SubscriptionStatus::class,
            'billing_cycle' => BillingCycle::class,
            'price_snapshot' => 'decimal:2',
            'features_snapshot' => 'array',
            'starts_at' => 'datetime',
            'ends_at' => 'datetime',
            'activated_at' => 'datetime',
            'cancelled_at' => 'datetime',
            'metadata' => 'array',
        ];
    }

    public function business(): BelongsTo
    {
        return $this->belongsTo(Business::class);
    }

    public function plan(): BelongsTo
    {
        return $this->belongsTo(Plan::class);
    }

    public function planPrice(): BelongsTo
    {
        return $this->belongsTo(PlanPrice::class);
    }

    public function createdBy(): BelongsTo
    {
        return $this->belongsTo(AdminUser::class, 'created_by_admin_id');
    }

    public function events(): HasMany
    {
        return $this->hasMany(SubscriptionEvent::class)->latest('occurred_at');
    }
}
