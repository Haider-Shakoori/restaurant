<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\HasMany;
use Stancl\Tenancy\Database\Concerns\CentralConnection;

#[Fillable(['code', 'name', 'description', 'is_active', 'sort_order'])]
class Plan extends Model
{
    use CentralConnection, HasFactory;

    protected function casts(): array
    {
        return [
            'is_active' => 'boolean',
        ];
    }

    public function features(): HasMany
    {
        return $this->hasMany(PlanFeature::class);
    }

    public function businesses(): HasMany
    {
        return $this->hasMany(Business::class);
    }

    public function prices(): HasMany
    {
        return $this->hasMany(PlanPrice::class)->orderBy('sort_order');
    }

    public function subscriptions(): HasMany
    {
        return $this->hasMany(Subscription::class);
    }

    public function entitlementSnapshot(): array
    {
        $this->loadMissing('features');

        return $this->features
            ->mapWithKeys(fn (PlanFeature $feature): array => [
                $feature->feature_key => $feature->resolvedValue(),
            ])
            ->all();
    }
}
