<?php

namespace App\Services\Platform;

use App\Enums\DeviceStatus;
use App\Enums\LicenseStatus;
use App\Models\AdminUser;
use App\Models\Business;
use App\Models\DeviceActivation;
use App\Models\LicenseKey;
use App\Models\OfflineLease;
use App\Models\TenantUser;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;

class LicenseService
{
    private const LICENSE_ALPHABET = '23456789ABCDEFGHJKLMNPQRSTUVWXYZ';

    public function __construct(
        private readonly SubscriptionService $subscriptions,
        private readonly LicenseSigningService $signing,
    ) {}

    /**
     * @return array{license: LicenseKey, raw_key: string}
     */
    public function generate(Business $business, AdminUser $admin): array
    {
        $business->refresh();

        $access = $this->subscriptions->access($business);

        if (! $access->allowed) {
            throw ValidationException::withMessages([
                'license' => 'An active trial or subscription is required before generating a license.',
            ]);
        }

        return DB::connection(config('tenancy.database.central_connection'))->transaction(
            function () use ($business, $admin, $access): array {
                $this->revokeActiveLicenses($business, $admin, 'License regenerated.');

                $version = ((int) $business->licenseKeys()->max('version')) + 1;
                $rawKey = $this->makeRawLicenseKey();
                $deviceLimit = $this->resolveDeviceLimit($access->features);

                $license = $business->licenseKeys()->create([
                    'generated_by_admin_id' => $admin->id,
                    'version' => $version,
                    'key_hash' => $this->hashLicenseKey($rawKey),
                    'key_prefix' => (string) config('license.key_prefix', 'RST'),
                    'key_last4' => substr($rawKey, -4),
                    'status' => LicenseStatus::Active,
                    'max_devices_snapshot' => $deviceLimit,
                    'generated_at' => now(),
                ]);

                $this->recordEvent(
                    $business,
                    $license,
                    null,
                    $admin,
                    'license.generated',
                    'New restaurant license generated. The raw key is shown only once.',
                    [
                        'version' => $version,
                        'max_devices' => $deviceLimit,
                    ],
                );

                return [
                    'license' => $license,
                    'raw_key' => $rawKey,
                ];
            }
        );
    }

    public function revoke(LicenseKey $license, AdminUser $admin, string $reason = 'License revoked.'): void
    {
        DB::connection(config('tenancy.database.central_connection'))->transaction(
            function () use ($license, $admin, $reason): void {
                if ($license->status === LicenseStatus::Revoked) {
                    return;
                }

                $license->update([
                    'status' => LicenseStatus::Revoked,
                    'revoked_at' => now(),
                ]);

                $license->devices()
                    ->where('status', DeviceStatus::Active)
                    ->update([
                        'status' => DeviceStatus::Revoked->value,
                        'revoked_at' => now(),
                    ]);

                $this->recordEvent(
                    $license->business,
                    $license,
                    null,
                    $admin,
                    'license.revoked',
                    $reason,
                );
            }
        );
    }

    /**
     * @return array{device: DeviceActivation, device_secret: string, lease: array<string, mixed>}
     */
    public function activateDevice(
        Business $business,
        string $rawLicenseKey,
        string $deviceUid,
        ?string $deviceName,
        string $platform,
        ?string $appVersion,
    ): array
    {
        $business->refresh();

        $access = $this->subscriptions->access($business);

        if (! $access->allowed) {
            throw ValidationException::withMessages([
                'license_key' => 'An active trial or subscription is required for activation.',
            ]);
        }

        $license = LicenseKey::query()
            ->where('business_id', $business->id)
            ->where('key_hash', $this->hashLicenseKey($rawLicenseKey))
            ->where('status', LicenseStatus::Active)
            ->first();

        if (! $license) {
            throw ValidationException::withMessages([
                'license_key' => 'The license key is invalid or has been revoked.',
            ]);
        }

        return $this->activateAgainstLicense(
            $business,
            $license,
            $access->features,
            $deviceUid,
            $deviceName,
            $platform,
            $appVersion,
            false,
        );
    }

