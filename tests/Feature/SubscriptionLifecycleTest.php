<?php

namespace Tests\Feature;

use App\Enums\BillingCycle;
use App\Enums\BusinessStatus;
use App\Enums\PlatformRole;
use App\Enums\ProvisioningState;
use App\Enums\SubscriptionStatus;
use App\Models\AdminUser;
use App\Models\Business;
use App\Models\Plan;
use App\Models\PlanPrice;
use App\Models\Tenant;
use App\Services\Platform\SubscriptionService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Carbon;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Route;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;
use Stancl\Tenancy\Middleware\InitializeTenancyByDomain;
use Stancl\Tenancy\Middleware\PreventAccessFromCentralDomains;
use Tests\TestCase;

class SubscriptionLifecycleTest extends TestCase
{
    use RefreshDatabase;

    /** @var array<int, string> */
    private array $tenantDatabases = [];

    protected function tearDown(): void
    {
        Carbon::setTestNow();

        if (tenancy()->initialized) {
            tenancy()->end();
        }

        foreach ($this->tenantDatabases as $database) {
            @unlink(database_path($database));
        }

        parent::tearDown();
    }

    public function test_trial_requires_successful_provisioning(): void
    {
        $plan = $this->createPlan();
        $business = Business::create([
            'plan_id' => $plan->id,
            'name' => 'Pending Restaurant',
            'contact_name' => 'Owner',
            'phone' => '+93700000010',
            'status' => BusinessStatus::Provisioning,
            'provisioning_state' => ProvisioningState::Pending,
        ]);

        $this->expectException(ValidationException::class);

        app(SubscriptionService::class)->startTrial($business);
    }

    public function test_trial_starts_for_exactly_configured_days_and_only_once(): void
    {
        Carbon::setTestNow(Carbon::parse('2026-10-01 09:00:00', 'Asia/Kabul'));
        [$business] = $this->createProvisionedBusiness();

        $service = app(SubscriptionService::class);
        $trial = $service->startTrial($business);

        $this->assertSame('2026-10-01 09:00:00', $trial->starts_at->format('Y-m-d H:i:s'));
        $this->assertSame('2026-10-08 09:00:00', $trial->ends_at->format('Y-m-d H:i:s'));
        $this->assertSame(SubscriptionStatus::Trial, $trial->status);
        $this->assertSame(BusinessStatus::Trial, $business->fresh()->status);
        $this->assertTrue($service->access($business->fresh())->allowed);

        try {
            $service->startTrial($business->fresh());
            $this->fail('A second hosted trial should not be allowed.');
        } catch (ValidationException $exception) {
            $this->assertArrayHasKey('trial', $exception->errors());
        }
    }

    public function test_early_renewal_stacks_after_existing_future_expiry(): void
    {
        Carbon::setTestNow(Carbon::parse('2026-10-01 09:00:00', 'Asia/Kabul'));
        [$business, $plan, $price] = $this->createProvisionedBusiness(withPrice: true);

        $service = app(SubscriptionService::class);
        $trial = $service->startTrial($business);

        $admin = AdminUser::factory()->create();
        $renewal = $service->renew($business->fresh(), $price, $admin);

        $this->assertSame($trial->ends_at->toDateTimeString(), $renewal->starts_at->toDateTimeString());
        $this->assertSame('2026-11-08 09:00:00', $renewal->ends_at->format('Y-m-d H:i:s'));
        $this->assertSame(SubscriptionStatus::Scheduled, $renewal->status);
        $this->assertSame(BusinessStatus::Trial, $business->fresh()->status);
        $this->assertSame($renewal->ends_at->toDateTimeString(), $business->fresh()->subscription_ends_at->toDateTimeString());
        $this->assertSame($plan->code, $renewal->plan_code_snapshot);
    }

