<?php

namespace Tests\Feature;

use App\Enums\BusinessStatus;
use App\Enums\PlatformRole;
use App\Enums\ProvisioningState;
use App\Models\AdminUser;
use App\Models\Business;
use App\Models\Plan;
use App\Models\Tenant;
use App\Models\TenantUser;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Hash;
use Tests\TestCase;

class PlatformTenantOwnerAccessTest extends TestCase
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

    public function test_platform_admin_can_assign_explicit_owner_email_and_temporary_password(): void
    {
        config()->set('platform.provisioning.tenant_domain_suffix', 'restaurant.test');

        $admin = AdminUser::factory()->create([
            'role' => PlatformRole::SuperAdmin,
            'is_active' => true,
        ]);

        $plan = Plan::create([
            'code' => 'owner-access',
            'name' => 'Owner Access',
            'is_active' => true,
            'sort_order' => 10,
        ]);

        $business = Business::create([
            'plan_id' => $plan->id,
            'name' => 'Grill Restaurant',
            'requested_subdomain' => 'grill',
            'contact_name' => 'Grill Owner',
            'phone' => '+93700000010',
            'status' => BusinessStatus::Provisioning,
            'provisioning_state' => ProvisioningState::Pending,
        ]);

        $this->actingAs($admin)
            ->post('http://localhost/platform/restaurants/'.$business->id.'/provision')
            ->assertSessionHas('status');

        $business->refresh();
        $tenant = Tenant::findOrFail($business->tenant_id);
        $this->tenantDatabases[] = $tenant->database()->getName();

        $password = 'GrillTest2026';

        $this->actingAs($admin)
            ->from('http://localhost/platform/restaurants/'.$business->id)
            ->post('http://localhost/platform/restaurants/'.$business->id.'/owner-access/reset', [
                'owner_email' => 'owner@grill.test',
                'temporary_password' => $password,
                'temporary_password_confirmation' => $password,
            ])
            ->assertRedirect('http://localhost/platform/restaurants/'.$business->id)
            ->assertSessionHas(
                'owner_credentials',
                fn (array $credentials): bool => $credentials['email'] === 'owner@grill.test'
                    && $credentials['password'] === $password
                    && $credentials['domain'] === 'grill.restaurant.test'
            );

        $this->assertSame('owner@grill.test', $business->fresh()->email);

        tenancy()->initialize($tenant);

        try {
            $owner = TenantUser::query()->where('role', 'owner')->firstOrFail();

            $this->assertSame('owner@grill.test', $owner->email);
            $this->assertTrue($owner->is_active);
            $this->assertTrue(Hash::check($password, $owner->password));
        } finally {
            tenancy()->end();
        }
    }

    public function test_owner_access_screen_exposes_email_and_temporary_password_fields(): void
    {
        config()->set('platform.provisioning.tenant_domain_suffix', 'restaurant.test');

        $admin = AdminUser::factory()->create([
            'role' => PlatformRole::SuperAdmin,
            'is_active' => true,
        ]);

        $plan = Plan::create([
            'code' => 'owner-access-ui',
            'name' => 'Owner Access UI',
            'is_active' => true,
        ]);

        $business = Business::create([
            'plan_id' => $plan->id,
            'name' => 'Grill Restaurant',
            'requested_subdomain' => 'grill-ui',
            'contact_name' => 'Owner',
            'phone' => '+93700000011',
            'status' => BusinessStatus::Provisioning,
            'provisioning_state' => ProvisioningState::Pending,
        ]);

        $this->actingAs($admin)
            ->post('http://localhost/platform/restaurants/'.$business->id.'/provision');

        $business->refresh();
        $tenant = Tenant::findOrFail($business->tenant_id);
        $this->tenantDatabases[] = $tenant->database()->getName();

        $this->actingAs($admin)
            ->get('http://localhost/platform/restaurants/'.$business->id)
            ->assertOk()
            ->assertSee('Tenant Owner Access')
            ->assertSee('Owner username / email')
            ->assertSee('New temporary password')
            ->assertSee('Confirm temporary password')
            ->assertSee('Save Owner Credentials');
    }
}
