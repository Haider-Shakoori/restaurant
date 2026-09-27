<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('device_activations', function (Blueprint $table) {
            $table->ulid('id')->primary();
            $table->foreignUlid('business_id')->constrained()->cascadeOnDelete();
            $table->foreignUlid('license_key_id')->constrained('license_keys')->cascadeOnDelete();
            $table->string('device_uid', 191);
            $table->string('device_name')->nullable();
            $table->string('platform', 32)->default('android');
            $table->string('app_version', 50)->nullable();
            $table->string('credential_hash', 64);
            $table->char('credential_last4', 4);
            $table->string('status', 20)->default('active')->index();
            $table->timestamp('activated_at')->index();
            $table->timestamp('last_seen_at')->nullable();
            $table->timestamp('last_verified_at')->nullable();
            $table->timestamp('revoked_at')->nullable()->index();
            $table->json('metadata')->nullable();
            $table->timestamps();

            $table->unique(['business_id', 'device_uid']);
            $table->index(['license_key_id', 'status']);
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('device_activations');
    }
};
