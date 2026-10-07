<?php

namespace App\Services\Tenant;

use App\Models\Business;
use App\Models\DeviceActivation;
use App\Models\TenantUser;
use Illuminate\Support\Facades\Cache;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;

class WaiterPairingService
{
    private const CACHE_PREFIX = 'restaurant:waiter-pairing:';

    /**
     * @return array{token: string, expires_at: string}
     */
    public function issue(
        Business $business,
        DeviceActivation $desktop,
        TenantUser $user,
    ): array {
        if (strtolower((string) $desktop->platform) !== 'windows') {
            throw ValidationException::withMessages([
                'device' => 'Waiter pairing codes can only be issued by an activated Windows Restaurant Desktop.',
            ]);
        }

        $token = rtrim(strtr(base64_encode(random_bytes(32)), '+/', '-_'), '=');
        $expiresAt = now()->addMinutes(max(1, (int) config('license.pairing_token_minutes', 5)));

        Cache::put(
            $this->cacheKey($token),
            [
                'business_id' => (string) $business->id,
                'tenant_id' => (string) $business->tenant_id,
                'desktop_device_id' => (string) $desktop->id,
                'issued_by_user_id' => (int) $user->id,
                'expires_at' => $expiresAt->getTimestamp(),
            ],
            $expiresAt,
        );

        return [
            'token' => $token,
            'expires_at' => $expiresAt->copy()->utc()->toIso8601String(),
        ];
    }

    /**
     * @return array<string, mixed>
     */
    public function redeem(Business $business, string $token): array
    {
        $value = Cache::pull($this->cacheKey($token));

        if (is_array($value) === false) {
            throw ValidationException::withMessages([
                'pairing_token' => 'The waiter pairing code is invalid, expired, or has already been used.',
            ]);
        }

        $businessId = strval($value['business_id'] ?? '');
        $tenantId = strval($value['tenant_id'] ?? '');
        $expiresAt = intval($value['expires_at'] ?? 0);

        if (
            hash_equals($businessId, strval($business->id)) === false ||
            hash_equals($tenantId, strval($business->tenant_id)) === false ||
            $expiresAt <= now()->getTimestamp()
        ) {
            throw ValidationException::withMessages([
                'pairing_token' => 'The waiter pairing code is invalid, expired, or has already been used.',
            ]);
        }

        return $value;
    }

    private function cacheKey(string $token): string
    {
        return self::CACHE_PREFIX.hash('sha256', trim($token));
    }
}
