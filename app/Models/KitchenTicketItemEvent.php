<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

#[Fillable([
    'kitchen_ticket_item_id',
    'actor_user_id',
    'event_type',
    'from_status',
    'to_status',
    'payload',
    'occurred_at',
])]
class KitchenTicketItemEvent extends Model
{
    use HasUlids;

    public $timestamps = false;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'payload' => 'array',
            'occurred_at' => 'datetime',
        ];
    }

    public function item(): BelongsTo
    {
        return $this->belongsTo(KitchenTicketItem::class, 'kitchen_ticket_item_id');
    }

    public function actor(): BelongsTo
    {
        return $this->belongsTo(TenantUser::class, 'actor_user_id');
    }
}
