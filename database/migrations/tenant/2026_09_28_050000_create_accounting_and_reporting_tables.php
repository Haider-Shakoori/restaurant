<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('chart_accounts', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->string('code', 32)->unique();
            $table->string('name');
            $table->string('type', 24)->index();
            $table->string('normal_balance', 8);
            $table->boolean('is_contra')->default(false);
            $table->string('system_key', 64)->nullable()->unique();
            $table->boolean('is_active')->default(true)->index();
            $table->timestamps();
        });

        Schema::create('journal_entries', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('branch_id')->nullable()->constrained('branches')->nullOnDelete();
            $table->foreignId('posted_by_user_id')->nullable()->constrained('users')->nullOnDelete();
            $table->string('entry_number', 48)->unique();
            $table->string('source_type', 64)->index();
            $table->string('source_id', 64);
            $table->string('source_action', 64);
            $table->string('idempotency_key', 180)->unique();
            $table->string('description');
            $table->date('entry_date')->index();
            $table->string('status', 24)->default('posted')->index();
            $table->foreignUlid('reversal_of_id')->nullable()->constrained('journal_entries')->nullOnDelete();
            $table->timestamp('posted_at');
            $table->timestamps();

            $table->index(['source_type', 'source_id']);
            $table->index(['branch_id', 'entry_date']);
        });

        Schema::create('journal_lines', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('journal_entry_id')->constrained('journal_entries')->cascadeOnDelete();
            $table->foreignUlid('account_id')->constrained('chart_accounts')->restrictOnDelete();
            $table->decimal('debit', 18, 2)->default(0);
            $table->decimal('credit', 18, 2)->default(0);
            $table->string('memo')->nullable();
            $table->string('counterparty_type', 40)->nullable()->index();
            $table->string('counterparty_id', 64)->nullable();
            $table->timestamps();

            $table->index(['account_id', 'journal_entry_id']);
            $table->index(['counterparty_type', 'counterparty_id']);
        });

        Schema::create('inventory_valuations', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('branch_id')->constrained('branches')->cascadeOnDelete();
            $table->foreignUlid('inventory_item_id')->constrained('inventory_items')->cascadeOnDelete();
            $table->decimal('quantity', 18, 4)->default(0);
            $table->decimal('value', 18, 2)->default(0);
            $table->decimal('average_unit_cost', 18, 6)->default(0);
            $table->timestamps();

            $table->unique(['branch_id', 'inventory_item_id']);
        });

        Schema::create('operating_expenses', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('branch_id')->constrained('branches')->restrictOnDelete();
            $table->foreignUlid('expense_account_id')->constrained('chart_accounts')->restrictOnDelete();
            $table->foreignUlid('payment_account_id')->constrained('chart_accounts')->restrictOnDelete();
            $table->foreignId('created_by_user_id')->nullable()->constrained('users')->nullOnDelete();
            $table->string('expense_number', 48)->unique();
            $table->string('client_expense_id', 80)->nullable()->unique();
            $table->string('description');
            $table->decimal('amount', 18, 2);
            $table->date('expense_date')->index();
            $table->string('reference')->nullable();
            $table->string('status', 24)->default('posted')->index();
            $table->timestamps();

            $table->index(['branch_id', 'expense_date']);
        });

        Schema::create('supplier_payments', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('branch_id')->constrained('branches')->restrictOnDelete();
            $table->foreignUlid('supplier_id')->constrained('suppliers')->restrictOnDelete();
            $table->foreignUlid('payment_account_id')->constrained('chart_accounts')->restrictOnDelete();
            $table->foreignId('paid_by_user_id')->nullable()->constrained('users')->nullOnDelete();
            $table->string('payment_number', 48)->unique();
            $table->string('client_supplier_payment_id', 80)->nullable()->unique();
            $table->decimal('amount', 18, 2);
            $table->date('payment_date')->index();
            $table->string('reference')->nullable();
            $table->string('status', 24)->default('posted')->index();
            $table->timestamps();

            $table->index(['branch_id', 'supplier_id', 'payment_date']);
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('supplier_payments');
        Schema::dropIfExists('operating_expenses');
        Schema::dropIfExists('inventory_valuations');
        Schema::dropIfExists('journal_lines');
        Schema::dropIfExists('journal_entries');
        Schema::dropIfExists('chart_accounts');
    }
};
