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
        return data_get($this->features, $key, $default);
    }
}
