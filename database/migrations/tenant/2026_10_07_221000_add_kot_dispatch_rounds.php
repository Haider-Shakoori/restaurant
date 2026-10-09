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
        // MySQL does not permit removing an index that still supports a foreign
        // key. Install its non-unique replacement *before* dropping the legacy
        // unique index, and make the migration safe to resume after a failed
        // MySQL DDL statement (MySQL DDL cannot be rolled back atomically).
        if (! Schema::hasTable('kot_number_sequences')) {
            Schema::create('kot_number_sequences', function (Blueprint $table): void {
                $table->ulid('id')->primary();
                $table->foreignUlid('branch_id')->constrained('branches')->cascadeOnDelete();
                $table->date('business_date');
                $table->unsignedInteger('next_number')->default(1);
                $table->timestamps();

                $table->unique(['branch_id', 'business_date']);
            });
        }

        if (! Schema::hasTable('kot_dispatch_rounds')) {
            Schema::create('kot_dispatch_rounds', function (Blueprint $table): void {
                $table->ulid('id')->primary();
                $table->foreignUlid('order_id')->constrained('orders')->cascadeOnDelete();
                $table->unsignedSmallInteger('sequence');
                $table->foreignId('submitted_by_user_id')->nullable()->constrained('users')->nullOnDelete();
                $table->string('client_mutation_id', 80)->nullable();
                $table->string('kot_number', 32);
                $table->string('priority', 16)->default('normal');
                $table->json('workflow_snapshot');
                $table->json('service_context')->nullable();
                $table->json('course_context')->nullable();
                $table->timestamp('sent_at')->index();
                $table->timestamps();

                $table->unique(['order_id', 'sequence']);
                $table->unique(['order_id', 'client_mutation_id']);
                $table->index(['order_id', 'sent_at']);
                $table->index('kot_number');
            });
        }

        if (! Schema::hasColumn('order_items', 'dispatched_quantity')) {
            Schema::table('order_items', function (Blueprint $table): void {
                $table->unsignedSmallInteger('dispatched_quantity')->default(0)->after('quantity');
            });
        }

        if (! Schema::hasColumn('order_items', 'last_dispatched_at')) {
            Schema::table('order_items', function (Blueprint $table): void {
                $table->timestamp('last_dispatched_at')->nullable()->after('status');
            });
        }

        if (! $this->indexExists('kitchen_tickets', 'kitchen_tickets_order_id_kitchen_station_id_index')) {
            Schema::table('kitchen_tickets', function (Blueprint $table): void {
                $table->index(['order_id', 'kitchen_station_id']);
            });
        }

        if ($this->indexExists('kitchen_tickets', 'kitchen_tickets_order_id_kitchen_station_id_unique')) {
            Schema::table('kitchen_tickets', function (Blueprint $table): void {
                $table->dropUnique(['order_id', 'kitchen_station_id']);
            });
        }

        if (! Schema::hasColumn('kitchen_tickets', 'kot_dispatch_round_id')) {
            Schema::table('kitchen_tickets', function (Blueprint $table): void {
                $table->foreignUlid('kot_dispatch_round_id')
                    ->nullable()
                    ->after('order_id')
                    ->constrained('kot_dispatch_rounds')
                    ->cascadeOnDelete();
            });
        }

        if (! Schema::hasColumn('kitchen_tickets', 'human_kot_number')) {
            Schema::table('kitchen_tickets', function (Blueprint $table): void {
                $table->string('human_kot_number', 32)->nullable()->after('ticket_number');
            });
        }

        if (! $this->indexExists('kitchen_tickets', 'kitchen_tickets_kot_dispatch_round_id_status_index')) {
            Schema::table('kitchen_tickets', function (Blueprint $table): void {
                $table->index(['kot_dispatch_round_id', 'status']);
            });
        }

        if (! $this->indexExists('kitchen_ticket_items', 'kitchen_ticket_items_order_item_id_index')) {
            Schema::table('kitchen_ticket_items', function (Blueprint $table): void {
                $table->index('order_item_id');
            });
        }

        if ($this->indexExists('kitchen_ticket_items', 'kitchen_ticket_items_order_item_id_unique')) {
            Schema::table('kitchen_ticket_items', function (Blueprint $table): void {
                $table->dropUnique(['order_item_id']);
            });
        }

        $now = now();

        DB::connection('tenant')
            ->table('kitchen_tickets')
            ->select('order_id')
            ->distinct()
            ->orderBy('order_id')
            ->each(function (object $row) use ($now): void {
                $firstTicket = DB::connection('tenant')
                    ->table('kitchen_tickets')
                    ->where('order_id', $row->order_id)
                    ->orderBy('queued_at')
                    ->first();

                if (! $firstTicket) {
                    return;
                }

                $roundId = (string) Str::ulid();
                $kotNumber = (string) $firstTicket->ticket_number;

                // A resumed migration must not create a duplicate KOT round.
                if (DB::connection('tenant')->table('kot_dispatch_rounds')
                    ->where('order_id', $row->order_id)->exists()) {
                    return;
                }

                DB::connection('tenant')->table('kot_dispatch_rounds')->insert([
                    'id' => $roundId,
                    'order_id' => $row->order_id,
                    'sequence' => 1,
                    'submitted_by_user_id' => $firstTicket->submitted_by_user_id,
                    'client_mutation_id' => null,
                    'kot_number' => $kotNumber,
                    'priority' => 'normal',
                    'workflow_snapshot' => json_encode([
                        'kitchen_queue_enabled' => true,
                        'preparing_stage_enabled' => true,
                    ], JSON_THROW_ON_ERROR),
                    'service_context' => null,
                    'course_context' => null,
                    'sent_at' => $firstTicket->queued_at ?? $now,
                    'created_at' => $now,
                    'updated_at' => $now,
                ]);

                DB::connection('tenant')
                    ->table('kitchen_tickets')
                    ->where('order_id', $row->order_id)
                    ->update([
                        'kot_dispatch_round_id' => $roundId,
                        'human_kot_number' => $kotNumber,
                    ]);
            });

        DB::connection('tenant')
            ->table('order_items')
            ->whereExists(function ($query): void {
                $query->selectRaw('1')
                    ->from('kitchen_ticket_items')
                    ->whereColumn('kitchen_ticket_items.order_item_id', 'order_items.id');
            })
            ->update([
                'dispatched_quantity' => DB::raw('quantity'),
                'last_dispatched_at' => $now,
            ]);
    }

    private function indexExists(string $table, string $name): bool
    {
        foreach (Schema::getIndexes($table) as $index) {
            if (($index['name'] ?? null) === $name) {
                return true;
            }
        }

        return false;
    }

    public function down(): void
    {
        Schema::table('kitchen_ticket_items', function (Blueprint $table): void {
            $table->dropIndex(['order_item_id']);
            $table->unique('order_item_id');
        });

        Schema::table('kitchen_tickets', function (Blueprint $table): void {
            $table->dropIndex(['kot_dispatch_round_id', 'status']);
            $table->dropIndex(['order_id', 'kitchen_station_id']);
            $table->dropConstrainedForeignId('kot_dispatch_round_id');
            $table->dropColumn('human_kot_number');
            $table->unique(['order_id', 'kitchen_station_id']);
        });

        Schema::table('order_items', function (Blueprint $table): void {
            $table->dropColumn(['dispatched_quantity', 'last_dispatched_at']);
        });

        Schema::dropIfExists('kot_dispatch_rounds');
        Schema::dropIfExists('kot_number_sequences');
    }
};
