<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('sync_device_states', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->string('central_device_id', 64);
            $table->foreignId('tenant_user_id')->constrained('users')->cascadeOnDelete();
            $table->string('device_uid', 160);
            $table->unsignedBigInteger('last_pull_cursor')->default(0);
            $table->timestamp('last_push_at')->nullable();
            $table->timestamp('last_pull_at')->nullable();
            $table->timestamp('last_seen_at')->nullable();
            $table->timestamps();

            $table->unique(['central_device_id', 'tenant_user_id']);
        });

        Schema::create('sync_mutations', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->string('central_device_id', 64)->index();
            $table->foreignId('tenant_user_id')->nullable()->constrained('users')->nullOnDelete();
            $table->string('mutation_id', 80);
            $table->string('operation', 64);
            $table->string('entity_type', 64)->nullable();
            $table->string('entity_id', 80)->nullable();
            $table->string('status', 24)->index();
            $table->string('request_hash', 64);
            $table->json('response')->nullable();
            $table->string('error_code', 80)->nullable();
            $table->text('error_message')->nullable();
            $table->timestamp('client_occurred_at')->nullable();
            $table->timestamp('processed_at');
            $table->timestamps();

            $table->unique(['central_device_id', 'mutation_id']);
            $table->index(['tenant_user_id', 'processed_at']);
        });

        Schema::create('sync_changes', function (Blueprint $table): void {
            $table->bigIncrements('sequence');
            $table->string('entity_type', 64)->index();
            $table->string('entity_id', 80)->index();
            $table->string('operation', 16);
            $table->json('payload')->nullable();
            $table->timestamp('occurred_at')->index();

            $table->index(['sequence', 'entity_type']);
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('sync_changes');
        Schema::dropIfExists('sync_mutations');
        Schema::dropIfExists('sync_device_states');
    }
};
