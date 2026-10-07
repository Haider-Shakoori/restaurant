<?php

namespace App\Services\Platform;

use RuntimeException;

class LicenseSigningService
{
    /**
     * @param  array<string, mixed>  $payload
     * @return array{payload: array<string, mixed>, signature: string, algorithm: string, key_id: string}
     */
    public function sign(array $payload): array
    {
        $secretKey = $this->secretKey();
        $canonical = $this->canonicalJson($payload);
        $signature = sodium_crypto_sign_detached($canonical, $secretKey);

        return [
            'payload' => $payload,
            'signature' => $this->base64UrlEncode($signature),
            'algorithm' => 'Ed25519',
            'key_id' => (string) config('license.signing.key_id'),
        ];
    }

    /**
     * @param  array<string, mixed>  $payload
     */
    public function verify(array $payload, string $signature): bool
    {
        $decoded = $this->base64UrlDecode($signature);

        if ($decoded === null) {
            return false;
        }

        return sodium_crypto_sign_verify_detached(
            $decoded,
            $this->canonicalJson($payload),
            $this->publicKey(),
        );
    }

    public function publicKeyEncoded(): string
    {
        return $this->base64UrlEncode($this->publicKey());
    }

    public function publicKey(): string
    {
        // The private signing key is the source of truth. Deriving the public
        // key from it prevents a stale public-key file/env value from causing
        // every desktop/mobile lease signature to fail verification.
        return sodium_crypto_sign_publickey_from_secretkey($this->secretKey());
    }

    private function secretKey(): string
    {
        $configured = $this->readConfiguredKey('private_key', 'private_key_path');

        if ($configured === null) {
            throw new RuntimeException(
                'No Ed25519 private signing key is configured. Run licenses:generate-signing-keys and configure the generated key path.'
            );
        }

        $decoded = $this->decodeKeyMaterial($configured);

        if (strlen($decoded) !== SODIUM_CRYPTO_SIGN_SECRETKEYBYTES) {
            throw new RuntimeException('Configured Ed25519 private key has an invalid length.');
        }

        return $decoded;
    }

    private function readConfiguredKey(string $inline, string $path): ?string
    {
        $value = config('license.signing.'.$inline);

        if (is_string($value) && trim($value) !== '') {
            return trim($value);
        }

        $file = config('license.signing.'.$path);

        if (is_string($file) && is_file($file)) {
            $contents = file_get_contents($file);

            return $contents === false ? null : trim($contents);
        }

        return null;
    }

    private function decodeKeyMaterial(string $value): string
    {
        $value = str_starts_with($value, 'base64:') ? substr($value, 7) : $value;

        $value = strtr($value, '-_', '+/');
        $padding = strlen($value) % 4;

        if ($padding > 0) {
            $value .= str_repeat('=', 4 - $padding);
        }

        $decoded = base64_decode($value, true);

        if ($decoded === false) {
            throw new RuntimeException('Signing key material is not valid base64.');
        }

        return $decoded;
    }

    /**
     * @param  array<string, mixed>  $payload
     */
    private function canonicalJson(array $payload): string
    {
        $canonical = $this->canonicalize($payload);

        return json_encode(
            $canonical,
            JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE,
        );
    }

    private function canonicalize(mixed $value): mixed
    {
        if (! is_array($value)) {
            return $value;
        }

        if (array_is_list($value)) {
            return array_map(fn (mixed $item): mixed => $this->canonicalize($item), $value);
        }

        ksort($value, SORT_STRING);

        foreach ($value as $key => $item) {
            $value[$key] = $this->canonicalize($item);
        }

        return $value;
    }

    private function base64UrlEncode(string $value): string
    {
        return rtrim(strtr(base64_encode($value), '+/', '-_'), '=');
    }

    private function base64UrlDecode(string $value): ?string
    {
        $padding = strlen($value) % 4;

        if ($padding > 0) {
            $value .= str_repeat('=', 4 - $padding);
        }

        $decoded = base64_decode(strtr($value, '-_', '+/'), true);

        return $decoded === false ? null : $decoded;
    }
}
