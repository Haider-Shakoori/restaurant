<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('kitchen_ticket_items', function (Blueprint $table): void {
            $table->string('client_operation_id', 80)->nullable()->unique()->after('production_reason');
            $table->text('void_reason')->nullable()->after('client_operation_id');
            $table->foreignId('voided_by_user_id')->nullable()->after('void_reason')->constrained('users')->nullOnDelete();
            $table->timestamp('recalled_at')->nullable()->after('voided_by_user_id');
            $table->text('recall_reason')->nullable()->after('recalled_at');
        });

        Schema::create('production_waste_events', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('kitchen_ticket_item_id')
                ->constrained('kitchen_ticket_items')
                ->restrictOnDelete();
            $table->foreignId('actor_user_id')->nullable()->constrained('users')->nullOnDelete();
            $table->unsignedSmallInteger('quantity');
            $table->text('reason');
            $table->timestamp('occurred_at')->index();
            $table->timestamps();

            $table->index(['kitchen_ticket_item_id', 'occurred_at']);
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('production_waste_events');

        Schema::table('kitchen_ticket_items', function (Blueprint $table): void {
            $table->dropForeign(['voided_by_user_id']);
            $table->dropUnique(['client_operation_id']);
            $table->dropColumn([
                'client_operation_id',
                'void_reason',
                'voided_by_user_id',
                'recalled_at',
                'recall_reason',
            ]);
        });
    }
};
