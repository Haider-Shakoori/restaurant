<?php

namespace App\Models;

use App\Enums\BillingCycle;
use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;
use Stancl\Tenancy\Database\Concerns\CentralConnection;

#[Fillable([
    'plan_id',
    'billing_cycle',
    'interval_months',
    'price',
    'currency',
    'is_active',
    'sort_order',
])]
class PlanPrice extends Model
{
    use CentralConnection;

    protected function casts(): array
    {
        return [
            'billing_cycle' => BillingCycle::class,
            'price' => 'decimal:2',
            'is_active' => 'boolean',
        ];
    }

    public function plan(): BelongsTo
    {
        return $this->belongsTo(Plan::class);
    }

    public function subscriptions(): HasMany
    {
        return $this->hasMany(Subscription::class);
    }
}
