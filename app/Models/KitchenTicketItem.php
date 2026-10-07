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
    'started_at',
    'ready_at',
    'completed_at',
    'voided_at',
    'refire_of_kitchen_ticket_item_id',
    'production_reason',
    'client_operation_id',
    'void_reason',
    'voided_by_user_id',
    'recalled_at',
    'recall_reason',
])]
class KitchenTicketItem extends Model
{
    use HasUlids;

    public const STATUS_HELD = 'held';

    public const STATUS_QUEUED = 'queued';

    public const STATUS_ACTIVE = 'active';

    public const STATUS_PREPARING = 'preparing';

    public const STATUS_READY = 'ready';

    public const STATUS_COMPLETED = 'completed';

    public const STATUS_VOIDED = 'voided';

    public const STATUS_CANCELLED = 'cancelled';

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'seat_number' => 'integer',
            'course_number' => 'integer',
            'modifiers_snapshot' => 'array',
            'started_at' => 'datetime',
            'ready_at' => 'datetime',
            'completed_at' => 'datetime',
            'voided_at' => 'datetime',
            'recalled_at' => 'datetime',
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

    public function refireOf(): BelongsTo
    {
        return $this->belongsTo(self::class, 'refire_of_kitchen_ticket_item_id');
    }
}
