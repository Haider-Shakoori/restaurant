<?php

namespace App\Models;

use App\Models\Concerns\RecordsSyncChanges;
use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;

#[Fillable(['branch_id', 'name', 'sort_order', 'is_active'])]
class DiningArea extends Model
{
    use HasUlids, RecordsSyncChanges;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return ['is_active' => 'boolean'];
    }

    public function branch(): BelongsTo
    {
        return $this->belongsTo(RestaurantBranch::class, 'branch_id');
    }

    public function tables(): HasMany
    {
        return $this->hasMany(DiningTable::class);
    }
}
