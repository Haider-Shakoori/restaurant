<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('kitchen_ticket_items', function (Blueprint $table): void {
            $table->timestamp('started_at')->nullable()->after('status')->index();
            $table->timestamp('ready_at')->nullable()->after('started_at')->index();
            $table->timestamp('completed_at')->nullable()->after('ready_at');
            $table->timestamp('voided_at')->nullable()->after('completed_at');
            $table->foreignUlid('refire_of_kitchen_ticket_item_id')
                ->nullable()
                ->after('voided_at')
                ->constrained('kitchen_ticket_items')
                ->nullOnDelete();
            $table->text('production_reason')->nullable()->after('refire_of_kitchen_ticket_item_id');
        });
    }

    public function down(): void
    {
        Schema::table('kitchen_ticket_items', function (Blueprint $table): void {
            $table->dropConstrainedForeignId('refire_of_kitchen_ticket_item_id');
            $table->dropColumn([
                'started_at',
                'ready_at',
                'completed_at',
                'voided_at',
                'production_reason',
            ]);
        });
    }
};
