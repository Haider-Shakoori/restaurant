<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;

#[Fillable([
    'purchase_order_id',
    'inventory_item_id',
    'item_name',
    'purchase_unit',
    'conversion_factor',
    'ordered_purchase_quantity',
    'ordered_base_quantity',
    'received_base_quantity',
    'unit_cost',
    'line_total',
])]
class PurchaseOrderLine extends Model
{
    use HasUlids;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'conversion_factor' => 'decimal:6',
            'ordered_purchase_quantity' => 'decimal:4',
            'ordered_base_quantity' => 'decimal:4',
            'received_base_quantity' => 'decimal:4',
            'unit_cost' => 'decimal:2',
            'line_total' => 'decimal:2',
        ];
    }

    public function purchaseOrder(): BelongsTo
    {
        return $this->belongsTo(PurchaseOrder::class);
    }

    public function item(): BelongsTo
    {
        return $this->belongsTo(InventoryItem::class, 'inventory_item_id');
    }

    public function receiptLines(): HasMany
    {
        return $this->hasMany(GoodsReceiptLine::class);
    }
}
