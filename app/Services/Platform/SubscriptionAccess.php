<?php

namespace App\Services\Platform;

use App\Models\Subscription;
use Illuminate\Support\Carbon;

final readonly class SubscriptionAccess
{
    /**
     * @param  array<string, mixed>  $features
     */
    public function __construct(
        public bool $allowed,
        public string $status,
        public string $code,
        public ?Subscription $subscription = null,
        public ?Carbon $endsAt = null,
        public array $features = [],
    ) {}

    public function feature(string $key, mixed $default = null): mixed
    {
        if (array_key_exists($key, $this->features)) {
            return $this->features[$key];
        }

        $legacyKey = match ($key) {
            'inventory' => 'Inventory',
            'max_branches' => 'Max branches',
            'max_devices' => 'Max devices',
            'max_mobile_devices' => 'Max mobile devices',
            'max_waiters' => 'Max waiters',
            default => null,
        };

        if ($legacyKey !== null && array_key_exists($legacyKey, $this->features)) {
            return $this->features[$legacyKey];
        }

        return $default;
    }
}
