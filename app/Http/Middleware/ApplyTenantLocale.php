<?php

namespace App\Http\Middleware;

use App\Models\Restaurant;
use Closure;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Schema;
use Symfony\Component\HttpFoundation\Response;

class ApplyTenantLocale
{
    public function handle(Request $request, Closure $next): Response
    {
        $locale = config('restaurant.default_locale', 'en');

        if (Schema::connection('tenant')->hasTable('restaurants')) {
            $locale = Restaurant::where('profile_key', 'primary')->value('locale') ?: $locale;
        }

        if (array_key_exists($locale, config('restaurant.locales', []))) {
            app()->setLocale($locale);
        }

        return $next($request);
    }
}
