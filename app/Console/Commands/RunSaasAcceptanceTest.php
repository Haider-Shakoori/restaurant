<?php

namespace App\Console\Commands;

use App\Models\AdminUser;
use App\Models\Business;
use App\Models\OfflineLease;
use App\Models\Plan;
use App\Models\SyncDeviceState;
use App\Models\TenantUser;
use App\Services\Platform\LicenseService;
use App\Services\Platform\SubscriptionService;
use Illuminate\Console\Command;
use Illuminate\Http\Client\Response;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Str;
use RuntimeException;
use Throwable;

class RunSaasAcceptanceTest extends Command
{
    protected $signature = 'restaurant:saas-acceptance
        {business : Business ULID or exact business name}
        {--base-url= : Optional tenant base URL override}';

    protected $description = 'Run a guarded live SaaS licensing/pairing acceptance test and restore all original state';

    public function handle(
        LicenseService $licenses,
        SubscriptionService $subscriptions,
    ): int {
        $selector = trim((string) $this->argument('business'));
        $business = Business::query()
            ->whereKey($selector)
            ->orWhere('name', $selector)
            ->first();

        if (! $business) {
            $this->error('Business not found.');

            return self::FAILURE;
        }

        if ($business->licenseKeys()->exists() || $business->devices()->exists()) {
            $this->error('Safety guard: business already has license/device history; acceptance test refused.');

            return self::FAILURE;
        }

        $tenant = $business->tenant()->with('domains')->first();
        $domain = $tenant?->domains->first()?->domain;

        if (! $tenant || ! $domain) {
            $this->error('Safety guard: business must have a provisioned tenant domain.');

            return self::FAILURE;
        }

        $subscription = $business->subscriptions()->orderByDesc('starts_at')->first();

        if (! $subscription) {
            $this->error('Safety guard: business has no subscription record to restore.');

            return self::FAILURE;
        }

        $admin = AdminUser::query()
            ->where('is_active', true)
            ->whereIn('role', ['super_admin', 'operator'])
            ->first();

        if (! $admin) {
            $this->error('No active platform operator is available.');

            return self::FAILURE;
        }

        $baseUrl = rtrim(
            (string) ($this->option('base-url') ?: 'https://'.$domain),
            '/',
        );
        $tag = 'acceptance-'.Str::lower(Str::random(10));
        $email = $tag.'@restaurant.test';
        $password = Str::password(24);
        $temporaryPlan = null;
        $temporaryUser = null;

        $businessBackup = [
            'plan_id' => $business->plan_id,
            'subscription_ends_at' => $business->subscription_ends_at?->copy(),
            'status' => $business->status,
        ];
        $subscriptionBackup = [
            'plan_id' => $subscription->plan_id,
            'plan_code_snapshot' => $subscription->plan_code_snapshot,
            'plan_name_snapshot' => $subscription->plan_name_snapshot,
            'features_snapshot' => $subscription->features_snapshot,
            'ends_at' => $subscription->ends_at?->copy(),
            'status' => $subscription->status,
        ];

        try {
            $temporaryPlan = Plan::create([
                'code' => $tag,
                'name' => 'Live SaaS Acceptance',
                'description' => 'Temporary plan created by restaurant:saas-acceptance.',
                'is_active' => true,
                'sort_order' => 9999,
            ]);

            foreach (['max_devices' => '3', 'max_mobile_devices' => '2'] as $key => $value) {
                $temporaryPlan->features()->create([
                    'feature_key' => $key,
                    'value' => ['value' => $value],
                ]);
            }

            $features = $temporaryPlan->entitlementSnapshot();

            if (($features['max_devices'] ?? null) !== 3 || ($features['max_mobile_devices'] ?? null) !== 2) {
                throw new RuntimeException('Plan entitlement snapshot did not resolve the requested numeric limits.');
            }

            $testEndsAt = now()->addHour();

            $subscription->update([
                'plan_id' => $temporaryPlan->id,
                'plan_code_snapshot' => $temporaryPlan->code,
                'plan_name_snapshot' => $temporaryPlan->name,
                'features_snapshot' => $features,
                'ends_at' => $testEndsAt,
            ]);
            $business->update([
                'plan_id' => $temporaryPlan->id,
                'subscription_ends_at' => $testEndsAt,
            ]);

            $access = $subscriptions->access($business->fresh());

            if (! $access->allowed) {
                throw new RuntimeException('Temporary acceptance subscription did not become active.');
            }

            $generated = $licenses->generate($business->fresh(), $admin);
            $rawLicense = $generated['raw_key'];

            tenancy()->initialize($tenant);
            $temporaryUser = TenantUser::create([
                'public_id' => (string) Str::ulid(),
                'name' => 'SaaS Acceptance Owner',
                'email' => $email,
                'password' => $password,
                'is_active' => true,
                'role' => 'owner',
            ]);
            tenancy()->end();

            $desktop = $this->expectStatus(
                Http::acceptJson()->timeout(30)->post($baseUrl.'/api/v1/license/activate', [
                    'license_key' => $rawLicense,
                    'device_uid' => $tag.'-windows-1',
                    'device_name' => 'Acceptance Desktop',
                    'platform' => 'windows',
                    'app_version' => 'acceptance',
                ]),
                201,
                'Desktop activation',
            );

            if (
                $desktop->json('lease.payload.device_limit') !== 3
                || $desktop->json('lease.payload.mobile_device_limit') !== 2
            ) {
                throw new RuntimeException('Signed lease did not contain the expected device limits.');
            }

            $login = $this->expectStatus(
                Http::acceptJson()->timeout(30)->post($baseUrl.'/api/v1/auth/login', [
                    'email' => $email,
                    'password' => $password,
                    'device_name' => 'acceptance-runner',
                ]),
                200,
                'Tenant API login',
            );
            $token = (string) $login->json('access_token');

            if ($token === '') {
                throw new RuntimeException('Tenant API login did not return an access token.');
            }

            $mobileOne = $this->pairMobile(
                $baseUrl,
                $token,
                (string) $desktop->json('device.id'),
                (string) $desktop->json('device_secret'),
                $tag.'-android-1',
                'android',
            );
            $mobileTwo = $this->pairMobile(
                $baseUrl,
                $token,
                (string) $desktop->json('device.id'),
                (string) $desktop->json('device_secret'),
                $tag.'-ios-1',
                'ios',
            );

            $thirdToken = $this->pairingToken(
                $baseUrl,
                $token,
                (string) $desktop->json('device.id'),
                (string) $desktop->json('device_secret'),
            );
            $thirdMobile = Http::acceptJson()->timeout(30)->post(
                $baseUrl.'/api/v1/pairing-tokens/redeem',
                [
                    'pairing_token' => $thirdToken,
                    'device_uid' => $tag.'-android-over-limit',
                    'device_name' => 'Over-limit waiter',
                    'platform' => 'android',
                    'app_version' => 'acceptance',
                ],
            );

            if ($thirdMobile->status() !== 422 || ! data_get($thirdMobile->json(), 'errors.device_uid')) {
                throw new RuntimeException(
                    'Third mobile was not rejected by the mobile-device limit. HTTP '.$thirdMobile->status(),
                );
            }

            $mobileOneDevice = $business->fresh()->devices()->findOrFail($mobileOne['id']);
            $licenses->revokeMobileDeviceForTenant($mobileOneDevice, $temporaryUser);

            $replacement = $this->pairMobile(
                $baseUrl,
                $token,
                (string) $desktop->json('device.id'),
                (string) $desktop->json('device_secret'),
                $tag.'-android-replacement',
                'android',
            );

            $extraDesktop = Http::acceptJson()->timeout(30)->post($baseUrl.'/api/v1/license/activate', [
                'license_key' => $rawLicense,
                'device_uid' => $tag.'-windows-over-limit',
                'device_name' => 'Over-limit Desktop',
                'platform' => 'windows',
                'app_version' => 'acceptance',
            ]);

            if ($extraDesktop->status() !== 422 || ! data_get($extraDesktop->json(), 'errors.device_uid')) {
                throw new RuntimeException(
                    'Additional desktop was not rejected by the total-device limit. HTTP '.$extraDesktop->status(),
                );
            }

            $activeDevices = $business->fresh()->devices()->where('status', 'active');
            $activeMobiles = (clone $activeDevices)->whereIn('platform', ['android', 'ios'])->count();
            $activeWindows = (clone $activeDevices)->where('platform', 'windows')->count();

            if ($activeMobiles !== 2 || $activeWindows !== 1) {
                throw new RuntimeException(
                    "Unexpected active-device counts: mobiles={$activeMobiles}, windows={$activeWindows}.",
                );
            }

            $this->info('PASS: plan entitlements resolved as max_devices=3 and max_mobile_devices=2.');
            $this->info('PASS: Windows Desktop activated and received signed limits in its offline lease.');
            $this->info('PASS: Android and iOS waiter devices paired through live tenant APIs.');
            $this->info('PASS: third mobile was rejected at the mobile activation limit.');
            $this->info('PASS: revoked mobile freed one slot and replacement pairing succeeded.');
            $this->info('PASS: extra Windows Desktop was rejected at the total device limit.');
            $this->info('PASS: final active state is 1 Windows Desktop + 2 waiter mobiles.');
            $this->line('Acceptance replacement device: '.$replacement['id']);

            return self::SUCCESS;
        } catch (Throwable $e) {
            $this->error('FAIL: '.$e->getMessage());

            return self::FAILURE;
        } finally {
            if (tenancy()->initialized) {
                tenancy()->end();
            }

            try {
                tenancy()->initialize($tenant);

                $user = TenantUser::query()->where('email', $email)->first();
                if ($user) {
                    $user->tokens()->delete();
                    SyncDeviceState::query()->where('tenant_user_id', $user->getKey())->delete();
                    $user->delete();
                }
            } catch (Throwable $cleanupError) {
                $this->warn('Tenant cleanup warning: '.$cleanupError->getMessage());
            } finally {
                if (tenancy()->initialized) {
                    tenancy()->end();
                }
            }

            try {
                $business->licenseEvents()->delete();
                OfflineLease::query()->where('business_id', $business->id)->delete();
                $business->devices()->delete();
                $business->licenseKeys()->delete();

                $subscription->forceFill($subscriptionBackup)->save();
                $business->forceFill($businessBackup)->save();

                if ($temporaryPlan) {
                    $temporaryPlan->features()->delete();
                    $temporaryPlan->prices()->delete();
                    $temporaryPlan->delete();
                }
            } catch (Throwable $cleanupError) {
                $this->warn('Central cleanup warning: '.$cleanupError->getMessage());
            }
        }
    }

