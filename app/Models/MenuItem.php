<?php

namespace App\Models;

use App\Models\Concerns\RecordsSyncChanges;
use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\BelongsToMany;
use Illuminate\Database\Eloquent\Relations\HasMany;

#[Fillable([
    'menu_category_id',
    'sku',
    'name',
    'name_dari',
    'name_pashto',
    'description',
    'image_path',
    'price',
    'preparation_time_minutes',
    'is_available',
    'sort_order',
])]
class MenuItem extends Model
{
    use HasUlids, RecordsSyncChanges;

    protected $connection = 'tenant';

    protected $appends = [
        'image_url',
    ];

    protected function casts(): array
    {
        return [
            'price' => 'decimal:2',
            'preparation_time_minutes' => 'integer',
            'is_available' => 'boolean',
        ];
    }

    public function getImageUrlAttribute(): ?string
    {
        if (! filled($this->image_path)) {
            return null;
        }

        // The content version keeps immutable browser/desktop caches safe:
        // replacing an image stores a new path (and even an in-place file
        // replacement changes size/mtime), so stale bytes are never served.
        return url('/media/menu-items/'.$this->getKey().'?v='.$this->imageVersion());
    }

    private function imageVersion(): string
    {
        $signature = (string) $this->image_path;

        try {
            $disk = \Illuminate\Support\Facades\Storage::disk('public');
            if ($disk->exists($this->image_path)) {
                $signature .= '|'.$disk->lastModified($this->image_path).'|'.$disk->size($this->image_path);
            }
        } catch (\Throwable) {
            // Remote/unsupported disks fall back to the stored path version.
        }

        return substr(sha1($signature), 0, 12);
    }

    public function category(): BelongsTo
    {
        return $this->belongsTo(MenuCategory::class, 'menu_category_id');
    }

    public function modifierGroups(): BelongsToMany
    {
        return $this->belongsToMany(
            MenuModifierGroup::class,
            'menu_item_modifier_group',
            'menu_item_id',
            'menu_modifier_group_id',
        )->withPivot('sort_order')->orderByPivot('sort_order');
    }

    public function kitchenRoutes(): HasMany
    {
        return $this->hasMany(MenuItemKitchenRoute::class);
    }

    public function recipes(): HasMany
    {
        return $this->hasMany(Recipe::class);
    }
}
