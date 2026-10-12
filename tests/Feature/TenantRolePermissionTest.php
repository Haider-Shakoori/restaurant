<?php

namespace Tests\Feature;

use App\Enums\BusinessStatus;
use App\Enums\ProvisioningState;
use App\Models\Business;
use App\Models\Plan;
use App\Models\Tenant;
use App\Models\TenantUser;
use App\Services\Platform\SubscriptionService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Str;
use Illuminate\Support\Facades\Route;
use Tests\TestCase;

/**
 * Batch 4 regression guards: tenant role catalog, permission matrix
 * enforcement, user role assignment guards and tenant isolation.
 */
class TenantRolePermissionTest extends TestCase
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

    public function test_owner_can_create_edit_and_delete_custom_roles_with_permissions(): void
    {
        [$tenant, $domain] = $this->createActiveTenant();
        $ownerToken = $this->tokenFor($tenant, 'roles-owner@example.test', 'owner');

        $base = "http://{$domain}/api/v1/desktop/roles";

        $listing = $this->withToken($ownerToken)->getJson($base)
            ->assertOk()
            ->assertJsonStructure(['data', 'catalog' => ['groups', 'protected']])
            ->json('data');
        $this->assertNotEmpty(array_filter($listing, fn (array $role) => $role['key'] === 'owner'));
        $this->assertNotEmpty(array_filter($listing, fn (array $role) => $role['key'] === 'manager'));

        $created = $this->freshGuard()->withToken($ownerToken)->postJson($base, [
            'name' => 'Head Cashier',
            'description' => 'Front till supervision',
            'permissions' => ['pos.view', 'orders.payments'],
        ])->assertCreated()->json('data');

        $this->assertSame('head_cashier', $created['key']);
        $this->assertFalse($created['is_system']);
        $this->assertSame(2, $created['permission_count']);

        $updated = $this->freshGuard()->withToken($ownerToken)->patchJson($base.'/'.$created['id'], [
            'name' => 'Senior Cashier',
            'permissions' => ['pos.view', 'orders.payments', 'orders.discount'],
        ])->assertOk()->json('data');

        $this->assertSame('Senior Cashier', $updated['name']);
        $this->assertContains('orders.discount', $updated['permissions']);

        $reduced = $this->freshGuard()->withToken($ownerToken)->patchJson($base.'/'.$created['id'], [
            'permissions' => ['pos.view'],
        ])->assertOk()->json('data');

        $this->assertSame(['pos.view'], $reduced['permissions']);
        $this->assertSame(1, $reduced['permission_count']);

        $this->freshGuard()->withToken($ownerToken)
            ->deleteJson($base.'/'.$created['id'])
            ->assertOk();

        $remaining = $this->freshGuard()->withToken($ownerToken)->getJson($base)->assertOk()->json('data');
        $this->assertEmpty(array_filter($remaining, fn (array $role) => $role['key'] === 'head_cashier'));
    }

    public function test_role_assignment_is_counted_and_deletion_is_blocked_until_users_are_reassigned(): void
    {
        [$tenant, $domain] = $this->createActiveTenant();
        $ownerToken = $this->tokenFor($tenant, 'safety-owner@example.test', 'owner');

        $roles = "http://{$domain}/api/v1/desktop/roles";
        $users = "http://{$domain}/api/v1/desktop/users";

        $custom = $this->withToken($ownerToken)->postJson($roles, ['name' => 'Floor Lead'])
            ->assertCreated()->json('data');

        $created = $this->freshGuard()->withToken($ownerToken)->postJson($users, [
            'name' => 'Floor Operator',
            'email' => 'floor-lead@example.test',
            'role' => $custom['key'],
            'password' => 'Password123',
        ])->assertCreated()->json('data');

        $this->assertSame($custom['key'], $created['role']);
        $this->assertSame('Floor Lead', $created['role_name']);

        $listing = $this->freshGuard()->withToken($ownerToken)->getJson($roles)->assertOk()->json('data');
        $floorLead = collect($listing)->firstWhere('key', $custom['key']);
        $this->assertNotNull($floorLead);
        $this->assertSame(1, $floorLead['user_count']);

        // Deleting a role that still has users is refused.
        $this->freshGuard()->withToken($ownerToken)
            ->deleteJson($roles.'/'.$custom['id'])
            ->assertUnprocessable();

        // Unknown role keys are refused on user creation.
        $this->freshGuard()->withToken($ownerToken)->postJson($users, [
            'name' => 'Ghost',
            'email' => 'ghost@example.test',
            'role' => 'does_not_exist',
            'password' => 'Password123',
        ])->assertUnprocessable();

        // Reassign the user, then the custom role can be deleted.
        $this->freshGuard()->withToken($ownerToken)->patchJson($users.'/'.$created['id'], [
            'name' => 'Floor Operator',
            'email' => 'floor-lead@example.test',
            'phone' => null,
            'role' => 'waiter',
            'is_active' => true,
        ])->assertOk()->assertJsonPath('data.role', 'waiter');

        $this->freshGuard()->withToken($ownerToken)
            ->deleteJson($roles.'/'.$custom['id'])
            ->assertOk();
    }

    public function test_system_roles_are_protected_and_permissions_cannot_be_escalated(): void
    {
        [$tenant, $domain] = $this->createActiveTenant();
        $ownerToken = $this->tokenFor($tenant, 'protect-owner@example.test', 'owner');
        $adminToken = $this->tokenFor($tenant, 'protect-admin@example.test', 'admin');

        $roles = "http://{$domain}/api/v1/desktop/roles";
        $listing = $this->withToken($ownerToken)->getJson($roles)->assertOk()->json('data');
        $ownerRole = collect($listing)->firstWhere('key', 'owner');
        $adminRole = collect($listing)->firstWhere('key', 'admin');

        // Super Admin role cannot be edited or deleted.
        $this->freshGuard()->withToken($ownerToken)->patchJson($roles.'/'.$ownerRole['id'], [
            'name' => 'Renamed Owner',
        ])->assertUnprocessable();

        $this->freshGuard()->withToken($ownerToken)
            ->deleteJson($roles.'/'.$ownerRole['id'])
            ->assertUnprocessable();

        // Administrators cannot change administrator permissions.
        $this->freshGuard()->withToken($adminToken)->patchJson($roles.'/'.$adminRole['id'], [
            'permissions' => ['pos.view'],
        ])->assertUnprocessable();

        // Administrators cannot grant protected permissions to a custom role.
        $this->freshGuard()->withToken($adminToken)->postJson($roles, [
            'name' => 'Privileged Custom',
            'permissions' => ['roles.manage'],
        ])->assertUnprocessable();

        // Administrators cannot grant a permission their own role no longer holds.
        $trimmed = array_values(array_diff($adminRole['permissions'], ['reports.financial']));
        $this->freshGuard()->withToken($ownerToken)->patchJson($roles.'/'.$adminRole['id'], [
            'permissions' => $trimmed,
        ])->assertOk();

        $this->freshGuard()->withToken($adminToken)->postJson($roles, [
            'name' => 'Escalated Role',
            'permissions' => ['reports.financial'],
        ])->assertUnprocessable();

        // The owner may grant the same permission.
        $this->freshGuard()->withToken($ownerToken)->postJson($roles, [
            'name' => 'Owner Trusted Role',
            'permissions' => ['reports.financial'],
        ])->assertCreated();

        // System roles other than Super Admin can never be deleted.
        $managerRole = collect($listing)->firstWhere('key', 'manager');
        $this->freshGuard()->withToken($ownerToken)
            ->deleteJson($roles.'/'.$managerRole['id'])
            ->assertUnprocessable();
    }

    public function test_role_management_is_forbidden_for_unauthorized_roles_and_isolated_per_tenant(): void
    {
        [$tenantA, $domainA] = $this->createActiveTenant();
        [$tenantB, $domainB] = $this->createActiveTenant();

        $ownerA = $this->tokenFor($tenantA, 'isolation-owner-a@example.test', 'owner');
        $waiterA = $this->tokenFor($tenantA, 'isolation-waiter-a@example.test', 'waiter');
        $ownerB = $this->tokenFor($tenantB, 'isolation-owner-b@example.test', 'owner');

        $rolesA = "http://{$domainA}/api/v1/desktop/roles";

        // Waiters cannot read or manage roles.
        $this->withToken($waiterA)->getJson($rolesA)->assertForbidden();
        $this->freshGuard()->withToken($waiterA)->postJson($rolesA, ['name' => 'Waiter Made Role'])->assertForbidden();
        $this->freshGuard()->withToken($waiterA)
            ->postJson("http://{$domainA}/api/v1/desktop/users", [
                'name' => 'No', 'email' => 'no@example.test', 'role' => 'waiter', 'password' => 'Password123',
            ])->assertForbidden();

        $created = $this->freshGuard()->withToken($ownerA)->postJson($rolesA, [
            'name' => 'Tenant A Exclusive',
            'permissions' => ['pos.view'],
        ])->assertCreated()->json('data');

        // Tenant B never sees Tenant A roles, even when ids match.
        $listingB = $this->freshGuard()->withToken($ownerB)
            ->getJson("http://{$domainB}/api/v1/desktop/roles")
            ->assertOk()->json('data');
        $this->assertEmpty(array_filter($listingB, fn (array $role) => $role['key'] === $created['key']));

        // Tenant B cannot address a role id it does not own.
        $this->freshGuard()->withToken($ownerB)
            ->patchJson("http://{$domainB}/api/v1/desktop/roles/999999", ['name' => 'Hijacked'])
            ->assertNotFound();

        // Tenant B cannot assign Tenant A's private role key to its own users.
        $this->freshGuard()->withToken($ownerB)
            ->postJson("http://{$domainB}/api/v1/desktop/users", [
                'name' => 'Cross Tenant',
                'email' => 'cross-tenant-b@example.test',
                'role' => $created['key'],
                'password' => 'Password123',
            ])->assertUnprocessable();

        // Tenant A still owns the role untouched.
        $listingA = $this->freshGuard()->withToken($ownerA)->getJson($rolesA)->assertOk()->json('data');
        $stillThere = collect($listingA)->firstWhere('key', $created['key']);
        $this->assertNotNull($stillThere);
        $this->assertSame('Tenant A Exclusive', $stillThere['name']);
    }

    public function test_desktop_role_routes_are_registered_with_authorization_middleware(): void
    {
        $base = 'api/'.config('restaurant.api.version', 'v1').'/desktop';
        $routes = collect(Route::getRoutes()->getRoutes());

        $expected = [
            ['GET', $base.'/roles', 'tenant.permission:roles.manage'],
            ['POST', $base.'/roles', 'tenant.permission:roles.manage'],
            ['GET', $base.'/roles/{role}', 'tenant.permission:roles.manage'],
            ['PATCH', $base.'/roles/{role}', 'tenant.permission:roles.manage'],
            ['DELETE', $base.'/roles/{role}', 'tenant.permission:roles.manage'],
            ['GET', $base.'/permissions', 'tenant.permission:roles.manage'],
            ['GET', $base.'/users', 'tenant.permission:users.view'],
            ['POST', $base.'/users', 'tenant.permission:users.create'],
            ['PATCH', $base.'/users/{user}', 'tenant.permission:users.edit'],
        ];

        foreach ($expected as [$method, $path, $permission]) {
            $route = $routes->first(
                fn ($route) => $route->uri() === $path && in_array($method, $route->methods(), true)
            );

            $this->assertNotNull($route, "Missing {$method} {$path} in deployed tenant API contract");
            $middleware = $route->gatherMiddleware();
            $this->assertContains('auth:sanctum', $middleware);
            $this->assertContains('subscription.active', $middleware);
            $this->assertContains('tenant.role:owner,admin', $middleware);
            $this->assertContains($permission, $middleware);
        }
    }

    /**
     * @return array{Tenant, string}
     */
    private function createActiveTenant(): array
    {
        $plan = Plan::create([
            'code' => 'roles-'.Str::lower(Str::random(6)),
            'name' => 'Basic',
            'is_active' => true,
        ]);

        $tenantId = 'roles-'.Str::lower(Str::random(8));
        $tenant = Tenant::create([
            'id' => $tenantId,
            'provisioning_state' => ProvisioningState::Ready->value,
        ]);

        $domain = $tenantId.'.test';
        $tenant->domains()->create(['domain' => $domain]);
        $this->tenantDatabases[] = $tenant->database()->getName();

        $business = Business::create([
            'tenant_id' => $tenant->id,
            'plan_id' => $plan->id,
            'name' => 'Roles Restaurant',
            'requested_subdomain' => $tenantId,
            'contact_name' => 'Owner',
            'phone' => '+93700000000',
            'email' => 'owner@example.test',
            'status' => BusinessStatus::Provisioning,
            'provisioning_state' => ProvisioningState::Ready,
        ]);

        app(SubscriptionService::class)->startTrial($business);

        return [$tenant, $domain];
    }

    private function tokenFor(Tenant $tenant, string $email, string $role): string
    {
        tenancy()->initialize($tenant);

        $user = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => Str::headline($role).' User',
            'email' => $email,
            'password' => 'Password123',
            'role' => $role,
            'is_active' => true,
        ]);

        $token = $user->createToken('roles-test')->plainTextToken;
        tenancy()->end();

        return $token;
    }

    private function freshGuard(): static
    {
        $this->app['auth']->forgetGuards();

        return $this;
    }
}
