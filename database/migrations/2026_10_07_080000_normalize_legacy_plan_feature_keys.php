<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Support\Facades\DB;

return new class extends Migration
{
    public function up(): void
    {
        $aliases = [
            'Inventory' => 'inventory',
            'Max branches' => 'max_branches',
            'Max devices' => 'max_devices',
            'Max mobile devices' => 'max_mobile_devices',
            'Max waiters' => 'max_waiters',
        ];

        foreach ($aliases as $legacy => $canonical) {
            $rows = DB::table('plan_features')
                ->where('feature_key', $legacy)
                ->get();

            foreach ($rows as $row) {
                $canonicalExists = DB::table('plan_features')
                    ->where('plan_id', $row->plan_id)
                    ->where('feature_key', $canonical)
                    ->exists();

                if ($canonicalExists) {
                    DB::table('plan_features')->where('id', $row->id)->delete();

                    continue;
                }

                DB::table('plan_features')
                    ->where('id', $row->id)
                    ->update([
                        'feature_key' => $canonical,
                        'updated_at' => now(),
                    ]);
            }
        }

        $deviceLimits = DB::table('plan_features')
            ->where('feature_key', 'max_devices')
            ->get();

        foreach ($deviceLimits as $deviceLimit) {
            $mobileLimitExists = DB::table('plan_features')
                ->where('plan_id', $deviceLimit->plan_id)
                ->where('feature_key', 'max_mobile_devices')
                ->exists();

            if ($mobileLimitExists) {
                continue;
            }

            DB::table('plan_features')->insert([
                'plan_id' => $deviceLimit->plan_id,
                'feature_key' => 'max_mobile_devices',
                'value' => $deviceLimit->value,
                'created_at' => now(),
                'updated_at' => now(),
            ]);
        }
    }

    public function down(): void
    {
        // Deliberately irreversible: canonical machine keys are the supported schema.
    }
};
