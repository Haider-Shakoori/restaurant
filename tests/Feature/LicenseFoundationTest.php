<?php

namespace Tests\Feature;

use App\Enums\BusinessStatus;
use App\Enums\DeviceStatus;
use App\Enums\LicenseStatus;
use App\Enums\PlatformRole;
use App\Enums\ProvisioningState;
use App\Models\AdminUser;
use App\Models\Business;
use App\Models\OfflineLease;
use App\Models\Plan;
use App\Models\Tenant;
use App\Services\Platform\LicenseService;
use App\Services\Platform\LicenseSigningService;
use App\Services\Platform\SubscriptionService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Carbon;
use Illuminate\Support\Facades\Schema;
use Illuminate\Support\Str;
use Tests\TestCase;

class LicenseFoundationTest extends TestCase
{
    use RefreshDatabase;

    /** @var array<int, string> */
    private array $tenantDatabases = [];

    protected function setUp(): void
    {
        parent::setUp();

        $keypair = sodium_crypto_sign_keypair();

        config([
            'license.signing.private_key' => base64_encode(sodium_crypto_sign_secretkey($keypair)),
            'license.signing.public_key' => base64_encode(sodium_crypto_sign_publickey($keypair)),
            'license.signing.key_id' => 'test-ed25519-v1',
            'license.offline_grace_days' => 7,
            'license.default_max_devices' => 5,
        ]);
    }

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

    public function test_generated_license_is_high_entropy_formatted_and_only_hash_is_persisted(): void
    {
        [$business] = $this->createActiveBusiness();
        $admin = $this->operator();

        $result = app(LicenseService::class)->generate($business, $admin);

        $this->assertMatchesRegularExpression(
            '/^RST-[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{4}(?:-[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{4}){3}$/',
            $result['raw_key'],
        );
        $this->assertSame(LicenseStatus::Active, $result['license']->status);
        $this->assertNotSame($result['raw_key'], $result['license']->key_hash);
        $this->assertSame(substr($result['raw_key'], -4), $result['license']->key_last4);
        $this->assertStringContainsString('****', $result['license']->masked());
    }

    public function test_activation_returns_device_credential_and_verifiable_signed_offline_lease(): void
    {
        Carbon::setTestNow(Carbon::parse('2026-10-01 09:00:00', 'Asia/Kabul'));
        [$business, , $domain] = $this->createActiveBusiness(['max_devices' => '3']);
        $license = app(LicenseService::class)->generate($business, $this->operator());

        config(['license.offline_grace_days' => 30]);

        $response = $this->postJson("http://{$domain}/api/v1/license/activate", [
            'license_key' => $license['raw_key'],
            'device_uid' => 'windows-desktop-001',
            'device_name' => 'Cashier Terminal 1',
            'platform' => 'windows',
            'app_version' => '1.0.0',
        ])->assertCreated();

        $deviceSecret = $response->json('device_secret');
        $payload = $response->json('lease.payload');
        $signature = $response->json('lease.signature');

        $this->assertIsString($deviceSecret);
        $this->assertGreaterThan(30, strlen($deviceSecret));
        $this->assertSame($business->tenant_id, $payload['tenant_id']);
        $this->assertSame($business->id, $payload['business_id']);
        $this->assertSame(1, $payload['license_version']);
        $this->assertSame(3, $payload['device_limit']);
        $this->assertSame('test-ed25519-v1', $response->json('lease.key_id'));
        $this->assertTrue(app(LicenseSigningService::class)->verify($payload, $signature));

        $subscriptionEnd = app(SubscriptionService::class)->access($business->fresh())->endsAt;
        $this->assertTrue(Carbon::parse($payload['offline_valid_until'])->equalTo($subscriptionEnd));
        $this->assertSame(1, OfflineLease::count());
    }

    public function test_public_key_endpoint_exposes_only_verification_material(): void
    {
        [, , $domain] = $this->createActiveBusiness();

        $response = $this->getJson("http://{$domain}/api/v1/license/public-key")
            ->assertOk()
            ->assertJsonPath('algorithm', 'Ed25519')
            ->assertJsonPath('key_id', 'test-ed25519-v1');

        $this->assertSame(
            app(LicenseSigningService::class)->publicKeyEncoded(),
            $response->json('public_key'),
        );
        $this->assertArrayNotHasKey('private_key', $response->json());
    }