    private function pairMobile(
        string $baseUrl,
        string $token,
        string $desktopId,
        string $desktopSecret,
        string $deviceUid,
        string $platform,
    ): array {
        $pairingToken = $this->pairingToken($baseUrl, $token, $desktopId, $desktopSecret);

        $response = $this->expectStatus(
            Http::acceptJson()->timeout(30)->post($baseUrl.'/api/v1/pairing-tokens/redeem', [
                'pairing_token' => $pairingToken,
                'device_uid' => $deviceUid,
                'device_name' => 'Acceptance '.ucfirst($platform),
                'platform' => $platform,
                'app_version' => 'acceptance',
            ]),
            201,
            ucfirst($platform).' pairing',
        );

        return [
            'id' => (string) $response->json('device.id'),
            'secret' => (string) $response->json('device_secret'),
        ];
    }

    private function pairingToken(
        string $baseUrl,
        string $token,
        string $desktopId,
        string $desktopSecret,
    ): string {
        $response = $this->expectStatus(
            Http::acceptJson()
                ->timeout(30)
                ->withToken($token)
                ->withHeaders([
                    'X-Device-Id' => $desktopId,
                    'X-Device-Secret' => $desktopSecret,
                    'X-App-Version' => 'acceptance',
                ])
                ->post($baseUrl.'/api/v1/pairing-tokens'),
            201,
            'Pairing-token issue',
        );

        $pairingToken = (string) $response->json('data.pairing_token');

        if ($pairingToken === '') {
            throw new RuntimeException('Pairing-token API returned an empty token.');
        }

        return $pairingToken;
    }

    private function expectStatus(Response $response, int $expected, string $label): Response
    {
        if ($response->status() !== $expected) {
            throw new RuntimeException(
                "{$label} expected HTTP {$expected}, received {$response->status()}: ".$response->body(),
            );
        }

        return $response;
    }
}
