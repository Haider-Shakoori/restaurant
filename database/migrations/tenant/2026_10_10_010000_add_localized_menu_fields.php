<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('menu_categories', function (Blueprint $table): void {
            $table->string('name_dari')->nullable()->after('name');
            $table->string('name_pashto')->nullable()->after('name_dari');
        });

        Schema::table('menu_items', function (Blueprint $table): void {
            $table->string('name_dari')->nullable()->after('name');
            $table->string('name_pashto')->nullable()->after('name_dari');
            $table->unsignedSmallInteger('preparation_time_minutes')->nullable()->after('price');
        });
    }

    public function down(): void
    {
        Schema::table('menu_items', function (Blueprint $table): void {
            $table->dropColumn(['name_dari', 'name_pashto', 'preparation_time_minutes']);
        });

        Schema::table('menu_categories', function (Blueprint $table): void {
            $table->dropColumn(['name_dari', 'name_pashto']);
        });
    }
};
