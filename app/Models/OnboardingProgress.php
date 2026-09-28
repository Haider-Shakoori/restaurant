<?php

namespace App\Models;

use App\Enums\OnboardingStep;
use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Model;

#[Fillable([
    'singleton_key',
    'current_step',
    'completed_steps',
    'started_at',
    'completed_at',
])]
class OnboardingProgress extends Model
{
    protected $connection = 'tenant';

    protected $table = 'onboarding_progress';

    protected function casts(): array
    {
        return [
            'current_step' => OnboardingStep::class,
            'completed_steps' => 'array',
            'started_at' => 'datetime',
            'completed_at' => 'datetime',
        ];
    }

    public function hasCompleted(OnboardingStep $step): bool
    {
        return in_array($step->value, $this->completed_steps ?? [], true);
    }
}
