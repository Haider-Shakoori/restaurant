<?php

namespace Tests\Feature;

use App\Enums\BusinessStatus;
use App\Enums\OnboardingStep;
use App\Enums\ProvisioningState;
use App\Models\Branch;
use App\Models\Business;
use App\Models\OnboardingProgress;
use App\Models\Plan;
use App\Models\Restaurant;
use App\Models\Tenant;
use App\Models\TenantUser;
use App\Services\Platform\SubscriptionService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\File;
use Illuminate\Support\Facades\Schema;
use Illuminate\Support\Str;
use Tests\TestCase;

class RestaurantOnboardingBranchTest extends TestCase
{
    use RefreshDatabase;

    /** @var array<int, string> */
    private array $tenantDatabases = [];

    /** @var array<int, string> */
    private array $tenantStoragePaths = [];

    protected function tearDown(): void
    {
        if (tenancy()->initialized) {
            tenancy()->end();
        }

        foreach ($this->tenantDatabases as $database) {
            @unlink(database_path($database));
        }

        foreach ($this->tenantStoragePaths as $path) {
            File::deleteDirectory($path);
        }

        parent::tearDown();
    }

    public function test_batch_six_tables_exist_only_inside_tenant_database(): void
    {
        [, , , $tenant] = $this->createRestaurantTenant();

        $this->assertFalse(Schema::hasTable('restaurants'));
        $this->assertFalse(Schema::hasTable('branches'));
        $this->assertFalse(Schema::hasTable('onboarding_progress'));

        tenancy()->initialize($tenant);

        $this->assertTrue(Schema::hasTable('restaurants'));
        $this->assertTrue(Schema::hasTable('branches'));
        $this->assertTrue(Schema::hasTable('onboarding_progress'));
    }

    public function test_tenant_user_can_login_before_subscription_but_onboarding_stays_locked(): void
    {
        [, $domain] = $this->createRestaurantTenant(startTrial: false);

        $this->post("http://{$domain}/login", [
            'email' => 'owner@example.test',
            'password' => 'StrongPass123',
        ])->assertRedirect('/onboarding');

        $this->get("http://{$domain}/onboarding")
            ->assertStatus(423)
            ->assertSee('active trial or subscription', false);
    }

    public function test_restaurant_and_primary_branch_onboarding_is_resumable(): void
    {
        [, $domain, , $tenant] = $this->createRestaurantTenant();

        $this->login($domain);

        $this->put("http://{$domain}/onboarding/restaurant", [
            'name' => 'Kabul Family Restaurant',
            'phone' => '+93700000001',
            'email' => 'restaurant@example.test',
            'address' => 'Kabul',
            'country_code' => 'AF',
            'currency' => 'AFN',
            'timezone' => 'Asia/Kabul',
            'locale' => 'en',
        ])->assertRedirect();

        $this->put("http://{$domain}/onboarding/branch", [
            'code' => 'MAIN',
            'name' => 'Main Branch',
            'phone' => '+93700000002',
            'address' => 'Shahr-e-Naw, Kabul',
        ])->assertRedirect();

        tenancy()->initialize($tenant);

        $restaurant = Restaurant::where('profile_key', 'primary')->firstOrFail();
        $branch = Branch::where('restaurant_id', $restaurant->id)->sole();
        $progress = OnboardingProgress::where('singleton_key', 'primary')->firstOrFail();

        $this->assertSame('AFN', $restaurant->currency);
        $this->assertSame('Asia/Kabul', $restaurant->timezone);
        $this->assertTrue($branch->is_primary);
        $this->assertTrue($branch->is_active);
        $this->assertSame(OnboardingStep::Floors, $progress->current_step);
        $this->assertTrue($progress->hasCompleted(OnboardingStep::Restaurant));
        $this->assertTrue($progress->hasCompleted(OnboardingStep::Branch));

        tenancy()->end();

        $this->get("http://{$domain}/onboarding")
            ->assertOk()
            ->assertSee('Kabul Family Restaurant')
            ->assertSee('Main Branch')
            ->assertSee('Floors / Sections');
    }

    public function test_saved_dari_locale_applies_rtl_to_tenant_ui(): void
    {
        [, $domain] = $this->createRestaurantTenant();
        $this->login($domain);

        $this->put("http://{$domain}/onboarding/restaurant", [
            'name' => 'رستورانت کابل',
            'country_code' => 'AF',
            'currency' => 'AFN',
            'timezone' => 'Asia/Kabul',
            'locale' => 'fa',
        ])->assertRedirect();

        $this->get("http://{$domain}/onboarding")
            ->assertOk()
            ->assertSee('dir="rtl"', false)
            ->assertSee('lang="fa"', false);
    }

