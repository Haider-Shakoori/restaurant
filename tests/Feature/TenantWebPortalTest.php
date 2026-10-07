<?php

namespace Tests\Feature;

use App\Enums\BusinessStatus;
use App\Enums\DeviceStatus;
use App\Enums\LicenseStatus;
use App\Enums\ProvisioningState;
use App\Models\Business;
use App\Models\DeviceActivation;
use App\Models\LicenseKey;
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

    public function test_owner_can_revoke_waiter_mobile_from_restaurant_settings(): void
    {
        [$tenant, $domain] = $this->createActiveTenant();
        $business = Business::query()->where('tenant_id', $tenant->id)->firstOrFail();

        $license = LicenseKey::create([
            'business_id' => $business->id,
            'version' => 1,
            'key_hash' => hash('sha256', 'tenant-settings-test-license'),
            'key_prefix' => 'RST',
            'key_last4' => 'TEST',
            'status' => LicenseStatus::Active,
            'max_devices_snapshot' => 5,
            'generated_at' => now(),
        ]);

        $device = DeviceActivation::create([
            'business_id' => $business->id,
            'license_key_id' => $license->id,
            'device_uid' => 'tenant-settings-waiter-1',
            'device_name' => 'Waiter Tablet',
            'platform' => 'android',
            'credential_hash' => hash('sha256', 'secret'),
            'credential_last4' => 'cret',
            'status' => DeviceStatus::Active,
            'activated_at' => now(),
            'last_seen_at' => now(),
        ]);

        tenancy()->initialize($tenant);
        TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Restaurant Owner',
            'email' => 'owner-device@example.test',
            'password' => 'OwnerPass123',
            'is_active' => true,
            'role' => 'owner',
        ]);
        tenancy()->end();

        $this->post("http://{$domain}/login", [
            'email' => 'owner-device@example.test',
            'password' => 'OwnerPass123',
        ])->assertRedirect('/dashboard');

        $this->post("http://{$domain}/settings/devices/{$device->id}/revoke")
            ->assertRedirect();

        $this->assertSame(DeviceStatus::Revoked, $device->fresh()->status);
        $this->assertNotNull($device->fresh()->revoked_at);
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
