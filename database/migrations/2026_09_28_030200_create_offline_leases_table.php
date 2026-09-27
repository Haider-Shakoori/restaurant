<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('offline_leases', function (Blueprint $table) {
            $table->ulid('id')->primary();
            $table->foreignUlid('business_id')->constrained()->cascadeOnDelete();
            $table->foreignUlid('license_key_id')->constrained('license_keys')->cascadeOnDelete();
            $table->foreignUlid('device_activation_id')->constrained('device_activations')->cascadeOnDelete();
            $table->foreignUlid('subscription_id')->nullable()->constrained()->nullOnDelete();
            $table->string('key_id', 64);
            $table->unsignedInteger('schema_version')->default(1);
            $table->string('payload_hash', 64)->unique();
            $table->timestamp('issued_at')->index();
            $table->timestamp('expires_at')->index();
            $table->timestamps();

            $table->index(['device_activation_id', 'expires_at']);
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('offline_leases');
    }
};
