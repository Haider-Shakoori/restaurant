<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('desktop_entity_links', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->string('central_device_id', 64);
            $table->string('entity_type', 64);
            $table->string('local_entity_id', 100);
            $table->string('cloud_entity_id', 100);
            $table->timestamps();

            $table->unique(
                ['central_device_id', 'entity_type', 'local_entity_id'],
                'desktop_entity_link_local_unique'
            );
            $table->index(
                ['entity_type', 'cloud_entity_id'],
                'desktop_entity_link_cloud_index'
            );
        });

        Schema::create('desktop_operational_records', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->string('central_device_id', 64);
            $table->string('record_type', 64);
            $table->string('local_entity_id', 100);
            $table->foreignId('actor_user_id')->nullable()->constrained('users')->nullOnDelete();
            $table->json('payload');
            $table->timestamp('occurred_at');
            $table->timestamps();

            $table->unique(
                ['central_device_id', 'record_type', 'local_entity_id'],
                'desktop_operational_record_unique'
            );
            $table->index(['record_type', 'occurred_at']);
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('desktop_operational_records');
        Schema::dropIfExists('desktop_entity_links');
    }
};