    /**
     * Activate a waiter mobile after a short-lived pairing token has already
     * proved that an authorized Restaurant Desktop approved the pairing.
     *
     * @return array{device: DeviceActivation, device_secret: string, lease: array<string, mixed>}
     */
    public function activatePairedMobile(
        Business $business,
        string $deviceUid,
        ?string $deviceName,
        string $platform,
        ?string $appVersion,
    ): array
    {
        if (! $this->isMobilePlatform($platform)) {
            throw ValidationException::withMessages([
                'platform' => 'Desktop pairing tokens may only activate Android or iOS waiter devices.',
            ]);
        }

        $business->refresh();
        $access = $this->subscriptions->access($business);

        if (! $access->allowed) {
            throw ValidationException::withMessages([
                'pairing_token' => 'An active trial or subscription is required for waiter pairing.',
            ]);
        }

        $license = LicenseKey::query()
            ->where('business_id', $business->id)
            ->where('status', LicenseStatus::Active)
            ->latest('version')
            ->first();

        if (! $license) {
            throw ValidationException::withMessages([
                'pairing_token' => 'Generate an active Restaurant license before pairing waiter devices.',
            ]);
        }

        return $this->activateAgainstLicense(
            $business,
            $license,
            $access->features,
            $deviceUid,
            $deviceName,
            $platform,
            $appVersion,
            true,
        );
    }

    /**
     * @param  array<string, mixed>  $features
     * @return array{device: DeviceActivation, device_secret: string, lease: array<string, mixed>}
     */
    private function activateAgainstLicense(
        Business $business,
        LicenseKey $license,
        array $features,
        string $deviceUid,
        ?string $deviceName,
        string $platform,
        ?string $appVersion,
        bool $paired,
    ): array
    {
        return DB::connection(config('tenancy.database.central_connection'))->transaction(
            function () use ($business, $license, $features, $deviceUid, $deviceName, $platform, $appVersion, $paired): array {
                $existing = DeviceActivation::query()
                    ->where('business_id', $business->id)
                    ->where('device_uid', $deviceUid)
                    ->first();

                $mobile = $this->isMobilePlatform($platform);
                $activeOtherDevices = DeviceActivation::query()
                    ->where('business_id', $business->id)
                    ->where('status', DeviceStatus::Active)
                    ->when($mobile, fn ($query) => $query->whereIn('platform', ['android', 'ios']))
                    ->when($existing, fn ($query) => $query->whereKeyNot($existing->getKey()))
                    ->count();

                $limit = $mobile
                    ? $this->resolveMobileDeviceLimit($features)
                    : $license->max_devices_snapshot;

                if ($limit !== null && $activeOtherDevices >= $limit) {
                    throw ValidationException::withMessages([
                        'device_uid' => sprintf(
                            'The restaurant has reached its %d-%s activation limit.',
                            $limit,
                            $mobile ? 'mobile waiter' : 'device',
                        ),
                    ]);
                }

                $secret = $this->makeDeviceSecret();

                $device = DeviceActivation::query()->updateOrCreate(
                    [
                        'business_id' => $business->id,
                        'device_uid' => $deviceUid,
                    ],
                    [
                        'license_key_id' => $license->id,
                        'device_name' => $deviceName,
                        'platform' => strtolower(trim($platform)),
                        'app_version' => $appVersion,
                        'credential_hash' => $this->hashDeviceSecret($secret),
                        'credential_last4' => substr($secret, -4),
                        'status' => DeviceStatus::Active,
                        'activated_at' => now(),
                        'last_seen_at' => now(),
                        'last_verified_at' => now(),
                        'revoked_at' => null,
                    ],
                );

                $license->update([
                    'last_used_at' => now(),
                ]);

                $this->recordEvent(
                    $business,
                    $license,
                    $device,
                    null,
                    'device.activated',
                    'Device activated and issued a dedicated device credential.',
                    [
                        'device_uid' => $deviceUid,
                        'platform' => $platform,
                        'app_version' => $appVersion,
                        'paired' => $paired,
                    ],
                );

                return [
                    'device' => $device,
                    'device_secret' => $secret,
                    'lease' => $this->issueLease($device),
                ];
            }
        );
    }

    public function authenticateDevice(
        Business $business,
        string $deviceId,
        string $deviceSecret,
        ?string $appVersion = null,
    ): DeviceActivation {
        $device = DeviceActivation::query()
            ->with(['licenseKey', 'business'])
            ->where('business_id', $business->id)
            ->whereKey($deviceId)
            ->where('status', DeviceStatus::Active)
            ->first();

        if (! $device || ! hash_equals($device->credential_hash, $this->hashDeviceSecret($deviceSecret))) {
            throw ValidationException::withMessages([
                'device_secret' => 'The device credential is invalid or revoked.',
            ]);
        }

        if ($device->licenseKey->status !== LicenseStatus::Active) {
            throw ValidationException::withMessages([
                'device_secret' => 'The license used by this device has been revoked.',
            ]);
        }

        $device->update([
            'app_version' => $appVersion ?: $device->app_version,
            'last_seen_at' => now(),
            'last_verified_at' => now(),
        ]);

        $device->licenseKey->update([
            'last_used_at' => now(),
        ]);

        return $device->fresh(['licenseKey', 'business']);
    }

