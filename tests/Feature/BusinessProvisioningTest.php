<?php

namespace Tests\Feature;

use App\Enums\BusinessStatus;
use App\Enums\PlatformRole;
use App\Enums\ProvisioningState;
use App\Models\AdminUser;
use App\Models\Business;
use App\Models\Plan;
use App\Models\Tenant;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\TestCase;

class BusinessProvisioningTest extends TestCase
{
    use RefreshDatabase;

    /** @var array<int, string> */
    private array $tenantDatabases = [];

    protected function tearDown(): void
    {
        if (tenancy()->initialized) {
            tenancy()->end();
        }

        foreach ($this->tenantDatabases as $database) {
            @unlink(database_path($database));
        }

        parent::tearDown();
    }

    public function test_operator_can_approve_and_provision_pending_restaurant(): void
    {
        config()->set('platform.provisioning.tenant_domain_suffix', 'restaurant.test');

        $operator = AdminUser::factory()->create([
            'role' => PlatformRole::Operator,
            'is_active' => true,
        ]);

        $plan = Plan::create([
            'code' => 'basic',
            'name' => 'Basic',
            'is_active' => true,
            'sort_order' => 10,
        ]);

        $business = Business::create([
            'plan_id' => $plan->id,
            'name' => 'Kabul Grill',
            'requested_subdomain' => 'kabul-grill',
            'contact_name' => 'Owner',
            'phone' => '+93700000000',
            'status' => BusinessStatus::Provisioning,
            'provisioning_state' => ProvisioningState::Pending,
        ]);

        $this->actingAs($operator)
            ->post('http://localhost/platform/restaurants/'.$business->id.'/provision')
            ->assertRedirect()
            ->assertSessionHas('status');

        $business->refresh();
        $tenant = Tenant::findOrFail($business->tenant_id);
        $this->tenantDatabases[] = $tenant->database()->getName();

        $this->assertSame(ProvisioningState::Ready, $business->provisioning_state);
        $this->assertNull($business->provisioning_error);
        $this->assertSame(ProvisioningState::Ready->value, $tenant->provisioning_state);
        $this->assertDatabaseHas('domains', [
            'tenant_id' => $tenant->id,
            'domain' => 'kabul-grill.restaurant.test',
        ]);
        $this->assertDatabaseHas('provisioning_events', [
            'business_id' => $business->id,
            'event' => 'provisioning.ready',
            'state' => ProvisioningState::Ready->value,
        ]);
    }

    public function test_pending_restaurant_screen_shows_provision_button_when_requirements_are_met(): void
    {
        $admin = AdminUser::factory()->create([
            'role' => PlatformRole::SuperAdmin,
            'is_active' => true,
        ]);

        $plan = Plan::create([
            'code' => 'basic',
            'name' => 'Basic',
            'is_active' => true,
            'sort_order' => 10,
        ]);

        $business = Business::create([
            'plan_id' => $plan->id,
            'name' => 'Kabul Grill',
            'requested_subdomain' => 'kabul-grill',
            'contact_name' => 'Owner',
            'phone' => '+93700000000',
            'status' => BusinessStatus::Provisioning,
            'provisioning_state' => ProvisioningState::Pending,
        ]);

        $this->actingAs($admin)
            ->get('http://localhost/platform/restaurants/'.$business->id)
            ->assertOk()
            ->assertSee('Approve & Provision Restaurant')
            ->assertSee('kabul-grill.restaurant.businessos.af');
    }

    public function test_provisioning_requires_plan_and_subdomain(): void
    {
        $operator = AdminUser::factory()->create([
            'role' => PlatformRole::Operator,
            'is_active' => true,
        ]);

        $business = Business::create([
            'name' => 'Incomplete Restaurant',
            'contact_name' => 'Owner',
            'phone' => '+93700000001',
            'status' => BusinessStatus::Provisioning,
            'provisioning_state' => ProvisioningState::Pending,
        ]);

        $this->actingAs($operator)
            ->post('http://localhost/platform/restaurants/'.$business->id.'/provision')
            ->assertSessionHasErrors('provisioning');

        $this->assertNull($business->fresh()->tenant_id);
        $this->assertSame(0, Tenant::count());
    }
}
