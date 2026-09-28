<?php

use App\Enums\BusinessStatus;
use App\Enums\ProvisioningState;
use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('businesses', function (Blueprint $table) {
            $table->ulid('id')->primary();
            $table->string('tenant_id')->nullable()->unique();
            $table->foreignId('plan_id')->nullable()->constrained()->nullOnDelete();
            $table->foreignId('assigned_operator_id')->nullable()->constrained('admin_users')->nullOnDelete();
            $table->string('name');
            $table->string('requested_subdomain', 100)->nullable()->unique();
            $table->string('contact_name');
            $table->string('phone', 50);
            $table->string('whatsapp', 50)->nullable();
            $table->string('email')->nullable()->index();
            $table->string('location')->nullable();
            $table->string('status')->default(BusinessStatus::Provisioning->value)->index();
            $table->string('provisioning_state')->default(ProvisioningState::Pending->value)->index();
            $table->text('provisioning_error')->nullable();
            $table->timestamp('last_health_at')->nullable()->index();
            $table->timestamps();

            $table->foreign('tenant_id')->references('id')->on('tenants')->nullOnDelete();
            $table->index(['status', 'provisioning_state']);
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('businesses');
    }
};