    public function test_lease_refresh_requires_device_secret_not_raw_license_key(): void
    {
        [$business, , $domain] = $this->createActiveBusiness();
        $license = app(LicenseService::class)->generate($business, $this->operator());

        $activation = $this->postJson("http://{$domain}/api/v1/license/activate", [
            'license_key' => $license['raw_key'],
            'device_uid' => 'android-device-002',
            'device_name' => 'Waiter Phone 2',
            'platform' => 'android',
        ])->assertCreated();

        $deviceId = $activation->json('device.id');
        $deviceSecret = $activation->json('device_secret');

        $this->withHeaders([
            'X-Device-Id' => $deviceId,
            'X-Device-Secret' => $deviceSecret,
        ])->postJson("http://{$domain}/api/v1/license/lease", [
            'app_version' => '1.0.1',
        ])->assertOk()->assertJsonPath('lease.algorithm', 'Ed25519');

        $this->withHeaders([
            'X-Device-Id' => $deviceId,
            'X-Device-Secret' => $license['raw_key'],
        ])->postJson("http://{$domain}/api/v1/license/lease")
            ->assertUnprocessable()
            ->assertJsonValidationErrors('device_secret');
    }

    public function test_device_limit_from_subscription_snapshot_is_enforced(): void
    {
        [$business, , $domain] = $this->createActiveBusiness(['max_devices' => '1']);
        $license = app(LicenseService::class)->generate($business, $this->operator());

        $this->postJson("http://{$domain}/api/v1/license/activate", [
            'license_key' => $license['raw_key'],
            'device_uid' => 'device-limit-1',
            'platform' => 'android',
        ])->assertCreated();

        $this->postJson("http://{$domain}/api/v1/license/activate", [
            'license_key' => $license['raw_key'],
            'device_uid' => 'device-limit-2',
            'platform' => 'android',
        ])->assertUnprocessable()
            ->assertJsonValidationErrors('device_uid');

        $this->assertSame(1, $business->fresh()->devices()->where('status', DeviceStatus::Active)->count());
    }

    public function test_waiter_mobile_limit_is_separate_from_desktop_activation_and_is_signed_into_lease(): void
    {
        [$business, , $domain] = $this->createActiveBusiness([
            'max_devices' => '5',
            'max_mobile_devices' => '1',
        ]);
        $license = app(LicenseService::class)->generate($business, $this->operator());

        $desktop = $this->postJson("http://{$domain}/api/v1/license/activate", [
            'license_key' => $license['raw_key'],
            'device_uid' => 'restaurant-desktop-001',
            'device_name' => 'Main Cashier',
            'platform' => 'windows',
        ])->assertCreated();

        $this->assertSame(1, $desktop->json('lease.payload.mobile_device_limit'));

        $mobile = $this->postJson("http://{$domain}/api/v1/license/activate", [
            'license_key' => $license['raw_key'],
            'device_uid' => 'waiter-mobile-001',
            'device_name' => 'Waiter Phone 1',
            'platform' => 'android',
        ])->assertCreated();

        $this->assertSame(1, $mobile->json('lease.payload.mobile_device_limit'));

        $this->postJson("http://{$domain}/api/v1/license/activate", [
            'license_key' => $license['raw_key'],
            'device_uid' => 'waiter-mobile-002',
            'device_name' => 'Waiter Phone 2',
            'platform' => 'ios',
        ])->assertUnprocessable()
            ->assertJsonValidationErrors('device_uid');

        $this->assertSame(
            1,
            $business->fresh()->devices()
                ->where('status', DeviceStatus::Active)
                ->whereIn('platform', ['android', 'ios'])
                ->count(),
        );
        $this->assertSame(
            1,
            $business->fresh()->devices()
                ->where('status', DeviceStatus::Active)
                ->where('platform', 'windows')
                ->count(),
        );
    }

    public function test_legacy_device_limit_labels_are_enforced_for_existing_subscription_snapshots(): void
    {
        [$business, , $domain] = $this->createActiveBusiness([
            'Max devices' => '3',
            'Max mobile devices' => '1',
        ]);
        $license = app(LicenseService::class)->generate($business, $this->operator());

        $desktop = $this->postJson("http://{$domain}/api/v1/license/activate", [
            'license_key' => $license['raw_key'],
            'device_uid' => 'legacy-desktop-001',
            'platform' => 'windows',
        ])->assertCreated();

        $this->assertSame(3, $desktop->json('lease.payload.device_limit'));
        $this->assertSame(1, $desktop->json('lease.payload.mobile_device_limit'));

        $this->postJson("http://{$domain}/api/v1/license/activate", [
            'license_key' => $license['raw_key'],
            'device_uid' => 'legacy-mobile-001',
            'platform' => 'android',
        ])->assertCreated();

        $this->postJson("http://{$domain}/api/v1/license/activate", [
            'license_key' => $license['raw_key'],
            'device_uid' => 'legacy-mobile-002',
            'platform' => 'ios',
        ])->assertUnprocessable()
            ->assertJsonValidationErrors('device_uid');
    }

