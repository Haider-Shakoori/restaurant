<?php

namespace App\Models;

use App\Models\Concerns\RecordsSyncChanges;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;

#[Fillable([
    'purchase_order_id',
    'branch_id',
    'supplier_id',
    'received_by_user_id',
    'receipt_number',
    'client_receipt_id',
    'status',
    'received_at',
    'notes',
])]
class GoodsReceipt extends Model
{
    use HasUlids, RecordsSyncChanges;

    public const STATUS_POSTED = 'posted';

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return ['received_at' => 'datetime'];
    }

    public function purchaseOrder(): BelongsTo
    {
        return $this->belongsTo(PurchaseOrder::class);
    }

    public function branch(): BelongsTo
    {
        return $this->belongsTo(RestaurantBranch::class, 'branch_id');
    }

    public function supplier(): BelongsTo
    {
        return $this->belongsTo(Supplier::class);
    }

    public function lines(): HasMany
    {
        return $this->hasMany(GoodsReceiptLine::class);
    }
}
