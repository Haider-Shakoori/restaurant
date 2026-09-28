<?php

namespace App\Models\Concerns;

use App\Models\SyncChange;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Support\Str;

trait RecordsSyncChanges
{
    public static function bootRecordsSyncChanges(): void
    {
        static::saved(function (Model $model): void {
            static::recordSyncChange($model, 'upsert');
        });

        static::deleted(function (Model $model): void {
            static::recordSyncChange($model, 'delete');
        });
    }

    private static function recordSyncChange(Model $model, string $operation): void
    {
        if (! tenancy()->initialized) {
            return;
        }

        SyncChange::query()->create([
            'entity_type' => Str::snake(class_basename($model)),
            'entity_id' => (string) $model->getKey(),
            'operation' => $operation,
            'payload' => $operation === 'delete' ? [
                'id' => (string) $model->getKey(),
            ] : $model->attributesToArray(),
            'occurred_at' => now(),
        ]);
    }
}
