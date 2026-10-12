<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        // Tenant-scoped role catalog. `users.role` keeps storing the role key
        // (varchar 32) so existing users, middleware and clients stay valid.
        Schema::create('roles', function (Blueprint $table) {
            $table->id();
            $table->ulid('public_id')->unique();
            $table->string('key', 32)->unique();
            $table->string('name', 100);
            $table->string('description', 400)->nullable();
            $table->boolean('is_system')->default(false)->index();
            $table->json('permissions')->nullable();
            $table->timestamps();
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('roles');
    }
};
