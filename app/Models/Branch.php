<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

#[Fillable([
    'restaurant_id',
    'code',
    'name',
    'phone',
    'address',
    'is_primary',
    'is_active',
])]
class Branch extends Model
{
    use HasUlids;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'is_primary' => 'boolean',
            'is_active' => 'boolean',
        ];
    }

    public function restaurant(): BelongsTo
    {
        return $this->belongsTo(Restaurant::class);
    }
}
