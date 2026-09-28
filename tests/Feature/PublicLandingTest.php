<?php

namespace Tests\Feature;

use App\Enums\BusinessStatus;
use App\Enums\PlatformRole;
use App\Enums\ProvisioningState;
use App\Models\AdminUser;
use App\Models\Business;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\TestCase;

class PublicLandingTest extends TestCase
{
    use RefreshDatabase;

    public function test_guest_sees_product_landing_instead_of_technical_foundation_screen(): void
    {
        $this->get('http://localhost/')
            ->assertOk()
            ->assertSee('Run every table, order, kitchen ticket and closing from one system.')
            ->assertSee('Start 7-day free trial')
            ->assertSee('Platform login')
            ->assertDontSee('Foundation status')
            ->assertDontSee('/api/v1');
    }

    public function test_authenticated_platform_user_is_sent_to_platform_dashboard_from_home(): void
    {
        $admin = AdminUser::factory()->create([
            'role' => PlatformRole::SuperAdmin,
            'is_active' => true,
        ]);

        $this->actingAs($admin)
            ->get('http://localhost/')
            ->assertRedirect('/platform');
    }

    public function test_public_trial_request_creates_pending_restaurant_without_starting_trial_clock(): void
    {
        $response = $this->post('http://localhost/start-trial', [
            'name' => 'Kabul Trial Restaurant',
            'requested_subdomain' => 'kabul-trial',
            'contact_name' => 'Restaurant Owner',
            'phone' => '+93700000123',
            'whatsapp' => '+93700000123',
            'email' => 'owner@example.test',
            'location' => 'Kabul',
        ]);

        $response->assertRedirect('/start-trial?submitted=1');

        $business = Business::sole();

        $this->assertSame(BusinessStatus::Provisioning, $business->status);
        $this->assertSame(ProvisioningState::Pending, $business->provisioning_state);
        $this->assertNull($business->trial_starts_at);
        $this->assertNull($business->trial_ends_at);
        $this->assertDatabaseHas('provisioning_events', [
            'business_id' => $business->id,
            'event' => 'trial.requested',
            'state' => ProvisioningState::Pending->value,
        ]);
    }

    public function test_public_trial_rejects_reserved_subdomain(): void
    {
        $this->post('http://localhost/start-trial', [
            'name' => 'Reserved Restaurant',
            'requested_subdomain' => 'admin',
            'contact_name' => 'Owner',
            'phone' => '+93700000456',
            'location' => 'Kabul',
        ])->assertSessionHasErrors('requested_subdomain');

        $this->assertSame(0, Business::count());
    }

    public function test_system_health_is_internal_to_authenticated_platform_users(): void
    {
        $this->get('http://localhost/platform/system-health')
            ->assertRedirect('/platform/login');

        $admin = AdminUser::factory()->create([
            'role' => PlatformRole::SuperAdmin,
            'is_active' => true,
        ]);

        $this->actingAs($admin)
            ->get('http://localhost/platform/system-health')
            ->assertOk()
            ->assertSee('Foundation status')
            ->assertSee('/api/v1');
    }
}
