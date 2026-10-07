<?php

namespace App\Services\Tenant;

use App\Models\KotNumberSequence;

class KotNumberService
{
    public function next(string $branchId): string
    {
        $businessDate = now()->toDateString();

        KotNumberSequence::query()->firstOrCreate(
            [
                'branch_id' => $branchId,
                'business_date' => $businessDate,
            ],
            [
                'next_number' => 1,
            ],
        );

        $sequence = KotNumberSequence::query()
            ->where('branch_id', $branchId)
            ->whereDate('business_date', $businessDate)
            ->lockForUpdate()
            ->firstOrFail();

        $number = $sequence->next_number;
        $sequence->update([
            'next_number' => $number + 1,
        ]);

        return 'KOT-'.str_pad((string) $number, 4, '0', STR_PAD_LEFT);
    }
}
