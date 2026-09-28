<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\HasMany;

#[Fillable(['code', 'name', 'is_active'])]
class RestaurantBranch extends Model
{
    use HasUlids;

    protected $connection = 'tenant';

    protected $table = 'branches';

    protected function casts(): array
    {
        return ['is_active' => 'boolean'];
    }

    public function diningAreas(): HasMany
    {
        return $this->hasMany(DiningArea::class, 'branch_id');
    }

    public function kitchenStations(): HasMany
    {
        return $this->hasMany(KitchenStation::class, 'branch_id');
    }

    public function cashierSessions(): HasMany
    {
        return $this->hasMany(CashierSession::class, 'branch_id');
    }

    public function bills(): HasMany
    {
        return $this->hasMany(Bill::class, 'branch_id');
    }

    public function dailyClosings(): HasMany
    {
        return $this->hasMany(DailyClosing::class, 'branch_id');
    }
}
