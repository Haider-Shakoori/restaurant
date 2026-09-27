<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('plan_prices', function (Blueprint $table) {
            $table->id();
            $table->foreignId('plan_id')->constrained()->cascadeOnDelete();
            $table->string('billing_cycle', 32);
            $table->unsignedInteger('interval_months')->nullable();
            $table->decimal('price', 18, 2);
            $table->char('currency', 3)->default('AFN');
            $table->boolean('is_active')->default(true)->index();
            $table->unsignedInteger('sort_order')->default(0);
            $table->timestamps();

            $table->unique(['plan_id', 'billing_cycle', 'interval_months']);
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('plan_prices');
    }
};
