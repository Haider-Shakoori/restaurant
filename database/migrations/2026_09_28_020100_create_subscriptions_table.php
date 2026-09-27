<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('subscriptions', function (Blueprint $table) {
            $table->ulid('id')->primary();
            $table->foreignUlid('business_id')->constrained()->cascadeOnDelete();
            $table->foreignId('plan_id')->constrained();
            $table->foreignId('plan_price_id')->nullable()->constrained()->nullOnDelete();
            $table->foreignId('created_by_admin_id')->nullable()->constrained('admin_users')->nullOnDelete();
            $table->string('source', 32);
            $table->string('status', 32)->index();
            $table->string('billing_cycle', 32)->nullable();
            $table->unsignedInteger('interval_months')->nullable();
            $table->unsignedInteger('custom_days')->nullable();
            $table->decimal('price_snapshot', 18, 2)->default(0);
            $table->char('currency', 3)->default('AFN');
            $table->string('plan_code_snapshot', 64);
            $table->string('plan_name_snapshot');
            $table->json('features_snapshot')->nullable();
            $table->timestamp('starts_at')->index();
            $table->timestamp('ends_at')->index();
            $table->timestamp('activated_at')->nullable();
            $table->timestamp('cancelled_at')->nullable();
            $table->json('metadata')->nullable();
            $table->timestamps();

            $table->index(['business_id', 'starts_at', 'ends_at']);
            $table->index(['business_id', 'status', 'ends_at']);
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('subscriptions');
    }
};
