<?php

namespace App\Models;

use App\Models\Concerns\RecordsSyncChanges;
use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\HasMany;

#[Fillable([
    'sku',
    'name',
    'base_unit',
    'purchase_unit',
    'purchase_to_base_factor',
    'reorder_level',
    'is_active',
])]
class InventoryItem extends Model
{
    use HasUlids, RecordsSyncChanges;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'purchase_to_base_factor' => 'decimal:6',
            'reorder_level' => 'decimal:4',
            'is_active' => 'boolean',
        ];
    }

    public function balances(): HasMany
    {
        return $this->hasMany(InventoryBalance::class);
    }

    public function movements(): HasMany
    {
        return $this->hasMany(StockMovement::class);
    }

    public function recipeItems(): HasMany
    {
        return $this->hasMany(RecipeItem::class);
    }
}