    public function test_plan_max_branches_limit_is_enforced_server_side(): void
    {
        [, $domain] = $this->createRestaurantTenant(['max_branches' => '1']);
        $this->login($domain);
        $this->completeBaseOnboarding($domain);

        $this->post("http://{$domain}/branches", [
            'code' => 'SECOND',
            'name' => 'Second Branch',
            'is_active' => '1',
            'is_primary' => '0',
        ])->assertSessionHasErrors('branch');

        $this->get("http://{$domain}/branches")
            ->assertOk()
            ->assertSee('Active branches: 1')->assertSee('1 allowed by plan');
    }

    public function test_primary_branch_cannot_be_deactivated_without_replacement(): void
    {
        [, $domain, , $tenant] = $this->createRestaurantTenant();
        $this->login($domain);
        $this->completeBaseOnboarding($domain);

        tenancy()->initialize($tenant);
        $branch = Branch::where('is_primary', true)->firstOrFail();
        tenancy()->end();

        $this->put("http://{$domain}/branches/{$branch->id}", [
            'code' => $branch->code,
            'name' => $branch->name,
            'is_primary' => '0',
            'is_active' => '0',
        ])->assertSessionHasErrors('is_primary');
    }

    public function test_branch_records_never_cross_tenant_database_boundary(): void
    {
        [, , , $tenantA] = $this->createRestaurantTenant();
        [, , , $tenantB] = $this->createRestaurantTenant();

        tenancy()->initialize($tenantA);
        $restaurantA = Restaurant::create([
            'profile_key' => 'primary',
            'name' => 'Restaurant A',
            'country_code' => 'AF',
            'currency' => 'AFN',
            'timezone' => 'Asia/Kabul',
            'locale' => 'en',
        ]);
        Branch::create([
            'restaurant_id' => $restaurantA->id,
            'code' => 'A',
            'name' => 'A Branch',
            'is_primary' => true,
            'is_active' => true,
        ]);
        tenancy()->end();

        tenancy()->initialize($tenantB);

        $this->assertSame(0, Restaurant::where('name', 'Restaurant A')->count());
        $this->assertSame(0, Branch::where('code', 'A')->count());
    }

    private function login(string $domain): void
    {
        $this->post("http://{$domain}/login", [
            'email' => 'owner@example.test',
            'password' => 'StrongPass123',
        ])->assertRedirect('/onboarding');
    }

    private function completeBaseOnboarding(string $domain): void
    {
        $this->put("http://{$domain}/onboarding/restaurant", [
            'name' => 'Test Restaurant',
            'country_code' => 'AF',
            'currency' => 'AFN',
            'timezone' => 'Asia/Kabul',
            'locale' => 'en',
        ])->assertRedirect();

        $this->put("http://{$domain}/onboarding/branch", [
            'code' => 'MAIN',
            'name' => 'Main Branch',
        ])->assertRedirect();
    }

    /**
     * @param  array<string, string>  $features
     * @return array{Business, string, Plan, Tenant}
     */
    private function createRestaurantTenant(
        array $features = ['max_branches' => '3'],
        bool $startTrial = true,
    ): array {
        $plan = Plan::create([
            'code' => 'onboarding-'.Str::lower(Str::random(8)),
            'name' => 'Onboarding Plan',
            'is_active' => true,
        ]);

        foreach ($features as $key => $value) {
            $plan->features()->create([
                'feature_key' => $key,
                'value' => ['value' => $value],
            ]);
        }

        $tenantId = 'onboarding-'.Str::lower(Str::random(10));
        $database = config('tenancy.database.prefix').$tenantId.config('tenancy.database.suffix');

        @unlink(database_path($database));

        $storagePath = storage_path(config('tenancy.filesystem.suffix_base').$tenantId);
        File::deleteDirectory($storagePath);
        $this->tenantStoragePaths[] = $storagePath;

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
            'name' => 'Onboarding Test Restaurant',
            'contact_name' => 'Owner',
            'phone' => '+93700000000',
            'status' => BusinessStatus::Provisioning,
            'provisioning_state' => ProvisioningState::Ready,
        ]);

        if ($startTrial) {
            app(SubscriptionService::class)->startTrial($business);
        }

        tenancy()->initialize($tenant);

        TenantUser::create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Restaurant Owner',
            'email' => 'owner@example.test',
            'password' => 'StrongPass123',
            'is_active' => true,
        ]);

        tenancy()->end();

        return [$business, $domain, $plan, $tenant];
    }
}
