<?php

namespace App\Console\Commands;

use Illuminate\Console\Command;
use Illuminate\Support\Facades\File;

class GenerateLicenseSigningKeys extends Command
{
    protected $signature = 'licenses:generate-signing-keys {--force : Overwrite existing key files}';

    protected $description = 'Generate the Ed25519 signing keypair used for offline restaurant leases';

    public function handle(): int
    {
        $privatePath = (string) config('license.signing.private_key_path');
        $publicPath = (string) config('license.signing.public_key_path');

        if (! $this->option('force') && (is_file($privatePath) || is_file($publicPath))) {
            $this->error('Signing key files already exist. Use --force only when intentionally rotating them.');

            return self::FAILURE;
        }

        File::ensureDirectoryExists(dirname($privatePath));
        File::ensureDirectoryExists(dirname($publicPath));

        $keypair = sodium_crypto_sign_keypair();
        $secret = sodium_crypto_sign_secretkey($keypair);
        $public = sodium_crypto_sign_publickey($keypair);

        File::put($privatePath, base64_encode($secret).PHP_EOL);
        File::put($publicPath, base64_encode($public).PHP_EOL);

        @chmod($privatePath, 0600);
        @chmod($publicPath, 0644);

        $this->info('Ed25519 license signing keypair generated.');
        $this->line('Private key path: '.$privatePath);
        $this->line('Public key path: '.$publicPath);
        $this->line('Key ID: '.config('license.signing.key_id'));
        $this->line('Public key: '.rtrim(strtr(base64_encode($public), '+/', '-_'), '='));
        $this->warn('Never commit, upload, or distribute the private key.');

        return self::SUCCESS;
    }
}
