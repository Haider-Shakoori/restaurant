<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('kitchen_stations', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('branch_id')->constrained('branches')->cascadeOnDelete();
            $table->string('code', 50);
            $table->string('name');
            $table->unsignedInteger('sort_order')->default(0);
            $table->boolean('is_active')->default(true)->index();
            $table->timestamps();

            $table->unique(['branch_id', 'code']);
        });

        Schema::create('menu_item_kitchen_routes', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('menu_item_id')->constrained('menu_items')->cascadeOnDelete();
            $table->foreignUlid('branch_id')->constrained('branches')->cascadeOnDelete();
            $table->foreignUlid('kitchen_station_id')->constrained('kitchen_stations')->cascadeOnDelete();
            $table->timestamps();

            $table->unique(['menu_item_id', 'branch_id']);
        });

        Schema::create('kitchen_tickets', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('order_id')->constrained('orders')->cascadeOnDelete();
            $table->foreignUlid('kitchen_station_id')->constrained('kitchen_stations')->restrictOnDelete();
            $table->foreignId('submitted_by_user_id')->nullable()->constrained('users')->nullOnDelete();
            $table->string('ticket_number', 48)->unique();
            $table->string('status', 24)->default('queued')->index();
            $table->timestamp('queued_at')->index();
            $table->timestamp('started_at')->nullable()->index();
            $table->timestamp('ready_at')->nullable()->index();
            $table->timestamp('completed_at')->nullable();
            $table->timestamps();

            $table->unique(['order_id', 'kitchen_station_id']);
            $table->index(['kitchen_station_id', 'status', 'queued_at']);
        });

        Schema::create('kitchen_ticket_items', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('kitchen_ticket_id')->constrained('kitchen_tickets')->cascadeOnDelete();
            $table->foreignUlid('order_item_id')->constrained('order_items')->cascadeOnDelete();
            $table->string('item_name');
            $table->unsignedSmallInteger('quantity');
            $table->text('notes')->nullable();
            $table->string('status', 24)->default('queued')->index();
            $table->timestamps();

            $table->unique('order_item_id');
        });

        Schema::create('kitchen_ticket_events', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('kitchen_ticket_id')->constrained('kitchen_tickets')->cascadeOnDelete();
            $table->foreignId('actor_user_id')->nullable()->constrained('users')->nullOnDelete();
            $table->string('event_type', 80)->index();
            $table->string('from_status', 24)->nullable();
            $table->string('to_status', 24)->nullable();
            $table->json('payload')->nullable();
            $table->timestamp('occurred_at')->index();
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('kitchen_ticket_events');
        Schema::dropIfExists('kitchen_ticket_items');
        Schema::dropIfExists('kitchen_tickets');
        Schema::dropIfExists('menu_item_kitchen_routes');
        Schema::dropIfExists('kitchen_stations');
    }
};
