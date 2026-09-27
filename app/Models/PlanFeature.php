<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Stancl\Tenancy\Database\Concerns\CentralConnection;

#[Fillable(['plan_id', 'feature_key', 'value'])]
class PlanFeature extends Model
{
    use CentralConnection;

    protected function casts(): array
    {
        return [
            'value' => 'array',
        ];
    }

    public function plan(): BelongsTo
    {
        return $this->belongsTo(Plan::class);
    }

    public function resolvedValue(): mixed
    {
        $value = data_get($this->value, 'value');

        if (! is_string($value)) {
            return $value;
        }

        $normalized = strtolower(trim($value));

        return match (true) {
            $normalized === 'true' => true,
            $normalized === 'false' => false,
            $normalized === 'null' => null,
            is_numeric($normalized) && str_contains($normalized, '.') => (float) $normalized,
            is_numeric($normalized) => (int) $normalized,
            default => $value,
        };
    }
}
