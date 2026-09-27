<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('license_keys', function (Blueprint $table) {
            $table->ulid('id')->primary();
            $table->foreignUlid('business_id')->constrained()->cascadeOnDelete();
            $table->foreignId('generated_by_admin_id')->nullable()->constrained('admin_users')->nullOnDelete();
            $table->unsignedInteger('version');
            $table->string('key_hash', 64)->unique();
            $table->string('key_prefix', 12);
            $table->char('key_last4', 4);
            $table->string('status', 20)->default('active')->index();
            $table->unsignedInteger('max_devices_snapshot')->nullable();
            $table->timestamp('generated_at')->index();
            $table->timestamp('last_used_at')->nullable();
            $table->timestamp('revoked_at')->nullable()->index();
            $table->json('metadata')->nullable();
            $table->timestamps();

            $table->unique(['business_id', 'version']);
            $table->index(['business_id', 'status']);
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('license_keys');
    }
};
