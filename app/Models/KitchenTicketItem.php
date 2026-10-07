<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

#[Fillable([
    'kitchen_ticket_id',
    'order_item_id',
    'item_name',
    'quantity',
    'notes',
    'seat_number',
    'course_number',
    'course_name',
    'modifiers_snapshot',
    'allergy_instructions',
    'kitchen_instructions',
    'status',
])]
class KitchenTicketItem extends Model
{
    use HasUlids;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'seat_number' => 'integer',
            'course_number' => 'integer',
            'modifiers_snapshot' => 'array',
        ];
    }

    public function ticket(): BelongsTo
    {
        return $this->belongsTo(KitchenTicket::class, 'kitchen_ticket_id');
    }

    public function orderItem(): BelongsTo
    {
        return $this->belongsTo(OrderItem::class);
    }
}
