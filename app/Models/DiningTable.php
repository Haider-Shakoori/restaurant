<?php

namespace App\Models;

use App\Models\Concerns\RecordsSyncChanges;
use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;

#[Fillable(['dining_area_id', 'code', 'name', 'capacity', 'status', 'is_active'])]
class DiningTable extends Model
{
    use HasUlids, RecordsSyncChanges;

    public const STATUS_AVAILABLE = 'available';

    public const STATUS_OCCUPIED = 'occupied';

    public const STATUS_RESERVED = 'reserved';

    public const STATUS_DISABLED = 'disabled';

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return ['is_active' => 'boolean'];
    }

    public function diningArea(): BelongsTo
    {
        return $this->belongsTo(DiningArea::class);
    }

    public function orders(): HasMany
    {
        return $this->hasMany(Order::class);
    }
}
