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
use Illuminate\Support\Facades\Hash;
use Tests\TestCase;

class PlatformAdminTest extends TestCase
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

    public function test_guest_is_redirected_to_platform_login(): void
    {
        $this->get('http://localhost/platform')
            ->assertRedirect('/platform/login');
    }

    public function test_active_platform_admin_can_login(): void
    {
        $admin = AdminUser::factory()->create([
            'role' => PlatformRole::SuperAdmin,
            'is_active' => true,
            'password' => Hash::make('StrongPass123'),
        ]);

        $this->post('http://localhost/platform/login', [
            'email' => $admin->email,
            'password' => 'StrongPass123',
        ])->assertRedirect('/platform');

        $this->assertAuthenticatedAs($admin);
        $this->assertNotNull($admin->fresh()->last_login_at);
    }

    public function test_inactive_platform_admin_cannot_login(): void
    {
        $admin = AdminUser::factory()->create([
            'is_active' => false,
            'password' => Hash::make('StrongPass123'),
        ]);

        $this->post('http://localhost/platform/login', [
            'email' => $admin->email,
            'password' => 'StrongPass123',
        ])->assertSessionHasErrors('email');

        $this->assertGuest();
    }

    public function test_support_operator_is_read_only(): void
    {
        $support = AdminUser::factory()->create([
            'role' => PlatformRole::Support,
            'is_active' => true,
        ]);

        $this->actingAs($support)
            ->get('http://localhost/platform')
            ->assertOk();

        $this->actingAs($support)
            ->get('http://localhost/platform/plans')
            ->assertForbidden();
    }

    public function test_operator_creates_only_pending_commercial_record(): void
    {
        $operator = AdminUser::factory()->create([
            'role' => PlatformRole::Operator,
            'is_active' => true,
        ]);

        $this->actingAs($operator)
            ->post('http://localhost/platform/restaurants', [
                'name' => 'Kabul Grill',
                'requested_subdomain' => 'kabul-grill',
                'contact_name' => 'Owner',
                'phone' => '+93700000000',
                'location' => 'Kabul',
                'assigned_operator_id' => $operator->id,
            ])
            ->assertRedirect();

        $business = Business::sole();

        $this->assertSame(BusinessStatus::Provisioning, $business->status);
        $this->assertSame(ProvisioningState::Pending, $business->provisioning_state);
        $this->assertNull($business->tenant_id);
        $this->assertSame(0, Tenant::count());
        $this->assertDatabaseHas('provisioning_events', [
            'business_id' => $business->id,
            'event' => 'business.created',
            'state' => ProvisioningState::Pending->value,
        ]);
    }

    public function test_operator_can_create_configurable_plan_features(): void
    {
        $operator = AdminUser::factory()->create([
            'role' => PlatformRole::Operator,
            'is_active' => true,
        ]);

        $this->actingAs($operator)
            ->post('http://localhost/platform/plans', [
                'code' => 'starter',
                'name' => 'Starter',
                'description' => 'Entry plan',
                'is_active' => '1',
                'sort_order' => 10,
                'features' => [
                    ['key' => 'max_waiters', 'value' => '10'],
                    ['key' => 'inventory', 'value' => 'false'],
                ],
            ])
            ->assertRedirect('/platform/plans');

        $plan = Plan::where('code', 'starter')->firstOrFail();

        $this->assertTrue($plan->is_active);
        $this->assertDatabaseHas('plan_features', [
            'plan_id' => $plan->id,
            'feature_key' => 'max_waiters',
        ]);
    }

    public function test_super_admin_can_create_operator(): void
    {
        $admin = AdminUser::factory()->create([
            'role' => PlatformRole::SuperAdmin,
            'is_active' => true,
        ]);

        $this->actingAs($admin)
            ->post('http://localhost/platform/operators', [
                'name' => 'Support User',
                'email' => 'support@example.test',
                'role' => PlatformRole::Support->value,
                'password' => 'Support1234',
                'password_confirmation' => 'Support1234',
            ])
            ->assertRedirect('/platform/operators');

        $this->assertDatabaseHas('admin_users', [
            'email' => 'support@example.test',
            'role' => PlatformRole::Support->value,
            'is_active' => true,
        ]);
    }

    public function test_super_admin_can_render_control_plane_management_screens(): void
    {
        $admin = AdminUser::factory()->create([
            'role' => PlatformRole::SuperAdmin,
            'is_active' => true,
        ]);

        $business = Business::create([
            'name' => 'Render Test Restaurant',
            'contact_name' => 'Owner',
            'phone' => '+93700000002',
            'status' => BusinessStatus::Provisioning,
            'provisioning_state' => ProvisioningState::Pending,
        ]);

        foreach ([
            '/platform',
            '/platform/restaurants',
            '/platform/restaurants/create',
            '/platform/restaurants/'.$business->id,
            '/platform/tenants',
            '/platform/plans',
            '/platform/plans/create',
            '/platform/operators',
            '/platform/operators/create',
        ] as $path) {
            $this->actingAs($admin)->get('http://localhost'.$path)->assertOk();
        }
    }

    public function test_reserved_platform_subdomain_is_rejected(): void
    {
        $operator = AdminUser::factory()->create([
            'role' => PlatformRole::Operator,
            'is_active' => true,
        ]);

        $this->actingAs($operator)
            ->post('http://localhost/platform/restaurants', [
                'name' => 'Reserved Domain Restaurant',
                'requested_subdomain' => 'admin',
                'contact_name' => 'Owner',
                'phone' => '+93700000001',
            ])
            ->assertSessionHasErrors('requested_subdomain');

        $this->assertSame(0, Business::count());
    }

    public function test_tenant_domain_cannot_access_central_platform_routes(): void
    {
        $tenant = Tenant::create([
            'id' => 'restaurant-platform-test',
            'provisioning_state' => 'ready',
        ]);
        $tenant->domains()->create(['domain' => 'restaurant.test']);
        $this->tenantDatabases[] = $tenant->database()->getName();

        $this->get('http://restaurant.test/platform')
            ->assertNotFound();
    }
}
