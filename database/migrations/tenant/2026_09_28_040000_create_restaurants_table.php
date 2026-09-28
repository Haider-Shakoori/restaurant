<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('restaurants', function (Blueprint $table) {
            $table->ulid('id')->primary();
            $table->string('profile_key', 32)->default('primary')->unique();
            $table->string('name');
            $table->string('phone', 50)->nullable();
            $table->string('email')->nullable();
            $table->string('address')->nullable();
            $table->char('country_code', 2)->default('AF');
            $table->char('currency', 3)->default('AFN');
            $table->string('timezone', 64)->default('Asia/Kabul');
            $table->string('locale', 10)->default('en');
            $table->boolean('is_active')->default(true)->index();
            $table->timestamps();
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('restaurants');
    }
};