    public function test_central_desktop_resolver_finds_tenant_from_license_key_without_returning_raw_key(): void
    {
        [$business, , $domain] = $this->createActiveBusiness();
        $license = app(LicenseService::class)->generate($business, $this->operator());

        $response = $this->postJson('http://localhost/api/v1/desktop/license/resolve', [
            'license_key' => $license['raw_key'],
        ])->assertOk()
            ->assertJsonPath('tenant_base_url', 'https://'.$domain)
            ->assertJsonPath('license.version', 1)
            ->assertJsonPath('license.last4', substr($license['raw_key'], -4));

        $this->assertArrayNotHasKey('raw_key', $response->json());
        $this->assertArrayNotHasKey('license_key', $response->json());
    }

    public function test_windows_license_binds_to_first_computer_but_can_reactivate_same_installation(): void
    {
        [$business, , $domain] = $this->createActiveBusiness(['max_devices' => '5']);
        $license = app(LicenseService::class)->generate($business, $this->operator());

        $first = $this->postJson("http://{$domain}/api/v1/license/activate", [
            'license_key' => $license['raw_key'],
            'device_uid' => 'restaurant-desktop-bound-001',
            'device_name' => 'Main Restaurant PC',
            'platform' => 'windows',
            'app_version' => '1.0.0',
        ])->assertCreated();

        $this->assertNotNull($business->fresh()->first_activated_at);

        $this->postJson("http://{$domain}/api/v1/license/activate", [
            'license_key' => $license['raw_key'],
            'device_uid' => 'restaurant-desktop-bound-002',
            'device_name' => 'Another Restaurant PC',
            'platform' => 'windows',
            'app_version' => '1.0.0',
        ])->assertUnprocessable()
            ->assertJsonValidationErrors('license_key');

        $samePc = $this->postJson("http://{$domain}/api/v1/license/activate", [
            'license_key' => $license['raw_key'],
            'device_uid' => 'restaurant-desktop-bound-001',
            'device_name' => 'Main Restaurant PC',
            'platform' => 'windows',
            'app_version' => '1.0.1',
        ])->assertCreated();

        $this->assertSame($first->json('device.id'), $samePc->json('device.id'));
        $this->assertSame(
            1,
            $business->fresh()->devices()
                ->where('status', DeviceStatus::Active)
                ->where('platform', 'windows')
                ->count(),
        );
    }

    public function test_license_rotation_revokes_old_license_and_device_credentials(): void
    {
        [$business, , $domain] = $this->createActiveBusiness();
        $service = app(LicenseService::class);
        $admin = $this->operator();

        $first = $service->generate($business, $admin);

        $activation = $this->postJson("http://{$domain}/api/v1/license/activate", [
            'license_key' => $first['raw_key'],
            'device_uid' => 'rotate-device',
            'platform' => 'android',
        ])->assertCreated();

        $deviceId = $activation->json('device.id');
        $oldSecret = $activation->json('device_secret');

        $second = $service->generate($business->fresh(), $admin);

        $this->assertSame(LicenseStatus::Revoked, $first['license']->fresh()->status);
        $this->assertSame(2, $second['license']->version);
        $this->assertSame(DeviceStatus::Revoked, $business->fresh()->devices()->firstOrFail()->status);

        $this->withHeaders([
            'X-Device-Id' => $deviceId,
            'X-Device-Secret' => $oldSecret,
        ])->postJson("http://{$domain}/api/v1/license/lease")
            ->assertUnprocessable();

        $reactivation = $this->postJson("http://{$domain}/api/v1/license/activate", [
            'license_key' => $second['raw_key'],
            'device_uid' => 'rotate-device',
            'platform' => 'android',
        ])->assertCreated();

        $this->assertNotSame($oldSecret, $reactivation->json('device_secret'));
        $this->assertSame(
            $second['license']->id,
            $business->fresh()->devices()->where('device_uid', 'rotate-device')->firstOrFail()->license_key_id,
        );
    }

