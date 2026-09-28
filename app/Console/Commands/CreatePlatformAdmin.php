<?php

namespace App\Console\Commands;

use App\Enums\PlatformRole;
use App\Models\AdminUser;
use Illuminate\Console\Command;
use Illuminate\Support\Str;

class CreatePlatformAdmin extends Command
{
    protected $signature = 'platform:admin:create
        {email : Platform administrator email address}
        {--name=BusinessOS Admin : Platform administrator display name}
        {--password= : Password to use instead of prompting}
        {--generate-password : Generate a strong password and print it once}';

    protected $description = 'Create the initial platform super administrator if the email does not already exist';

    public function handle(): int
    {
        $email = Str::lower(trim((string) $this->argument('email')));
        $name = trim((string) $this->option('name'));

        if (! filter_var($email, FILTER_VALIDATE_EMAIL)) {
            $this->error('A valid email address is required.');

            return self::FAILURE;
        }

        if (AdminUser::query()->where('email', $email)->exists()) {
            $this->error('A platform account with this email already exists. No changes were made.');

            return self::FAILURE;
        }

        $generated = (bool) $this->option('generate-password');

        if ($generated && filled($this->option('password'))) {
            $this->error('Use either --password or --generate-password, not both.');

            return self::FAILURE;
        }

        $password = $generated
            ? $this->generatedPassword()
            : (string) ($this->option('password') ?: $this->secret('Password (minimum 12 characters)'));

        if (strlen($password) < 12) {
            $this->error('Password must be at least 12 characters.');

            return self::FAILURE;
        }

        $admin = AdminUser::query()->create([
            'name' => $name !== '' ? $name : 'BusinessOS Admin',
            'email' => $email,
            'password' => $password,
            'role' => PlatformRole::SuperAdmin,
            'is_active' => true,
        ]);

        $this->info('Platform super administrator created.');
        $this->line('Email: '.$admin->email);

        if ($generated) {
            $this->warn('Generated password (shown once): '.$password);
        }

        return self::SUCCESS;
    }

    private function generatedPassword(): string
    {
        return rtrim(strtr(base64_encode(random_bytes(18)), '+/', '-_'), '=');
    }
}
