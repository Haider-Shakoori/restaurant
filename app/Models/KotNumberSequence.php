<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;

#[Fillable([
    'branch_id',
    'business_date',
    'next_number',
])]
class KotNumberSequence extends Model
{
    use HasUlids;

    protected $connection = 'tenant';

    protected function casts(): array
    {
        return [
            'business_date' => 'date',
            'next_number' => 'integer',
        ];
    }
}