    /**
     * @return array<string, mixed>
     */
    public function refreshLease(
        Business $business,
        string $deviceId,
        string $deviceSecret,
        ?string $appVersion = null,
    ): array
    {
        $device = DeviceActivation::query()
            ->where('business_id', $business->id)
            ->whereKey($deviceId)
            ->where('status', DeviceStatus::Active)
            ->first();

        if (! $device || ! hash_equals($device->credential_hash, $this->hashDeviceSecret($deviceSecret))) {
            throw ValidationException::withMessages([
                'device_secret' => 'The device credential is invalid or revoked.',
            ]);
        }

        if ($device->licenseKey->status !== LicenseStatus::Active) {
            throw ValidationException::withMessages([
                'device_secret' => 'The license used by this device has been revoked.',
            ]);
        }

        $device->update([
            'app_version' => $appVersion ?: $device->app_version,
            'last_seen_at' => now(),
            'last_verified_at' => now(),
        ]);

        $device->licenseKey->update([
            'last_used_at' => now(),
        ]);

        return $this->issueLease($device->fresh(['licenseKey', 'business']));
    }

    public function revokeDevice(DeviceActivation $device, AdminUser $admin): void
    {
        if ($device->status === DeviceStatus::Revoked) {
            return;
        }

        $device->update([
            'status' => DeviceStatus::Revoked,
            'revoked_at' => now(),
        ]);

        $this->recordEvent(
            $device->business,
            $device->licenseKey,
            $device,
            $admin,
            'device.revoked',
            'Device activation revoked. Existing offline leases expire at their signed expiry.',
        );
    }

    public function revokeMobileDeviceForTenant(
        DeviceActivation $device,
        TenantUser $user,
    ): void
    {
        if (! $this->isMobilePlatform((string) $device->platform)) {
            throw ValidationException::withMessages([
                'device' => 'Only waiter mobile activations can be revoked from restaurant settings.',
            ]);
        }

        if ($device->status === DeviceStatus::Revoked) {
            return;
        }

        $device->update([
            'status' => DeviceStatus::Revoked,
            'revoked_at' => now(),
        ]);

        $this->recordEvent(
            $device->business,
            $device->licenseKey,
            $device,
            null,
            'device.revoked_by_tenant',
            'Waiter mobile activation revoked by restaurant management.',
            [
                'tenant_user_id' => $user->id,
                'tenant_user_public_id' => $user->public_id,
            ],
        );
    }

    /**
     * @return array<string, mixed>
     */
    public function issueLease(DeviceActivation $device): array
    {
        $business = $device->business()->firstOrFail();
        $access = $this->subscriptions->access($business);

        if (! $access->allowed || ! $access->subscription || ! $access->endsAt) {
            throw ValidationException::withMessages([
                'lease' => 'An active trial or subscription is required to issue an offline lease.',
            ]);
        }

        if ($device->status !== DeviceStatus::Active || $device->licenseKey->status !== LicenseStatus::Active) {
            throw ValidationException::withMessages([
                'lease' => 'The device or license has been revoked.',
            ]);
        }

        $issuedAt = now();
        $offlineLimit = $issuedAt->copy()->addDays(max(1, (int) config('license.offline_grace_days', 7)));
        $expiresAt = $offlineLimit->lessThan($access->endsAt)
            ? $offlineLimit
            : $access->endsAt->copy();

        if (! $expiresAt->greaterThan($issuedAt)) {
            throw ValidationException::withMessages([
                'lease' => 'The subscription expires before an offline lease can be issued.',
            ]);
        }

        $leaseId = (string) Str::ulid();
        $keyId = (string) config('license.signing.key_id');

        $payload = [
            'schema_version' => (int) config('license.schema_version', 1),
            'lease_id' => $leaseId,
            'key_id' => $keyId,
            'tenant_id' => $business->tenant_id,
            'business_id' => $business->id,
            'subscription_id' => $access->subscription->id,
            'license_version' => $device->licenseKey->version,
            'device_id' => $device->id,
            'device_uid' => $device->device_uid,
            'plan' => [
                'code' => $access->subscription->plan_code_snapshot,
                'name' => $access->subscription->plan_name_snapshot,
            ],
            'features' => $access->features,
            'device_limit' => $device->licenseKey->max_devices_snapshot,
            'mobile_device_limit' => $this->resolveMobileDeviceLimit($access->features),
            'issued_at' => $issuedAt->copy()->utc()->toIso8601String(),
            'offline_valid_until' => $expiresAt->copy()->utc()->toIso8601String(),
            'subscription_ends_at' => $access->endsAt->copy()->utc()->toIso8601String(),
        ];

        $signed = $this->signing->sign($payload);
        $payloadHash = hash(
            'sha256',
            json_encode($signed['payload'], JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE)
                .'.'.$signed['signature'],
        );

        OfflineLease::create([
            'id' => $leaseId,
            'business_id' => $business->id,
            'license_key_id' => $device->license_key_id,
            'device_activation_id' => $device->id,
            'subscription_id' => $access->subscription->id,
            'key_id' => $keyId,
            'schema_version' => (int) config('license.schema_version', 1),
            'payload_hash' => $payloadHash,
            'issued_at' => $issuedAt,
            'expires_at' => $expiresAt,
        ]);

        $this->recordEvent(
            $business,
            $device->licenseKey,
            $device,
            null,
            'lease.issued',
            'Signed offline lease issued.',
            [
                'lease_id' => $leaseId,
                'expires_at' => $expiresAt->toIso8601String(),
                'key_id' => $keyId,
            ],
        );

        return $signed;
    }

