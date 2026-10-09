<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('waiter_push_devices', function (Blueprint $table): void {
            $table->id();
            $table->string('central_device_id', 40)->unique();
            $table->foreignId('tenant_user_id')->constrained('tenant_users')->cascadeOnDelete();
            $table->text('fcm_token');
            $table->string('platform', 12);
            $table->boolean('enabled')->default(true);
            $table->timestamps();
            $table->index(['tenant_user_id', 'enabled']);
        });

        Schema::create('waiter_push_deliveries', function (Blueprint $table): void {
            $table->id();
            $table->string('central_device_id', 40);
            $table->string('ready_item_id', 40);
            $table->string('order_id', 40);
            $table->string('event_key', 120);
            $table->unsignedSmallInteger('attempts')->default(0);
            $table->timestamp('next_attempt_at')->nullable();
            $table->timestamp('sent_at')->nullable();
            $table->timestamp('expires_at');
            $table->string('last_error', 80)->nullable();
            $table->timestamps();
            $table->unique(['event_key', 'central_device_id'], 'waiter_push_once_per_device');
            $table->index(['sent_at', 'next_attempt_at', 'expires_at'], 'waiter_push_due');
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('waiter_push_deliveries');
        Schema::dropIfExists('waiter_push_devices');
    }
};
