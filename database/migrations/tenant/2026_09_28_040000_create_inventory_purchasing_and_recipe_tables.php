<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('suppliers', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->string('code', 50)->unique();
            $table->string('name');
            $table->string('phone', 80)->nullable();
            $table->string('email')->nullable();
            $table->text('address')->nullable();
            $table->boolean('is_active')->default(true)->index();
            $table->timestamps();
        });

        Schema::create('inventory_items', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->string('sku', 80)->unique();
            $table->string('name');
            $table->string('base_unit', 24);
            $table->string('purchase_unit', 24)->nullable();
            $table->decimal('purchase_to_base_factor', 18, 6)->default(1);
            $table->decimal('reorder_level', 18, 4)->default(0);
            $table->boolean('is_active')->default(true)->index();
            $table->timestamps();
        });

        Schema::create('inventory_balances', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('branch_id')->constrained('branches')->cascadeOnDelete();
            $table->foreignUlid('inventory_item_id')->constrained('inventory_items')->cascadeOnDelete();
            $table->decimal('quantity', 18, 4)->default(0);
            $table->timestamps();

            $table->unique(['branch_id', 'inventory_item_id']);
        });

        Schema::create('stock_movements', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('branch_id')->constrained('branches')->restrictOnDelete();
            $table->foreignUlid('inventory_item_id')->constrained('inventory_items')->restrictOnDelete();
            $table->foreignId('actor_user_id')->nullable()->constrained('users')->nullOnDelete();
            $table->string('movement_type', 32)->index();
            $table->decimal('quantity_delta', 18, 4);
            $table->decimal('unit_cost', 18, 2)->nullable();
            $table->string('source_type', 64)->index();
            $table->string('source_id', 64);
            $table->string('source_line_id', 64)->nullable();
            $table->string('idempotency_key', 160)->unique();
            $table->text('notes')->nullable();
            $table->timestamp('occurred_at')->index();
            $table->timestamps();

            $table->index(['branch_id', 'inventory_item_id', 'occurred_at']);
            $table->index(['source_type', 'source_id']);
        });

        Schema::create('purchase_orders', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('branch_id')->constrained('branches')->restrictOnDelete();
            $table->foreignUlid('supplier_id')->constrained('suppliers')->restrictOnDelete();
            $table->foreignId('ordered_by_user_id')->nullable()->constrained('users')->nullOnDelete();
            $table->string('po_number', 48)->unique();
            $table->string('status', 32)->default('ordered')->index();
            $table->decimal('estimated_total', 18, 2)->default(0);
            $table->text('notes')->nullable();
            $table->timestamp('ordered_at')->index();
            $table->timestamp('completed_at')->nullable();
            $table->timestamps();

            $table->index(['branch_id', 'status', 'ordered_at']);
        });

        Schema::create('purchase_order_lines', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('purchase_order_id')->constrained('purchase_orders')->cascadeOnDelete();
            $table->foreignUlid('inventory_item_id')->constrained('inventory_items')->restrictOnDelete();
            $table->string('item_name');
            $table->string('purchase_unit', 24);
            $table->decimal('conversion_factor', 18, 6);
            $table->decimal('ordered_purchase_quantity', 18, 4);
            $table->decimal('ordered_base_quantity', 18, 4);
            $table->decimal('received_base_quantity', 18, 4)->default(0);
            $table->decimal('unit_cost', 18, 2);
            $table->decimal('line_total', 18, 2);
            $table->timestamps();
        });

        Schema::create('goods_receipts', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('purchase_order_id')->constrained('purchase_orders')->restrictOnDelete();
            $table->foreignUlid('branch_id')->constrained('branches')->restrictOnDelete();
            $table->foreignUlid('supplier_id')->constrained('suppliers')->restrictOnDelete();
            $table->foreignId('received_by_user_id')->nullable()->constrained('users')->nullOnDelete();
            $table->string('receipt_number', 48)->unique();
            $table->string('client_receipt_id', 64)->nullable()->unique();
            $table->string('status', 24)->default('posted')->index();
            $table->timestamp('received_at')->index();
            $table->text('notes')->nullable();
            $table->timestamps();
        });

        Schema::create('goods_receipt_lines', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('goods_receipt_id')->constrained('goods_receipts')->cascadeOnDelete();
            $table->foreignUlid('purchase_order_line_id')->constrained('purchase_order_lines')->restrictOnDelete();
            $table->foreignUlid('inventory_item_id')->constrained('inventory_items')->restrictOnDelete();
            $table->decimal('received_purchase_quantity', 18, 4);
            $table->decimal('received_base_quantity', 18, 4);
            $table->decimal('unit_cost', 18, 2);
            $table->decimal('line_total', 18, 2);
            $table->timestamps();
        });

        Schema::create('recipes', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('branch_id')->constrained('branches')->cascadeOnDelete();
            $table->foreignUlid('menu_item_id')->constrained('menu_items')->cascadeOnDelete();
            $table->string('name');
            $table->unsignedInteger('version')->default(1);
            $table->boolean('is_active')->default(true)->index();
            $table->timestamps();

            $table->unique(['branch_id', 'menu_item_id', 'version']);
        });

        Schema::create('recipe_items', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('recipe_id')->constrained('recipes')->cascadeOnDelete();
            $table->foreignUlid('inventory_item_id')->constrained('inventory_items')->restrictOnDelete();
            $table->decimal('quantity_base', 18, 4);
            $table->timestamps();

            $table->unique(['recipe_id', 'inventory_item_id']);
        });

        Schema::create('inventory_consumptions', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('order_id')->unique()->constrained('orders')->restrictOnDelete();
            $table->foreignUlid('branch_id')->constrained('branches')->restrictOnDelete();
            $table->foreignId('consumed_by_user_id')->nullable()->constrained('users')->nullOnDelete();
            $table->timestamp('consumed_at')->index();
            $table->timestamps();
        });

        Schema::create('inventory_consumption_lines', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('inventory_consumption_id')->constrained('inventory_consumptions')->cascadeOnDelete();
            $table->foreignUlid('order_item_id')->constrained('order_items')->restrictOnDelete();
            $table->foreignUlid('recipe_id')->constrained('recipes')->restrictOnDelete();
            $table->foreignUlid('inventory_item_id')->constrained('inventory_items')->restrictOnDelete();
            $table->foreignUlid('stock_movement_id')->constrained('stock_movements')->restrictOnDelete();
            $table->decimal('quantity_base', 18, 4);
            $table->timestamps();

            $table->unique([
                'inventory_consumption_id',
                'order_item_id',
                'inventory_item_id',
            ], 'inventory_consumption_line_unique');
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('inventory_consumption_lines');
        Schema::dropIfExists('inventory_consumptions');
        Schema::dropIfExists('recipe_items');
        Schema::dropIfExists('recipes');
        Schema::dropIfExists('goods_receipt_lines');
        Schema::dropIfExists('goods_receipts');
        Schema::dropIfExists('purchase_order_lines');
        Schema::dropIfExists('purchase_orders');
        Schema::dropIfExists('stock_movements');
        Schema::dropIfExists('inventory_balances');
        Schema::dropIfExists('inventory_items');
        Schema::dropIfExists('suppliers');
    }
};