    /**
     * @param  array<string, mixed>  $features
     */
    private function resolveMobileDeviceLimit(array $features): ?int
    {
        $value = data_get($features, 'max_mobile_devices');

        if ($value === null || $value === '') {
            return $this->resolveDeviceLimit($features);
        }

        if (is_string($value) && strtolower(trim($value)) === 'unlimited') {
            return null;
        }

        if (is_numeric($value)) {
            $limit = (int) $value;
            return $limit > 0 ? $limit : null;
        }

        return $this->resolveDeviceLimit($features);
    }

    private function isMobilePlatform(string $platform): bool
    {
        return in_array(strtolower(trim($platform)), ['android', 'ios'], true);
    }

    private function resolveDeviceLimit(array $features): ?int
    {
        $value = data_get($features, 'max_devices');

        if (is_string($value) && strtolower(trim($value)) === 'unlimited') {
            return null;
        }

        if (is_numeric($value)) {
            $limit = (int) $value;

            return $limit > 0 ? $limit : null;
        }

        $default = (int) config('license.default_max_devices', 5);

        return $default > 0 ? $default : null;
    }

    private function revokeActiveLicenses(Business $business, AdminUser $admin, string $reason): void
    {
        $licenses = $business->licenseKeys()
            ->where('status', LicenseStatus::Active)
            ->get();

        foreach ($licenses as $license) {
            $license->update([
                'status' => LicenseStatus::Revoked,
                'revoked_at' => now(),
            ]);

            $license->devices()
                ->where('status', DeviceStatus::Active)
                ->update([
                    'status' => DeviceStatus::Revoked->value,
                    'revoked_at' => now(),
                ]);

            $this->recordEvent(
                $business,
                $license,
                null,
                $admin,
                'license.revoked',
                $reason,
            );
        }
    }

    private function makeRawLicenseKey(): string
    {
        $groups = [];

        for ($group = 0; $group < 4; $group++) {
            $value = '';

            for ($i = 0; $i < 4; $i++) {
                $value .= self::LICENSE_ALPHABET[random_int(0, strlen(self::LICENSE_ALPHABET) - 1)];
            }

            $groups[] = $value;
        }

        return strtoupper((string) config('license.key_prefix', 'RST')).'-'.implode('-', $groups);
    }

    private function makeDeviceSecret(): string
    {
        return rtrim(strtr(base64_encode(random_bytes(32)), '+/', '-_'), '=');
    }

    private function hashLicenseKey(string $rawKey): string
    {
        $normalized = strtoupper(preg_replace('/\s+/', '', trim($rawKey)) ?? trim($rawKey));

        return hash_hmac('sha256', $normalized, $this->licensePepper());
    }

    private function hashDeviceSecret(string $secret): string
    {
        return hash_hmac('sha256', trim($secret), $this->devicePepper());
    }

    private function licensePepper(): string
    {
        return (string) (config('license.hash_pepper') ?: config('app.key'));
    }

    private function devicePepper(): string
    {
        return (string) (config('license.device_secret_pepper') ?: config('app.key'));
    }

    /**
     * @param  array<string, mixed>  $context
     */
    private function recordEvent(
        Business $business,
        ?LicenseKey $license,
        ?DeviceActivation $device,
        ?AdminUser $admin,
        string $event,
        string $message,
        array $context = [],
    ): void
    {
        $business->licenseEvents()->create([
            'license_key_id' => $license?->id,
            'device_activation_id' => $device?->id,
            'admin_user_id' => $admin?->id,
            'event' => $event,
            'message' => $message,
            'context' => $context,
            'occurred_at' => now(),
        ]);
    }
}
