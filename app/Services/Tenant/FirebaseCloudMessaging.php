<?php

namespace App\Services\Tenant;

use Illuminate\Support\Facades\Cache;
use Illuminate\Support\Facades\Http;
use RuntimeException;

class FirebaseCloudMessaging
{
    public function configured(): bool
    {
        $path = config('services.restaurant_fcm.credentials_path');

        return is_string($path) && $path !== '' && is_readable($path);
    }

    public function send(string $token, string $tableLabel, string $orderId, string $itemId): string
    {
        $account = $this->serviceAccount();
        $project = (string) config('services.restaurant_fcm.project_id', '');
        if ($project === '') {
            $project = (string) ($account['project_id'] ?? '');
        }
        if ($project === '' || ! preg_match('/^[a-zA-Z0-9._~:-]+$/', $project)) {
            throw new RuntimeException('missing_firebase_project');
        }

        $payload = [
            'message' => [
                'token' => $token,
                'notification' => [
                    'title' => 'READY FOR PICKUP',
                    'body' => 'Kitchen is ready · '.$tableLabel,
                ],
                'data' => [
                    'type' => 'restaurant.kot.ready',
                    'order_id' => $orderId,
                    'item_id' => $itemId,
                ],
                'android' => ['priority' => 'HIGH'],
                'apns' => [
                    'headers' => ['apns-priority' => '10'],
                    'payload' => ['aps' => ['sound' => 'default']],
                ],
            ],
        ];
        $result = Http::timeout(8)
            ->withToken($this->accessToken($account))
            ->acceptJson()
            ->post('https://fcm.googleapis.com/v1/projects/'.$project.'/messages:send', $payload);

        if ($result->successful()) {
            return 'sent';
        }
        $message = $result->json('error.status') ?: 'http_'.$result->status();

        if (in_array($result->status(), [400, 404], true) &&
            in_array($message, ['UNREGISTERED', 'INVALID_ARGUMENT', 'NOT_FOUND'], true)) {
            return 'invalid';
        }
        throw new RuntimeException('fcm_'.$message);
    }

    private function serviceAccount(): array
    {
        $path = config('services.restaurant_fcm.credentials_path');
        if (! is_string($path) || ! is_readable($path)) {
            throw new RuntimeException('fcm_not_configured');
        }

        $account = json_decode((string) file_get_contents($path), true);
        if (! is_array($account) || empty($account['client_email']) ||
            empty($account['private_key'])) {
            throw new RuntimeException('invalid_service_account');
        }

        return $account;
    }

    private function accessToken(array $account): string
    {
        $cacheKey = 'restaurant_fcm_oauth_'.hash('sha256', $account['client_email']);

        return Cache::remember($cacheKey, now()->addMinutes(50), function () use ($account): string {
            $now = time();
            $encode = static fn (array $array) => rtrim(
                strtr(base64_encode(json_encode($array, JSON_THROW_ON_ERROR)), '+/', '-_'), '=');
            $header = $encode(['alg' => 'RS256', 'typ' => 'JWT']);
            $claims = $encode([
                'iss' => $account['client_email'],
                'scope' => 'https://www.googleapis.com/auth/firebase.messaging',
                'aud' => 'https://oauth2.googleapis.com/token',
                'iat' => $now,
                'exp' => $now + 3600,
            ]);
            $unsigned = $header.'.'.$claims;
            if (! openssl_sign($unsigned, $signature, $account['private_key'], OPENSSL_ALGO_SHA256)) {
                throw new RuntimeException('service_account_signing_failed');
            }
            $assertion = $unsigned.'.'.rtrim(strtr(base64_encode($signature), '+/', '-_'), '=');
            $result = Http::asForm()->timeout(8)->post('https://oauth2.googleapis.com/token', [
                'grant_type' => 'urn:ietf:params:oauth:grant-type:jwt-bearer',
                'assertion' => $assertion,
            ]);
            if (! $result->successful() || ! is_string($result->json('access_token'))) {
                throw new RuntimeException('oauth_token_unavailable');
            }

            return $result->json('access_token');
        });
    }
}
