<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

#[Fillable([
    'branch_id',
    'inventory_item_id',
    'actor_user_id',
    'movement_type',
    'quantity_delta',
    'unit_cost',
    'source_type',
    'source_id',
    'source_line_id',
    'idempotency_key',
    'notes',
    'occurred_at',
])]
class StockMovement extends Model
{
    use HasUlids;

    public const TYPE_RECEIPT = 'receipt';

    public const TYPE_CONSUMPTION = 'consumption';

    public const TYPE_ADJUSTMENT = 'adjustment';

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'quantity_delta' => 'decimal:4',
            'unit_cost' => 'decimal:2',
            'occurred_at' => 'datetime',
        ];
    }

    public function branch(): BelongsTo
    {
        return $this->belongsTo(RestaurantBranch::class, 'branch_id');
    }

    public function item(): BelongsTo
    {
        return $this->belongsTo(InventoryItem::class, 'inventory_item_id');
    }

    public function actor(): BelongsTo
    {
        return $this->belongsTo(TenantUser::class, 'actor_user_id');
    }
}
