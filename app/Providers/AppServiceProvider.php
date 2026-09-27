<?php

namespace App\Providers;

use App\Enums\PlatformRole;
use App\Models\AdminUser;
use Illuminate\Support\Facades\Gate;
use Illuminate\Support\ServiceProvider;

class AppServiceProvider extends ServiceProvider
{
    public function register(): void
    {
        //
    }

    public function boot(): void
    {
        Gate::define('view-platform', fn (AdminUser $user): bool => $user->is_active);

        Gate::define(
            'manage-platform',
            fn (AdminUser $user): bool => $user->is_active
                && in_array($user->role, [PlatformRole::SuperAdmin, PlatformRole::Operator], true)
        );

        Gate::define(
            'manage-operators',
            fn (AdminUser $user): bool => $user->is_active && $user->role === PlatformRole::SuperAdmin
        );
    }
}
