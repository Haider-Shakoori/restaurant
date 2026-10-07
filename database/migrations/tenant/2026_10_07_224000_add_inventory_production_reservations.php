<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('inventory_reservations', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('branch_id')->constrained('branches')->restrictOnDelete();
            $table->foreignUlid('order_id')->constrained('orders')->cascadeOnDelete();
            $table->foreignUlid('kitchen_ticket_item_id')
                ->unique()
                ->constrained('kitchen_ticket_items')
                ->cascadeOnDelete();
            $table->string('status', 24)->default('reserved')->index();
            $table->timestamp('reserved_at')->index();
            $table->timestamp('committed_at')->nullable();
            $table->timestamp('released_at')->nullable();
            $table->timestamps();
        });

        Schema::create('inventory_reservation_lines', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('inventory_reservation_id')
                ->constrained('inventory_reservations')
                ->cascadeOnDelete();
            $table->foreignUlid('recipe_id')->constrained('recipes')->restrictOnDelete();
            $table->foreignUlid('inventory_item_id')->constrained('inventory_items')->restrictOnDelete();
            $table->decimal('quantity_base', 18, 4);
            $table->timestamps();

            $table->unique(
                ['inventory_reservation_id', 'inventory_item_id'],
                'inventory_reservation_line_unique',
            );
        });

        Schema::table('inventory_consumptions', function (Blueprint $table): void {
            $table->decimal('total_cost', 18, 2)->default(0)->after('consumed_by_user_id');
        });

        Schema::table('inventory_consumption_lines', function (Blueprint $table): void {
            $table->dropUnique('inventory_consumption_line_unique');
            $table->foreignUlid('kitchen_ticket_item_id')
                ->nullable()
                ->after('order_item_id')
                ->constrained('kitchen_ticket_items')
                ->restrictOnDelete();
            $table->decimal('cost_amount', 18, 2)->default(0)->after('quantity_base');
            $table->unique(
                ['inventory_consumption_id', 'kitchen_ticket_item_id', 'inventory_item_id'],
                'inventory_production_consumption_unique',
            );
        });
    }

    public function down(): void
    {
        Schema::table('inventory_consumption_lines', function (Blueprint $table): void {
            $table->dropUnique('inventory_production_consumption_unique');
            $table->dropConstrainedForeignId('kitchen_ticket_item_id');
            $table->dropColumn('cost_amount');
            $table->unique(
                ['inventory_consumption_id', 'order_item_id', 'inventory_item_id'],
                'inventory_consumption_line_unique',
            );
        });

        Schema::table('inventory_consumptions', function (Blueprint $table): void {
            $table->dropColumn('total_cost');
        });

        Schema::dropIfExists('inventory_reservation_lines');
        Schema::dropIfExists('inventory_reservations');
    }
};
