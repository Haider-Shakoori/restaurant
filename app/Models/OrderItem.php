<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;
use Illuminate\Database\Eloquent\Relations\HasOne;

#[Fillable([
    'order_id',
    'menu_item_id',
    'client_line_id',
    'item_name',
    'unit_price',
    'quantity',
    'dispatched_quantity',
    'line_total',
    'notes',
    'status',
])]
class OrderItem extends Model
{
    use HasUlids;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'unit_price' => 'decimal:2',
            'quantity' => 'integer',
            'dispatched_quantity' => 'integer',
            'line_total' => 'decimal:2',
        ];
    }

    public function order(): BelongsTo
    {
        return $this->belongsTo(Order::class);
    }

    public function menuItem(): BelongsTo
    {
        return $this->belongsTo(MenuItem::class);
    }

    public function kitchenTicketItem(): HasOne
    {
        return $this->hasOne(KitchenTicketItem::class);
    }

    public function kitchenTicketItems(): HasMany
    {
        return $this->hasMany(KitchenTicketItem::class);
    }

    public function pendingDispatchQuantity(): int
    {
        return max(0, (int) $this->quantity - (int) $this->dispatched_quantity);
    }
}
