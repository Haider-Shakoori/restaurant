<?php

namespace App\Console\Commands;

use App\Services\Platform\SubscriptionService;
use Illuminate\Console\Command;

class RefreshSubscriptionStates extends Command
{
    protected $signature = 'subscriptions:refresh';

    protected $description = 'Refresh central restaurant lifecycle statuses from authoritative server time';

    public function handle(SubscriptionService $subscriptions): int
    {
        $updated = $subscriptions->refreshLifecycleStates();

        $this->info("Subscription lifecycle refreshed; {$updated} restaurant status record(s) changed.");

        return self::SUCCESS;
    }
}
