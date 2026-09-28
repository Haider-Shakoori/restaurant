<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

#[Fillable([
    'inventory_consumption_id',
    'order_item_id',
    'recipe_id',
    'inventory_item_id',
    'stock_movement_id',
    'quantity_base',
])]
class InventoryConsumptionLine extends Model
{
    use HasUlids;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return ['quantity_base' => 'decimal:4'];
    }

    public function consumption(): BelongsTo
    {
        return $this->belongsTo(InventoryConsumption::class, 'inventory_consumption_id');
    }

    public function stockMovement(): BelongsTo
    {
        return $this->belongsTo(StockMovement::class);
    }
}
