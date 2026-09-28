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
use Tests\TestCase;

class TenantWebPortalTest extends TestCase
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

    public function test_tenant_root_redirects_guest_to_restaurant_login(): void
    {
        [, $domain] = $this->createActiveTenant();

        $this->get("http://{$domain}/")
            ->assertRedirect('/login');

        $this->get("http://{$domain}/login")
            ->assertOk()
            ->assertSee('Sign in to your restaurant');
    }

    public function test_owner_can_login_and_open_dashboard(): void
    {
        [$tenant, $domain] = $this->createActiveTenant();

        tenancy()->initialize($tenant);

        TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Restaurant Owner',
            'email' => 'owner@example.test',
            'password' => 'OwnerPass123',
            'is_active' => true,
            'role' => 'owner',
        ]);

        tenancy()->end();

        $this->post("http://{$domain}/login", [
            'email' => 'owner@example.test',
            'password' => 'OwnerPass123',
        ])->assertRedirect('/dashboard');

        $this->get("http://{$domain}/dashboard")
            ->assertOk()
            ->assertSee('Dashboard')
            ->assertSee('Open orders')
            ->assertDontSee('tenant_id');
    }

    /**
     * @return array{Tenant, string}
     */
    private function createActiveTenant(): array
    {
        $plan = Plan::create([
            'code' => 'basic-'.Str::lower(Str::random(6)),
            'name' => 'Basic',
            'is_active' => true,
        ]);

        $tenantId = 'portal-'.Str::lower(Str::random(8));
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
            'name' => 'Portal Restaurant',
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
}