    public function test_late_renewal_starts_from_current_server_time(): void
    {
        Carbon::setTestNow(Carbon::parse('2026-10-01 09:00:00', 'Asia/Kabul'));
        [$business, , $price] = $this->createProvisionedBusiness(withPrice: true);

        $service = app(SubscriptionService::class);
        $service->startTrial($business);

        Carbon::setTestNow(Carbon::parse('2026-10-10 14:30:00', 'Asia/Kabul'));

        $renewal = $service->renew(
            $business->fresh(),
            $price,
            AdminUser::factory()->create(),
        );

        $this->assertSame('2026-10-10 14:30:00', $renewal->starts_at->format('Y-m-d H:i:s'));
        $this->assertSame('2026-11-10 14:30:00', $renewal->ends_at->format('Y-m-d H:i:s'));
        $this->assertSame(SubscriptionStatus::Active, $renewal->status);
        $this->assertSame(BusinessStatus::Active, $business->fresh()->status);
    }

    public function test_expired_subscription_locks_protected_tenant_access_but_keeps_recovery_endpoints_open(): void
    {
        Carbon::setTestNow(Carbon::parse('2026-10-01 09:00:00', 'Asia/Kabul'));
        [$business, , , $domain] = $this->createProvisionedBusiness();

        app(SubscriptionService::class)->startTrial($business);

        Carbon::setTestNow(Carbon::parse('2026-10-09 09:00:00', 'Asia/Kabul'));

        $this->getJson("http://{$domain}/")
            ->assertStatus(423)
            ->assertJsonPath('code', 'subscription_expired');

        $this->getJson("http://{$domain}/api/v1/health")
            ->assertOk()
            ->assertJsonPath('status', 'ok');

        $this->getJson("http://{$domain}/api/v1/subscription")
            ->assertOk()
            ->assertJsonPath('access_allowed', false)
            ->assertJsonPath('status', 'expired');
    }

    public function test_feature_enforcement_uses_subscription_snapshot_not_later_plan_edit(): void
    {
        [$business, $plan, , $domain] = $this->createProvisionedBusiness([
            'inventory' => 'false',
        ]);

        app(SubscriptionService::class)->startTrial($business);

        $plan->features()->where('feature_key', 'inventory')->update([
            'value' => json_encode(['value' => 'true']),
        ]);

        Route::domain($domain)
            ->middleware([
                InitializeTenancyByDomain::class,
                PreventAccessFromCentralDomains::class,
                'subscription.active',
                'plan.feature:inventory',
            ])
            ->get('/feature-probe', fn () => response()->json(['ok' => true]));

        $this->getJson("http://{$domain}/feature-probe")
            ->assertForbidden()
            ->assertJsonPath('code', 'feature_not_available');

        $this->assertFalse(app(SubscriptionService::class)->featureEnabled($business->fresh(), 'inventory'));
    }

    public function test_cancelling_access_does_not_delete_tenant_database_or_operational_rows(): void
    {
        [$business, , , , $tenant] = $this->createProvisionedBusiness();

        app(SubscriptionService::class)->startTrial($business);

        tenancy()->initialize($tenant);
        DB::table('users')->insert([
            'public_id' => (string) Str::ulid(),
            'name' => 'Protected Waiter',
            'email' => 'protected@example.test',
            'password' => 'hashed',
            'is_active' => true,
            'created_at' => now(),
            'updated_at' => now(),
        ]);
        tenancy()->end();

        $databasePath = database_path($tenant->database()->getName());

        app(SubscriptionService::class)->cancelAccess(
            $business->fresh(),
            AdminUser::factory()->create(),
        );

        $this->assertFileExists($databasePath);
        $this->assertSame(BusinessStatus::Cancelled, $business->fresh()->status);
        $this->assertSame('subscription_cancelled', app(SubscriptionService::class)->access($business->fresh())->code);

        tenancy()->initialize($tenant);
        $this->assertTrue(DB::table('users')->where('email', 'protected@example.test')->exists());
        tenancy()->end();
    }

