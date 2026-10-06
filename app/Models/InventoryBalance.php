<?php

namespace App\Models;

use App\Models\Concerns\RecordsSyncChanges;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

#[Fillable(['branch_id', 'inventory_item_id', 'quantity'])]
class InventoryBalance extends Model
{
    use HasUlids, RecordsSyncChanges;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return ['quantity' => 'decimal:4'];
    }

    public function branch(): BelongsTo
    {
        return $this->belongsTo(RestaurantBranch::class, 'branch_id');
    }

    public function item(): BelongsTo
    {
        return $this->belongsTo(InventoryItem::class, 'inventory_item_id');
    }
}
