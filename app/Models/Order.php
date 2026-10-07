<?php

namespace App\Models;

use App\Models\Concerns\RecordsSyncChanges;
use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;
use Illuminate\Database\Eloquent\Relations\HasOne;

#[Fillable([
    'client_order_id',
    'branch_id',
    'service_type',
    'service_reference',
    'dining_table_id',
    'waiter_id',
    'status',
    'guest_count',
    'notes',
    'subtotal',
    'total',
    'opened_at',
    'submitted_at',
    'served_at',
    'closed_at',
])]
class Order extends Model
{
    use HasUlids, RecordsSyncChanges;

    public const SERVICE_DINE_IN = 'dine_in';

    public const SERVICE_TAKEAWAY = 'takeaway';

    public const SERVICE_DELIVERY = 'delivery';

    public const SERVICE_COUNTER = 'counter';

    public const SERVICE_TYPES = [
        self::SERVICE_DINE_IN,
        self::SERVICE_TAKEAWAY,
        self::SERVICE_DELIVERY,
        self::SERVICE_COUNTER,
    ];

    public const STATUS_DRAFT = 'draft';

    public const STATUS_SUBMITTED = 'submitted';

    public const STATUS_PREPARING = 'preparing';

    public const STATUS_READY = 'ready';

    public const STATUS_SERVED = 'served';

    public const STATUS_BILLED = 'billed';

    public const STATUS_CLOSED = 'closed';

    public const STATUS_CANCELLED = 'cancelled';

    public const EDITABLE_STATUSES = [
        self::STATUS_DRAFT,
        self::STATUS_SUBMITTED,
        self::STATUS_PREPARING,
        self::STATUS_READY,
        self::STATUS_SERVED,
    ];

    public const ACTIVE_STATUSES = [
        self::STATUS_DRAFT,
        self::STATUS_SUBMITTED,
        self::STATUS_PREPARING,
        self::STATUS_READY,
        self::STATUS_SERVED,
        self::STATUS_BILLED,
    ];

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'subtotal' => 'decimal:2',
            'total' => 'decimal:2',
            'opened_at' => 'datetime',
            'submitted_at' => 'datetime',
            'served_at' => 'datetime',
            'closed_at' => 'datetime',
        ];
    }

    public function branch(): BelongsTo
    {
        return $this->belongsTo(RestaurantBranch::class, 'branch_id');
    }

    public function table(): BelongsTo
    {
        return $this->belongsTo(DiningTable::class, 'dining_table_id');
    }

    public function waiter(): BelongsTo
    {
        return $this->belongsTo(TenantUser::class, 'waiter_id');
    }

    public function items(): HasMany
    {
        return $this->hasMany(OrderItem::class);
    }

    public function events(): HasMany
    {
        return $this->hasMany(OrderEvent::class)->orderBy('occurred_at');
    }

    public function kotRounds(): HasMany
    {
        return $this->hasMany(KotDispatchRound::class)->orderBy('sequence');
    }

    public function kitchenTickets(): HasMany
    {
        return $this->hasMany(KitchenTicket::class)->orderBy('queued_at');
    }

    public function bill(): HasOne
    {
        return $this->hasOne(Bill::class);
    }

    public function inventoryConsumption(): HasOne
    {
        return $this->hasOne(InventoryConsumption::class);
    }
}
