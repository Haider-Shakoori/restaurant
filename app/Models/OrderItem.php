<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
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
    'last_dispatched_at',
])]
class OrderItem extends Model
{
    use HasUlids;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'unit_price' => 'decimal:2',
            'line_total' => 'decimal:2',
            'dispatched_quantity' => 'integer',
            'last_dispatched_at' => 'datetime',
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
}
