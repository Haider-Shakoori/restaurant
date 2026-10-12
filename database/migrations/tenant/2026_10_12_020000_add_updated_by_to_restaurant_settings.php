<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        // Inventory/mode switches keep an audit trail of who changed them.
        Schema::table('restaurant_settings', function (Blueprint $table): void {
            $table->foreignId('updated_by_user_id')
                ->nullable()
                ->after('value')
                ->constrained('users')
                ->nullOnDelete();
        });
    }

    public function down(): void
    {
        Schema::table('restaurant_settings', function (Blueprint $table): void {
            $table->dropConstrainedForeignId('updated_by_user_id');
        });
    }
};
