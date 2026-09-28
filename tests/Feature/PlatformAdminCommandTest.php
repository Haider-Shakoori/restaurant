<?php

namespace Tests\Feature;

use App\Enums\PlatformRole;
use App\Models\AdminUser;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Hash;
use Tests\TestCase;

class PlatformAdminCommandTest extends TestCase
{
    use RefreshDatabase;

    public function test_command_creates_active_super_admin_once(): void
    {
        $this->artisan('platform:admin:create', [
            'email' => 'Admin@BusinessOS.af',
            '--name' => 'BusinessOS Admin',
            '--password' => 'StrongPlatformPass123!',
        ])->assertSuccessful();

        $admin = AdminUser::query()->where('email', 'admin@businessos.af')->sole();

        $this->assertSame('BusinessOS Admin', $admin->name);
        $this->assertSame(PlatformRole::SuperAdmin, $admin->role);
        $this->assertTrue($admin->is_active);
        $this->assertTrue(Hash::check('StrongPlatformPass123!', $admin->password));
    }

    public function test_command_refuses_to_modify_existing_platform_account(): void
    {
        $existing = AdminUser::factory()->create([
            'email' => 'admin@businessos.af',
            'name' => 'Existing Admin',
            'role' => PlatformRole::Support,
            'is_active' => false,
            'password' => 'OriginalPassword123!',
        ]);

        $this->artisan('platform:admin:create', [
            'email' => 'admin@businessos.af',
            '--name' => 'Replacement Admin',
            '--password' => 'ReplacementPass123!',
        ])
            ->expectsOutputToContain('already exists')
            ->assertFailed();

        $existing->refresh();

        $this->assertSame('Existing Admin', $existing->name);
        $this->assertSame(PlatformRole::Support, $existing->role);
        $this->assertFalse($existing->is_active);
        $this->assertTrue(Hash::check('OriginalPassword123!', $existing->password));
        $this->assertSame(1, AdminUser::query()->where('email', 'admin@businessos.af')->count());
    }

    public function test_command_rejects_short_passwords(): void
    {
        $this->artisan('platform:admin:create', [
            'email' => 'admin@businessos.af',
            '--password' => 'short',
        ])
            ->expectsOutputToContain('at least 12 characters')
            ->assertFailed();

        $this->assertDatabaseMissing('admin_users', [
            'email' => 'admin@businessos.af',
        ]);
    }
}
