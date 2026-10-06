<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('license_keys', function (Blueprint $table): void {
            $table->unsignedInteger('max_mobile_devices_snapshot')
                ->nullable()
                ->after('max_devices_snapshot');
        });
    }

    public function down(): void
    {
        Schema::table('license_keys', function (Blueprint $table): void {
            $table->dropColumn('max_mobile_devices_snapshot');
        });
    }
};
