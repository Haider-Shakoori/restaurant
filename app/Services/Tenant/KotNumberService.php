<?php

namespace App\Services\Tenant;

use App\Models\KotNumberSequence;
use Illuminate\Database\UniqueConstraintViolationException;

class KotNumberService
{
    public function next(string $branchId): string
    {
        $businessDate = now()->toDateString();

        $sequence = KotNumberSequence::query()
            ->where('branch_id', $branchId)
            ->whereDate('business_date', $businessDate)
            ->lockForUpdate()
            ->first();

        if (! $sequence) {
            try {
                KotNumberSequence::query()->create([
                    'branch_id' => $branchId,
                    'business_date' => $businessDate,
                    'next_number' => 1,
                ]);
            } catch (UniqueConstraintViolationException) {
                // A concurrent dispatcher created today's sequence first.
            }

            $sequence = KotNumberSequence::query()
                ->where('branch_id', $branchId)
                ->whereDate('business_date', $businessDate)
                ->lockForUpdate()
                ->firstOrFail();
        }

        $number = $sequence->next_number;
        $sequence->update([
            'next_number' => $number + 1,
        ]);

        return 'KOT-'.str_pad((string) $number, 4, '0', STR_PAD_LEFT);
    }
}
