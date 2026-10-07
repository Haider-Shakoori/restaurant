<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;
use Illuminate\Support\Str;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('kot_number_sequences', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('branch_id')->constrained('branches')->cascadeOnDelete();
            $table->date('business_date');
            $table->unsignedInteger('last_number')->default(0);
            $table->timestamps();

            $table->unique(['branch_id', 'business_date']);
        });

        Schema::create('kot_rounds', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('order_id')->constrained('orders')->cascadeOnDelete();
            $table->foreignUlid('branch_id')->constrained('branches')->restrictOnDelete();
            $table->foreignId('submitted_by_user_id')->nullable()->constrained('users')->nullOnDelete();
            $table->unsignedInteger('round_number');
            $table->unsignedInteger('display_number');
            $table->date('business_date');
            $table->string('client_dispatch_id', 80)->nullable();
            $table->timestamp('dispatched_at')->index();
            $table->timestamps();

            $table->unique(['order_id', 'round_number']);
            $table->unique(['order_id', 'client_dispatch_id']);
            $table->unique(['branch_id', 'business_date', 'display_number'], 'kot_round_display_unique');
        });

        Schema::table('order_items', function (Blueprint $table): void {
            $table->unsignedSmallInteger('dispatched_quantity')->default(0)->after('quantity');
        });

        DB::table('kitchen_ticket_items')
            ->select(['order_item_id'])
            ->distinct()
            ->orderBy('order_item_id')
            ->get()
            ->each(function (object $row): void {
                DB::table('order_items')
                    ->where('id', $row->order_item_id)
                    ->update(['dispatched_quantity' => DB::raw('quantity')]);
            });

        Schema::table('kitchen_tickets', function (Blueprint $table): void {
            $table->foreignUlid('kot_round_id')->nullable()->after('order_id')->constrained('kot_rounds')->cascadeOnDelete();
            $table->dropUnique(['order_id', 'kitchen_station_id']);
            $table->unique(['kot_round_id', 'kitchen_station_id']);
            $table->index(['order_id', 'kot_round_id']);
        });

        $legacyOrders = DB::table('kitchen_tickets')
            ->join('orders', 'orders.id', '=', 'kitchen_tickets.order_id')
            ->join('dining_tables', 'dining_tables.id', '=', 'orders.dining_table_id')
            ->join('dining_areas', 'dining_areas.id', '=', 'dining_tables.dining_area_id')
            ->select([
                'kitchen_tickets.order_id',
                'dining_areas.branch_id',
                DB::raw('MIN(kitchen_tickets.queued_at) as queued_at'),
            ])
            ->groupBy('kitchen_tickets.order_id', 'dining_areas.branch_id')
            ->orderBy('queued_at')
            ->get();

        $sequences = [];

        foreach ($legacyOrders as $legacy) {
            $businessDate = substr((string) $legacy->queued_at, 0, 10);
            $sequenceKey = $legacy->branch_id.'|'.$businessDate;
            $displayNumber = ($sequences[$sequenceKey] ?? 0) + 1;
            $sequences[$sequenceKey] = $displayNumber;
            $roundId = (string) Str::ulid();

            DB::table('kot_rounds')->insert([
                'id' => $roundId,
                'order_id' => $legacy->order_id,
                'branch_id' => $legacy->branch_id,
                'submitted_by_user_id' => null,
                'round_number' => 1,
                'display_number' => $displayNumber,
                'business_date' => $businessDate,
                'client_dispatch_id' => null,
                'dispatched_at' => $legacy->queued_at,
                'created_at' => $legacy->queued_at,
                'updated_at' => $legacy->queued_at,
            ]);

            DB::table('kitchen_tickets')
                ->where('order_id', $legacy->order_id)
                ->update(['kot_round_id' => $roundId]);
        }

        foreach ($sequences as $key => $lastNumber) {
            [$branchId, $businessDate] = explode('|', $key, 2);

            DB::table('kot_number_sequences')->insert([
                'id' => (string) Str::ulid(),
                'branch_id' => $branchId,
                'business_date' => $businessDate,
                'last_number' => $lastNumber,
                'created_at' => now(),
                'updated_at' => now(),
            ]);
        }

        Schema::table('kitchen_ticket_items', function (Blueprint $table): void {
            $table->timestamp('started_at')->nullable()->after('status');
            $table->timestamp('ready_at')->nullable()->after('started_at');
            $table->timestamp('completed_at')->nullable()->after('ready_at');
        });

        Schema::create('kitchen_ticket_item_events', function (Blueprint $table): void {
            $table->ulid('id')->primary();
            $table->foreignUlid('kitchen_ticket_item_id')->constrained('kitchen_ticket_items')->cascadeOnDelete();
            $table->foreignId('actor_user_id')->nullable()->constrained('users')->nullOnDelete();
            $table->string('event_type', 80)->index();
            $table->string('from_status', 24)->nullable();
            $table->string('to_status', 24)->nullable();
            $table->json('payload')->nullable();
            $table->timestamp('occurred_at')->index();
        });

        Schema::table('inventory_consumption_lines', function (Blueprint $table): void {
            $table->dropUnique('inventory_consumption_line_unique');
            $table->foreignUlid('kitchen_ticket_item_id')
                ->nullable()
                ->after('order_item_id')
                ->constrained('kitchen_ticket_items')
                ->restrictOnDelete();
            $table->unique(
                ['inventory_consumption_id', 'kitchen_ticket_item_id', 'inventory_item_id'],
                'inventory_consumption_ticket_item_unique',
            );
            $table->index(['inventory_consumption_id', 'order_item_id']);
        });

        DB::table('inventory_consumption_lines')
            ->select(['id', 'order_item_id'])
            ->orderBy('id')
            ->get()
            ->each(function (object $line): void {
                $ticketItemId = DB::table('kitchen_ticket_items')
                    ->where('order_item_id', $line->order_item_id)
                    ->value('id');

                if ($ticketItemId) {
                    DB::table('inventory_consumption_lines')
                        ->where('id', $line->id)
                        ->update(['kitchen_ticket_item_id' => $ticketItemId]);
                }
            });
    }

    public function down(): void
    {
        Schema::table('inventory_consumption_lines', function (Blueprint $table): void {
            $table->dropUnique('inventory_consumption_ticket_item_unique');
            $table->dropIndex(['inventory_consumption_id', 'order_item_id']);
            $table->dropConstrainedForeignId('kitchen_ticket_item_id');
            $table->unique(
                ['inventory_consumption_id', 'order_item_id', 'inventory_item_id'],
                'inventory_consumption_line_unique',
            );
        });

        Schema::dropIfExists('kitchen_ticket_item_events');

        Schema::table('kitchen_ticket_items', function (Blueprint $table): void {
            $table->dropColumn(['started_at', 'ready_at', 'completed_at']);
        });

        Schema::table('kitchen_tickets', function (Blueprint $table): void {
            $table->dropUnique(['kot_round_id', 'kitchen_station_id']);
            $table->dropIndex(['order_id', 'kot_round_id']);
            $table->dropConstrainedForeignId('kot_round_id');
            $table->unique(['order_id', 'kitchen_station_id']);
        });

        Schema::table('order_items', function (Blueprint $table): void {
            $table->dropColumn('dispatched_quantity');
        });

        Schema::dropIfExists('kot_rounds');
        Schema::dropIfExists('kot_number_sequences');
    }
};
