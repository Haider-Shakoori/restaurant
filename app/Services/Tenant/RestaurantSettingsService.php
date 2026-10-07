<?php

namespace App\Services\Tenant;

use App\Models\RestaurantSetting;

class RestaurantSettingsService
{
    public const DEFAULTS = [
        'kitchen_queue_enabled' => true,
        'preparing_stage_enabled' => true,
        'expo_enabled' => false,
        'courses_enabled' => false,
        'kot_sound_enabled' => true,
        'kitchen_warning_minutes' => 10,
        'kitchen_late_minutes' => 20,
        'require_manager_approval_post_kot_void' => false,
        'negative_stock_policy' => 'block',
    ];

    public function all(?string $branchId = null): array
    {
        $values = self::DEFAULTS;

        $global = RestaurantSetting::query()
            ->whereNull('branch_id')
            ->whereIn('key', array_keys(self::DEFAULTS))
            ->pluck('value', 'key');

        foreach ($global as $key => $value) {
            $values[$key] = $this->cast((string) $key, $value);
        }

        if ($branchId !== null) {
            $branch = RestaurantSetting::query()
                ->where('branch_id', $branchId)
                ->whereIn('key', array_keys(self::DEFAULTS))
                ->pluck('value', 'key');

            foreach ($branch as $key => $value) {
                $values[$key] = $this->cast((string) $key, $value);
            }
        }

        return $values;
    }

    public function put(array $values, ?string $branchId = null): array
    {
        foreach ($values as $key => $value) {
            if (! array_key_exists($key, self::DEFAULTS)) {
                continue;
            }

            RestaurantSetting::query()->updateOrCreate(
                [
                    'branch_id' => $branchId,
                    'key' => $key,
                ],
                [
                    'value' => $this->serialize($value),
                ],
            );
        }

        return $this->all($branchId);
    }

    private function cast(string $key, mixed $value): bool|int|string|null
    {
        $default = self::DEFAULTS[$key];

        if (is_bool($default)) {
            return filter_var($value, FILTER_VALIDATE_BOOL);
        }

        if (is_int($default)) {
            return (int) $value;
        }

        return $value === null ? null : (string) $value;
    }

    private function serialize(mixed $value): ?string
    {
        if (is_bool($value)) {
            return $value ? '1' : '0';
        }

        return $value === null ? null : (string) $value;
    }
}
