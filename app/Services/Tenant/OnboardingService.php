<?php

namespace App\Services\Tenant;

use App\Enums\OnboardingStep;
use App\Models\Branch;
use App\Models\OnboardingProgress;
use App\Models\Restaurant;
use Illuminate\Support\Facades\DB;

class OnboardingService
{
    public function __construct(private readonly BranchService $branches) {}

    public function progress(): OnboardingProgress
    {
        return OnboardingProgress::firstOrCreate(
            ['singleton_key' => 'primary'],
            [
                'current_step' => OnboardingStep::Restaurant,
                'completed_steps' => [],
                'started_at' => now(),
            ],
        );
    }

    public function restaurant(): ?Restaurant
    {
        return Restaurant::where('profile_key', 'primary')->first();
    }

    public function saveRestaurant(array $data): Restaurant
    {
        return DB::connection('tenant')->transaction(function () use ($data): Restaurant {
            $restaurant = Restaurant::updateOrCreate(
                ['profile_key' => 'primary'],
                [
                    ...$data,
                    'country_code' => strtoupper((string) ($data['country_code'] ?? 'AF')),
                    'currency' => strtoupper((string) ($data['currency'] ?? 'AFN')),
                    'is_active' => true,
                ],
            );

            $this->completeStep(OnboardingStep::Restaurant);

            return $restaurant;
        });
    }

    public function savePrimaryBranch(array $data): Branch
    {
        $restaurant = $this->restaurant();

        if (! $restaurant) {
            abort(409, 'Restaurant information must be completed first.');
        }

        $existing = $restaurant->branches()->where('is_primary', true)->first();

        if ($existing) {
            $branch = $this->branches->update($existing, [
                ...$data,
                'is_primary' => true,
                'is_active' => true,
            ]);
        } else {
            $branch = $this->branches->create($restaurant, [
                ...$data,
                'is_primary' => true,
                'is_active' => true,
            ]);
        }

        $this->completeStep(OnboardingStep::Branch);

        return $branch;
    }

    private function completeStep(OnboardingStep $step): void
    {
        $progress = $this->progress();
        $completed = collect($progress->completed_steps ?? [])
            ->push($step->value)
            ->unique()
            ->values()
            ->all();

        $progress->update([
            'completed_steps' => $completed,
            'current_step' => $step->next() ?? $step,
        ]);
    }
}
