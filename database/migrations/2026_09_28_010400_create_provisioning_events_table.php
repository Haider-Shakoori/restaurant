<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('provisioning_events', function (Blueprint $table) {
            $table->ulid('id')->primary();
            $table->foreignUlid('business_id')->constrained()->cascadeOnDelete();
            $table->string('tenant_id')->nullable()->index();
            $table->string('event', 100);
            $table->string('state', 50)->index();
            $table->text('message')->nullable();
            $table->json('context')->nullable();
            $table->timestamp('occurred_at')->index();
            $table->timestamp('created_at')->nullable();

            $table->index(['business_id', 'occurred_at']);
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('provisioning_events');
    }
};
