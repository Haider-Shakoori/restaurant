<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;

#[Fillable([
    'branch_id',
    'order_id',
    'kitchen_ticket_item_id',
    'status',
    'reserved_at',
    'committed_at',
    'released_at',
])]
class InventoryReservation extends Model
{
    use HasUlids;

    public const STATUS_RESERVED = 'reserved';

    public const STATUS_COMMITTED = 'committed';

    public const STATUS_RELEASED = 'released';

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'reserved_at' => 'datetime',
            'committed_at' => 'datetime',
            'released_at' => 'datetime',
        ];
    }

    public function branch(): BelongsTo
    {
        return $this->belongsTo(RestaurantBranch::class, 'branch_id');
    }

    public function order(): BelongsTo
    {
        return $this->belongsTo(Order::class);
    }

    public function productionItem(): BelongsTo
    {
        return $this->belongsTo(KitchenTicketItem::class, 'kitchen_ticket_item_id');
    }

    public function lines(): HasMany
    {
        return $this->hasMany(InventoryReservationLine::class);
    }
}
