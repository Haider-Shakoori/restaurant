<?php

namespace Tests\Unit;

use App\Services\Platform\SubscriptionAccess;
use PHPUnit\Framework\TestCase;

class SubscriptionAccessTest extends TestCase
{
    public function test_legacy_feature_labels_resolve_to_canonical_machine_keys(): void
    {
        $access = new SubscriptionAccess(
            allowed: true,
            status: 'active',
            code: 'ok',
            features: [
                'Inventory' => true,
                'Max branches' => 1,
                'Max devices' => 5,
                'Max mobile devices' => 3,
                'Max waiters' => 10,
            ],
        );

        $this->assertTrue($access->feature('inventory'));
        $this->assertSame(1, $access->feature('max_branches'));
        $this->assertSame(5, $access->feature('max_devices'));
        $this->assertSame(3, $access->feature('max_mobile_devices'));
        $this->assertSame(10, $access->feature('max_waiters'));
        $this->assertSame('fallback', $access->feature('unknown_feature', 'fallback'));
    }
}
