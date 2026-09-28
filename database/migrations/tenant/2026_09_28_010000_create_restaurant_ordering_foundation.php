<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('users', function (Blueprint $table): void {
            $table->string('role', 32)->default('waiter')->index()->after('is_active');
        });

        Schema::create('branches', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->string('code', 32)->unique();
            $table->string('name');
            $table->boolean('is_active')->default(true)->index();
            $table->timestamps();
        });

        Schema::create('dining_areas', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('branch_id')->constrained('branches')->cascadeOnDelete();
            $table->string('name');
            $table->unsignedInteger('sort_order')->default(0);
            $table->boolean('is_active')->default(true)->index();
            $table->timestamps();

            $table->unique(['branch_id', 'name']);
        });

        Schema::create('dining_tables', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('dining_area_id')->constrained('dining_areas')->cascadeOnDelete();
            $table->string('code', 50)->unique();
            $table->string('name');
            $table->unsignedSmallInteger('capacity')->default(4);
            $table->string('status', 24)->default('available')->index();
            $table->boolean('is_active')->default(true)->index();
            $table->timestamps();
        });

        Schema::create('menu_categories', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->string('name');
            $table->unsignedInteger('sort_order')->default(0);
            $table->boolean('is_active')->default(true)->index();
            $table->timestamps();
        });

        Schema::create('menu_items', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('menu_category_id')->nullable()->constrained('menu_categories')->nullOnDelete();
            $table->string('sku', 80)->nullable()->unique();
            $table->string('name');
            $table->text('description')->nullable();
            $table->decimal('price', 18, 2);
            $table->boolean('is_available')->default(true)->index();
            $table->unsignedInteger('sort_order')->default(0);
            $table->timestamps();
        });

        Schema::create('orders', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->string('client_order_id', 40)->nullable()->unique();
            $table->foreignUlid('dining_table_id')->constrained('dining_tables')->restrictOnDelete();
            $table->foreignId('waiter_id')->constrained('users')->restrictOnDelete();
            $table->string('status', 24)->default('draft')->index();
            $table->unsignedSmallInteger('guest_count')->default(1);
            $table->text('notes')->nullable();
            $table->decimal('subtotal', 18, 2)->default(0);
            $table->decimal('total', 18, 2)->default(0);
            $table->timestamp('opened_at')->nullable();
            $table->timestamp('submitted_at')->nullable();
            $table->timestamp('served_at')->nullable();
            $table->timestamp('closed_at')->nullable();
            $table->timestamps();

            $table->index(['dining_table_id', 'status']);
            $table->index(['waiter_id', 'status']);
        });

        Schema::create('order_items', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('order_id')->constrained('orders')->cascadeOnDelete();
            $table->foreignUlid('menu_item_id')->nullable()->constrained('menu_items')->nullOnDelete();
            $table->string('client_line_id', 40)->nullable();
            $table->string('item_name');
            $table->decimal('unit_price', 18, 2);
            $table->unsignedSmallInteger('quantity')->default(1);
            $table->decimal('line_total', 18, 2);
            $table->text('notes')->nullable();
            $table->string('status', 24)->default('pending')->index();
            $table->timestamps();

            $table->unique(['order_id', 'client_line_id']);
        });

        Schema::create('order_events', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('order_id')->constrained('orders')->cascadeOnDelete();
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
        Schema::dropIfExists('order_events');
        Schema::dropIfExists('order_items');
        Schema::dropIfExists('orders');
        Schema::dropIfExists('menu_items');
        Schema::dropIfExists('menu_categories');
        Schema::dropIfExists('dining_tables');
        Schema::dropIfExists('dining_areas');
        Schema::dropIfExists('branches');

        Schema::table('users', function (Blueprint $table): void {
            $table->dropColumn('role');
        });
    }
};
