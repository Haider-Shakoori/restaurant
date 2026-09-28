<?php

namespace App\Services\Tenant;

use App\Models\Branch;
use App\Models\Business;
use App\Models\Restaurant;
use App\Services\Platform\SubscriptionService;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;

class BranchService
{
    public function __construct(private readonly SubscriptionService $subscriptions) {}

    public function create(Restaurant $restaurant, array $data): Branch
    {
        return DB::connection('tenant')->transaction(function () use ($restaurant, $data): Branch {
            $isActive = (bool) ($data['is_active'] ?? true);

            if ($isActive) {
                $this->assertBranchLimitAllowsAnother($restaurant);
            }

            $isPrimary = (bool) ($data['is_primary'] ?? false);

            if (! $restaurant->branches()->exists()) {
                $isPrimary = true;
            }

            if ($isPrimary) {
                $restaurant->branches()->update(['is_primary' => false]);
            }

            return $restaurant->branches()->create([
                ...$data,
                'code' => Str::upper(trim((string) $data['code'])),
                'is_primary' => $isPrimary,
                'is_active' => $isActive,
            ]);
        });
    }

    public function update(Branch $branch, array $data): Branch
    {
        return DB::connection('tenant')->transaction(function () use ($branch, $data): Branch {
            $restaurant = $branch->restaurant()->firstOrFail();
            $willBeActive = array_key_exists('is_active', $data)
                ? (bool) $data['is_active']
                : $branch->is_active;
            $willBePrimary = array_key_exists('is_primary', $data)
                ? (bool) $data['is_primary']
                : $branch->is_primary;

            if (! $branch->is_active && $willBeActive) {
                $this->assertBranchLimitAllowsAnother($restaurant);
            }

            if ($branch->is_primary && (! $willBePrimary || ! $willBeActive)) {
                $otherPrimary = $restaurant->branches()
                    ->where($branch->getKeyName(), '!=', $branch->getKey())
                    ->where('is_active', true)
                    ->where('is_primary', true)
                    ->exists();

                if (! $otherPrimary) {
                    throw ValidationException::withMessages([
                        'is_primary' => 'Assign another active primary branch before changing this one.',
                    ]);
                }
            }

            if ($willBePrimary) {
                $restaurant->branches()
                    ->where($branch->getKeyName(), '!=', $branch->getKey())
                    ->update(['is_primary' => false]);
            }

            $branch->update([
                ...$data,
                'code' => array_key_exists('code', $data)
                    ? Str::upper(trim((string) $data['code']))
                    : $branch->code,
                'is_primary' => $willBePrimary,
                'is_active' => $willBeActive,
            ]);

            return $branch->refresh();
        });
    }

    public function maxBranches(): ?int
    {
        $business = Business::query()->where('tenant_id', tenant('id'))->first();

        if (! $business) {
            return null;
        }

        $value = $this->subscriptions->featureValue($business, 'max_branches');

        if (! is_numeric($value)) {
            return null;
        }

        $limit = (int) $value;

        return $limit > 0 ? $limit : null;
    }

    private function assertBranchLimitAllowsAnother(Restaurant $restaurant): void
    {
        $limit = $this->maxBranches();

        if ($limit === null) {
            return;
        }

        if ($restaurant->branches()->where('is_active', true)->count() >= $limit) {
            throw ValidationException::withMessages([
                'branch' => "The current plan allows a maximum of {$limit} active branch(es).",
            ]);
        }
    }
}
