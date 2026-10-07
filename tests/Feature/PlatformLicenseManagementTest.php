<?php

namespace Tests\Feature;

use App\Enums\BusinessStatus;
use App\Enums\LicenseStatus;
use App\Enums\PlatformRole;
use App\Enums\ProvisioningState;
use App\Models\AdminUser;
use App\Models\Business;
use App\Models\Plan;
use App\Models\Tenant;
use App\Services\Platform\LicenseService;
use App\Services\Platform\SubscriptionService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Str;
use Tests\TestCase;

class PlatformLicenseManagementTest extends TestCase
{
    use RefreshDatabase;

    public function test_platform_admin_can_open_license_management_and_regenerate_tenant_license(): void
    {
        $admin = AdminUser::factory()->create([
            'role' => PlatformRole::SuperAdmin,
            'is_active' => true,
        ]);

        $business = $this->activeBusiness();
        $first = app(LicenseService::class)->generate($business, $admin);

        $this->actingAs($admin)
            ->get('http://localhost/platform/licenses')
            ->assertOk()
            ->assertSee('License Management')
            ->assertSee($business->name)
            ->assertSee('Regenerate license');

        $this->actingAs($admin)
            ->from('http://localhost/platform/licenses')
            ->post('http://localhost/platform/restaurants/'.$business->id.'/license/generate')
            ->assertRedirect('http://localhost/platform/licenses')
            ->assertSessionHas('generated_license_key');

        $licenses = $business->fresh()->licenseKeys()->get();

        $this->assertCount(2, $licenses);
        $this->assertSame(LicenseStatus::Active, $licenses->first()->status);
        $this->assertSame(2, $licenses->first()->version);
        $this->assertSame(LicenseStatus::Revoked, $first['license']->fresh()->status);
    }

    public function test_support_can_view_license_management_but_cannot_regenerate(): void
    {
        $support = AdminUser::factory()->create([
            'role' => PlatformRole::Support,
            'is_active' => true,
        ]);

        $business = $this->activeBusiness();

        $this->actingAs($support)
            ->get('http://localhost/platform/licenses')
            ->assertOk()
            ->assertSee($business->name)
            ->assertDontSee('Generate license');

        $this->actingAs($support)
            ->post('http://localhost/platform/restaurants/'.$business->id.'/license/generate')
            ->assertForbidden();
    }

    private function activeBusiness(): Business
    {
        $plan = Plan::create([
            'code' => 'license-management-'.Str::lower(Str::random(6)),
            'name' => 'License Management Plan',
            'is_active' => true,
        ]);

        $plan->features()->create([
            'feature_key' => 'max_devices',
            'value' => ['value' => '5'],
        ]);

        $tenant = Tenant::create([
            'id' => 'license-page-'.Str::lower(Str::random(8)),
            'provisioning_state' => ProvisioningState::Ready->value,
        ]);

        $tenant->domains()->create([
            'domain' => $tenant->id.'.test',
        ]);

        $business = Business::create([
            'tenant_id' => $tenant->id,
            'plan_id' => $plan->id,
            'name' => 'License Page Restaurant',
            'contact_name' => 'Owner',
            'phone' => '+93700000999',
            'status' => BusinessStatus::Provisioning,
            'provisioning_state' => ProvisioningState::Ready,
        ]);

        app(SubscriptionService::class)->startTrial($business);

        return $business->fresh();
    }
}
