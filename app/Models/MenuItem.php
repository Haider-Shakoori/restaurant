<?php

namespace App\Models;

use App\Models\Concerns\RecordsSyncChanges;
use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\BelongsToMany;
use Illuminate\Database\Eloquent\Relations\HasMany;

#[Fillable([
    'menu_category_id',
    'sku',
    'name',
    'description',
    'price',
    'is_available',
    'sort_order',
])]
class MenuItem extends Model
{
    use HasUlids, RecordsSyncChanges;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'price' => 'decimal:2',
            'is_available' => 'boolean',
        ];
    }

    public function category(): BelongsTo
    {
        return $this->belongsTo(MenuCategory::class, 'menu_category_id');
    }

    public function modifierGroups(): BelongsToMany
    {
        return $this->belongsToMany(
            MenuModifierGroup::class,
            'menu_item_modifier_group',
            'menu_item_id',
            'menu_modifier_group_id',
        )->withPivot('sort_order')->orderByPivot('sort_order');
    }

    public function kitchenRoutes(): HasMany
    {
        return $this->hasMany(MenuItemKitchenRoute::class);
    }

    public function recipes(): HasMany
    {
        return $this->hasMany(Recipe::class);
    }
}
