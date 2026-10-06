<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('menu_modifier_groups', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->string('name');
            $table->unsignedSmallInteger('min_selections')->default(0);
            $table->unsignedSmallInteger('max_selections')->default(1);
            $table->unsignedInteger('sort_order')->default(0);
            $table->boolean('is_active')->default(true)->index();
            $table->timestamps();
        });

        Schema::create('menu_modifier_options', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('menu_modifier_group_id')
                ->constrained('menu_modifier_groups')
                ->cascadeOnDelete();
            $table->string('name');
            $table->decimal('price_delta', 18, 2)->default(0);
            $table->unsignedInteger('sort_order')->default(0);
            $table->boolean('is_active')->default(true)->index();
            $table->timestamps();
        });

        Schema::create('menu_item_modifier_group', function (Blueprint $table): void {
            $table->foreignUlid('menu_item_id')->constrained('menu_items')->cascadeOnDelete();
            $table->foreignUlid('menu_modifier_group_id')
                ->constrained('menu_modifier_groups')
                ->cascadeOnDelete();
            $table->unsignedInteger('sort_order')->default(0);

            $table->primary(['menu_item_id', 'menu_modifier_group_id']);
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('menu_item_modifier_group');
        Schema::dropIfExists('menu_modifier_options');
        Schema::dropIfExists('menu_modifier_groups');
    }
};
