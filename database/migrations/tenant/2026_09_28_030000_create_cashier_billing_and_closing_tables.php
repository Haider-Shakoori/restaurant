<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('cashier_sessions', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('branch_id')->constrained('branches')->restrictOnDelete();
            $table->foreignId('cashier_user_id')->constrained('users')->restrictOnDelete();
            $table->string('status', 24)->default('open')->index();
            $table->decimal('opening_cash', 18, 2)->default(0);
            $table->decimal('expected_cash', 18, 2)->nullable();
            $table->decimal('declared_cash', 18, 2)->nullable();
            $table->decimal('cash_variance', 18, 2)->nullable();
            $table->timestamp('opened_at')->index();
            $table->timestamp('closed_at')->nullable()->index();
            $table->timestamps();

            $table->index(['branch_id', 'status', 'opened_at']);
            $table->index(['cashier_user_id', 'status']);
        });

        Schema::create('bills', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('order_id')->unique()->constrained('orders')->restrictOnDelete();
            $table->foreignUlid('branch_id')->constrained('branches')->restrictOnDelete();
            $table->foreignId('created_by_user_id')->nullable()->constrained('users')->nullOnDelete();
            $table->string('bill_number', 48)->unique();
            $table->string('status', 24)->default('open')->index();
            $table->decimal('subtotal', 18, 2);
            $table->string('discount_type', 16)->nullable();
            $table->decimal('discount_value', 18, 4)->nullable();
            $table->decimal('discount_amount', 18, 2)->default(0);
            $table->string('discount_reason')->nullable();
            $table->decimal('total', 18, 2);
            $table->decimal('paid_amount', 18, 2)->default(0);
            $table->decimal('balance_due', 18, 2);
            $table->timestamp('issued_at')->index();
            $table->timestamp('paid_at')->nullable()->index();
            $table->timestamp('voided_at')->nullable();
            $table->timestamps();

            $table->index(['branch_id', 'status', 'issued_at']);
        });

        Schema::create('bill_lines', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('bill_id')->constrained('bills')->cascadeOnDelete();
            $table->foreignUlid('order_item_id')->unique()->constrained('order_items')->restrictOnDelete();
            $table->string('item_name');
            $table->unsignedSmallInteger('quantity');
            $table->decimal('unit_price', 18, 2);
            $table->decimal('line_total', 18, 2);
            $table->timestamps();
        });

        Schema::create('tenant_payments', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('bill_id')->constrained('bills')->restrictOnDelete();
            $table->foreignUlid('cashier_session_id')->constrained('cashier_sessions')->restrictOnDelete();
            $table->foreignId('received_by_user_id')->constrained('users')->restrictOnDelete();
            $table->string('client_payment_id', 50)->nullable()->unique();
            $table->string('method', 24)->index();
            $table->decimal('amount', 18, 2);
            $table->string('reference')->nullable();
            $table->string('status', 24)->default('posted')->index();
            $table->timestamp('received_at')->index();
            $table->timestamp('reversed_at')->nullable();
            $table->string('reversal_reason')->nullable();
            $table->timestamps();

            $table->index(['cashier_session_id', 'status', 'received_at']);
        });

        Schema::create('bill_events', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('bill_id')->constrained('bills')->cascadeOnDelete();
            $table->foreignId('actor_user_id')->nullable()->constrained('users')->nullOnDelete();
            $table->string('event_type', 80)->index();
            $table->json('payload')->nullable();
            $table->timestamp('occurred_at')->index();
        });

        Schema::create('daily_closings', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('branch_id')->constrained('branches')->restrictOnDelete();
            $table->date('business_date');
            $table->string('status', 24)->default('open')->index();
            $table->foreignId('created_by_user_id')->nullable()->constrained('users')->nullOnDelete();
            $table->timestamp('finalized_at')->nullable();
            $table->timestamp('reopened_at')->nullable();
            $table->timestamps();

            $table->unique(['branch_id', 'business_date']);
        });

        Schema::create('daily_closing_snapshots', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('daily_closing_id')->constrained('daily_closings')->cascadeOnDelete();
            $table->unsignedInteger('version');
            $table->foreignId('finalized_by_user_id')->constrained('users')->restrictOnDelete();
            $table->unsignedInteger('bill_count')->default(0);
            $table->unsignedInteger('payment_count')->default(0);
            $table->unsignedInteger('cashier_session_count')->default(0);
            $table->decimal('gross_sales', 18, 2)->default(0);
            $table->decimal('discounts', 18, 2)->default(0);
            $table->decimal('net_sales', 18, 2)->default(0);
            $table->decimal('payments_total', 18, 2)->default(0);
            $table->decimal('cash_payments', 18, 2)->default(0);
            $table->decimal('card_payments', 18, 2)->default(0);
            $table->decimal('bank_payments', 18, 2)->default(0);
            $table->decimal('mobile_money_payments', 18, 2)->default(0);
            $table->decimal('other_payments', 18, 2)->default(0);
            $table->decimal('expected_cash', 18, 2)->default(0);
            $table->decimal('declared_cash', 18, 2)->default(0);
            $table->decimal('cash_variance', 18, 2)->default(0);
            $table->timestamp('finalized_at');
            $table->timestamps();

            $table->unique(['daily_closing_id', 'version']);
        });

        Schema::create('daily_closing_events', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('daily_closing_id')->constrained('daily_closings')->cascadeOnDelete();
            $table->foreignId('actor_user_id')->nullable()->constrained('users')->nullOnDelete();
            $table->string('event_type', 80)->index();
            $table->json('payload')->nullable();
            $table->timestamp('occurred_at')->index();
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('daily_closing_events');
        Schema::dropIfExists('daily_closing_snapshots');
        Schema::dropIfExists('daily_closings');
        Schema::dropIfExists('bill_events');
        Schema::dropIfExists('tenant_payments');
        Schema::dropIfExists('bill_lines');
        Schema::dropIfExists('bills');
        Schema::dropIfExists('cashier_sessions');
    }
};
