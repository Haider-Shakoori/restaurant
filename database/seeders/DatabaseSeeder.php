<?php

namespace Database\Seeders;

use App\Enums\PlatformRole;
use App\Models\AdminUser;
use Illuminate\Database\Console\Seeds\WithoutModelEvents;
use Illuminate\Database\Seeder;

class DatabaseSeeder extends Seeder
{
    use WithoutModelEvents;

    public function run(): void
    {
        if (! app()->environment('production')) {
            AdminUser::factory()->create([
                'name' => 'BusinessOS Admin',
                'email' => 'admin@example.test',
                'role' => PlatformRole::SuperAdmin,
                'is_active' => true,
            ]);
        }
    }
}