    public function test_refresh_command_expires_stale_records_using_server_time(): void
    {
        Carbon::setTestNow(Carbon::parse('2026-10-01 09:00:00', 'Asia/Kabul'));
        [$business] = $this->createProvisionedBusiness();

        $trial = app(SubscriptionService::class)->startTrial($business);

        Carbon::setTestNow(Carbon::parse('2026-10-09 09:00:00', 'Asia/Kabul'));

        $this->artisan('subscriptions:refresh')->assertSuccessful();

        $this->assertSame(BusinessStatus::Expired, $business->fresh()->status);
        $this->assertSame(SubscriptionStatus::Expired, $trial->fresh()->status);
    }

    public function test_custom_billing_uses_operator_supplied_duration(): void
    {
        Carbon::setTestNow(Carbon::parse('2026-10-01 09:00:00', 'Asia/Kabul'));
        [$business, $plan] = $this->createProvisionedBusiness();

        $price = PlanPrice::create([
            'plan_id' => $plan->id,
            'billing_cycle' => BillingCycle::Custom,
            'interval_months' => null,
            'price' => 2500,
            'currency' => 'AFN',
            'is_active' => true,
        ]);

        $renewal = app(SubscriptionService::class)->renew(
            $business,
            $price,
            AdminUser::factory()->create(),
            45,
        );

        $this->assertSame('2026-10-01 09:00:00', $renewal->starts_at->format('Y-m-d H:i:s'));
        $this->assertSame('2026-11-15 09:00:00', $renewal->ends_at->format('Y-m-d H:i:s'));
        $this->assertSame(45, $renewal->custom_days);
    }

    public function test_platform_operator_can_create_price_start_trial_and_record_renewal(): void
    {
        [$business, $plan] = $this->createProvisionedBusiness();

        $operator = AdminUser::factory()->create([
            'role' => PlatformRole::Operator,
            'is_active' => true,
        ]);

        $this->actingAs($operator)
            ->post('http://localhost/platform/plans/'.$plan->id.'/prices', [
                'billing_cycle' => BillingCycle::Monthly->value,
                'price' => '1500.00',
                'currency' => 'AFN',
                'is_active' => '1',
                'sort_order' => 0,
            ])
            ->assertRedirect();

        $price = PlanPrice::where('plan_id', $plan->id)->sole();

        $this->actingAs($operator)
            ->post('http://localhost/platform/restaurants/'.$business->id.'/trial/start')
            ->assertRedirect();

        $this->actingAs($operator)
            ->post('http://localhost/platform/restaurants/'.$business->id.'/subscription/renew', [
                'plan_price_id' => $price->id,
            ])
            ->assertRedirect();

        $this->assertSame(2, $business->subscriptions()->count());
        $this->assertSame(2, $business->subscriptionEvents()->count());
    }

    /**
     * @param  array<string, string>  $features
     * @return array{Business, Plan, ?PlanPrice, string, Tenant}
     */
    private function createProvisionedBusiness(
        array $features = ['inventory' => 'true'],
        bool $withPrice = false,
    ): array {
        $plan = $this->createPlan($features);

        $price = $withPrice
            ? PlanPrice::create([
                'plan_id' => $plan->id,
                'billing_cycle' => BillingCycle::Monthly,
                'interval_months' => 1,
                'price' => 1200,
                'currency' => 'AFN',
                'is_active' => true,
            ])
            : null;

        $tenantId = 'subscription-'.Str::lower(Str::random(10));
        $database = config('tenancy.database.prefix').$tenantId.config('tenancy.database.suffix');

        @unlink(database_path($database));

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
            'name' => 'Subscription Test Restaurant',
            'contact_name' => 'Owner',
            'phone' => '+93700000011',
            'status' => BusinessStatus::Provisioning,
            'provisioning_state' => ProvisioningState::Ready,
        ]);

        return [$business, $plan, $price, $domain, $tenant];
    }

    /**
     * @param  array<string, string>  $features
     */
    private function createPlan(array $features = ['inventory' => 'true']): Plan
    {
        $plan = Plan::create([
            'code' => 'plan-'.Str::lower(Str::random(8)),
            'name' => 'Subscription Plan',
            'is_active' => true,
        ]);

        foreach ($features as $key => $value) {
            $plan->features()->create([
                'feature_key' => $key,
                'value' => ['value' => $value],
            ]);
        }

        return $plan;
    }
}
