<?php

namespace Tests\Feature;

use Tests\TestCase;

class FoundationTest extends TestCase
{
    public function test_foundation_page_is_available(): void
    {
        $this->get('/')
            ->assertOk()
            ->assertSee('BusinessOS')
            ->assertSee('Restaurant')
            ->assertSee('Offline-first waiter app');
    }

    public function test_versioned_api_health_endpoint_is_available(): void
    {
        $this->getJson('/api/v1/health')
            ->assertOk()
            ->assertJsonPath('status', 'ok')
            ->assertJsonPath('service', 'BusinessOS Restaurant')
            ->assertJsonPath('api', 'v1');
    }

    public function test_api_user_endpoint_requires_authentication(): void
    {
        $this->getJson('/api/v1/user')->assertUnauthorized();
    }

    public function test_afghanistan_foundation_defaults_are_configured(): void
    {
        $this->assertSame('AFN', config('restaurant.currency'));
        $this->assertSame('Asia/Kabul', config('restaurant.timezone'));
        $this->assertSame('Asia/Kabul', config('app.timezone'));
        $this->assertSame('rtl', config('restaurant.locales.fa.direction'));
        $this->assertSame('rtl', config('restaurant.locales.ps.direction'));
        $this->assertTrue(config('restaurant.performance.low_bandwidth_mode'));
    }
}