    public function test_platform_device_revocation_blocks_future_lease_refresh_without_deleting_tenant_data(): void
    {
        [$business, , $domain, $tenant] = $this->createActiveBusiness();
        $service = app(LicenseService::class);
        $license = $service->generate($business, $this->operator());

        $activation = $this->postJson("http://{$domain}/api/v1/license/activate", [
            'license_key' => $license['raw_key'],
            'device_uid' => 'revoke-device',
            'platform' => 'android',
        ])->assertCreated();

        $device = $business->fresh()->devices()->firstOrFail();

        tenancy()->initialize($tenant);
        $this->assertTrue(Schema::hasTable('users'));
        tenancy()->end();

        $service->revokeDevice($device, $this->operator());

        $this->withHeaders([
            'X-Device-Id' => $activation->json('device.id'),
            'X-Device-Secret' => $activation->json('device_secret'),
        ])->postJson("http://{$domain}/api/v1/license/lease")
            ->assertUnprocessable();

        $this->assertFileExists(database_path($tenant->database()->getName()));
        $this->assertSame(DeviceStatus::Revoked, $device->fresh()->status);
    }

    public function test_platform_shows_generated_raw_license_once_and_support_is_read_only(): void
    {
        [$business] = $this->createActiveBusiness();
        $operator = $this->operator();

        $response = $this->actingAs($operator)
            ->post('http://localhost/platform/restaurants/'.$business->id.'/license/generate')
            ->assertRedirect()
            ->assertSessionHas('generated_license_key');

        $rawKey = $response->getSession()->get('generated_license_key');

        $this->assertIsString($rawKey);

        $this->actingAs($operator)
            ->get('http://localhost/platform/restaurants/'.$business->id.'/license')
            ->assertOk()
            ->assertSee($rawKey, false);

        $this->actingAs($operator)
            ->get('http://localhost/platform/restaurants/'.$business->id.'/license')
            ->assertOk()
            ->assertDontSee($rawKey, false);

        $support = AdminUser::factory()->create([
            'role' => PlatformRole::Support,
            'is_active' => true,
        ]);

        $this->actingAs($support)
            ->get('http://localhost/platform/restaurants/'.$business->id.'/license')
            ->assertOk();

        $this->actingAs($support)
            ->post('http://localhost/platform/restaurants/'.$business->id.'/license/generate')
            ->assertForbidden();
    }

    public function test_signing_key_command_generates_server_private_and_public_files_without_printing_private_key(): void
    {
        $base = storage_path('framework/testing/license-keys-'.Str::random(8));
        $private = $base.'/secret.key';
        $public = $base.'/public.key';

        config([
            'license.signing.private_key' => null,
            'license.signing.public_key' => null,
            'license.signing.private_key_path' => $private,
            'license.signing.public_key_path' => $public,
            'license.signing.key_id' => 'generated-test-key',
        ]);

        $this->artisan('licenses:generate-signing-keys')
            ->expectsOutputToContain('Ed25519 license signing keypair generated.')
            ->expectsOutputToContain('Public key:')
            ->doesntExpectOutputToContain('Private key:')
            ->assertSuccessful();

        $this->assertFileExists($private);
        $this->assertFileExists($public);
        $this->assertNotSame(file_get_contents($private), file_get_contents($public));

        @unlink($private);
        @unlink($public);
        @rmdir($base);
    }

    /**
     * @param  array<string, string>  $features
     * @return array{Business, Plan, string, Tenant}
     */
    private function createActiveBusiness(array $features = ['max_devices' => '5']): array
    {
        Carbon::setTestNow(Carbon::parse('2026-10-01 09:00:00', 'Asia/Kabul'));

        $plan = Plan::create([
            'code' => 'license-'.Str::lower(Str::random(8)),
            'name' => 'License Test Plan',
            'is_active' => true,
        ]);

        foreach ($features as $key => $value) {
            $plan->features()->create([
                'feature_key' => $key,
                'value' => ['value' => $value],
            ]);
        }

        $tenantId = 'license-'.Str::lower(Str::random(10));
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
            'name' => 'License Test Restaurant',
            'contact_name' => 'Owner',
            'phone' => '+93700000021',
            'status' => BusinessStatus::Provisioning,
            'provisioning_state' => ProvisioningState::Ready,
        ]);

        app(SubscriptionService::class)->startTrial($business);

        return [$business->fresh(), $plan, $domain, $tenant];
    }

    private function operator(): AdminUser
    {
        return AdminUser::factory()->create([
            'role' => PlatformRole::Operator,
            'is_active' => true,
        ]);
    }
}
