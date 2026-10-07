<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('orders', function (Blueprint $table): void {
            $table->foreignUlid('branch_id')
                ->nullable()
                ->after('client_order_id')
                ->constrained('branches')
                ->restrictOnDelete();
            $table->string('service_type', 24)->default('dine_in')->after('branch_id')->index();
            $table->string('service_reference', 120)->nullable()->after('service_type');
            $table->ulid('dining_table_id')->nullable()->change();
        });

        DB::connection('tenant')
            ->table('orders')
            ->select(['orders.id', 'dining_areas.branch_id'])
            ->join('dining_tables', 'dining_tables.id', '=', 'orders.dining_table_id')
            ->join('dining_areas', 'dining_areas.id', '=', 'dining_tables.dining_area_id')
            ->orderBy('orders.id')
            ->each(function (object $row): void {
                DB::connection('tenant')
                    ->table('orders')
                    ->where('id', $row->id)
                    ->update(['branch_id' => $row->branch_id]);
            });

        Schema::table('order_items', function (Blueprint $table): void {
            $table->unsignedSmallInteger('seat_number')->nullable()->after('notes');
            $table->unsignedSmallInteger('course_number')->nullable()->after('seat_number');
            $table->string('course_name', 80)->nullable()->after('course_number');
            $table->json('modifiers_snapshot')->nullable()->after('course_name');
            $table->text('allergy_instructions')->nullable()->after('modifiers_snapshot');
            $table->text('kitchen_instructions')->nullable()->after('allergy_instructions');
        });

        Schema::table('kitchen_ticket_items', function (Blueprint $table): void {
            $table->unsignedSmallInteger('seat_number')->nullable()->after('notes');
            $table->unsignedSmallInteger('course_number')->nullable()->after('seat_number');
            $table->string('course_name', 80)->nullable()->after('course_number');
            $table->json('modifiers_snapshot')->nullable()->after('course_name');
            $table->text('allergy_instructions')->nullable()->after('modifiers_snapshot');
            $table->text('kitchen_instructions')->nullable()->after('allergy_instructions');
        });
    }

    public function down(): void
    {
        Schema::table('kitchen_ticket_items', function (Blueprint $table): void {
            $table->dropColumn([
                'seat_number',
                'course_number',
                'course_name',
                'modifiers_snapshot',
                'allergy_instructions',
                'kitchen_instructions',
            ]);
        });

        Schema::table('order_items', function (Blueprint $table): void {
            $table->dropColumn([
                'seat_number',
                'course_number',
                'course_name',
                'modifiers_snapshot',
                'allergy_instructions',
                'kitchen_instructions',
            ]);
        });

        Schema::table('orders', function (Blueprint $table): void {
            $table->ulid('dining_table_id')->nullable(false)->change();
            $table->dropConstrainedForeignId('branch_id');
            $table->dropColumn(['service_type', 'service_reference']);
        });
    }
};
