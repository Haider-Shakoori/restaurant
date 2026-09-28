<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\HasMany;

#[Fillable([
    'profile_key',
    'name',
    'phone',
    'email',
    'address',
    'country_code',
    'currency',
    'timezone',
    'locale',
    'is_active',
])]
class Restaurant extends Model
{
    use HasUlids;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'is_active' => 'boolean',
        ];
    }

    public function branches(): HasMany
    {
        return $this->hasMany(Branch::class);
    }
}
