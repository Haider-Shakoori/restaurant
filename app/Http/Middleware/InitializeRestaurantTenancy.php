<?php

namespace App\Http\Middleware;

use App\Models\Tenant;
use Closure;
use Illuminate\Http\Request;
use Symfony\Component\HttpFoundation\Response;

class InitializeRestaurantTenancy
{
    public function handle(Request $request, Closure $next): Response
    {
        if (tenancy()->initialized) {
            return $next($request);
        }

        $host = strtolower($request->getHost());
        $tenant = $this->resolveTenant($host);

        tenancy()->initialize($tenant);

        try {
            return $next($request);
        } finally {
            tenancy()->end();
        }
    }

    private function resolveTenant(string $host): Tenant
    {
        if ($this->isConfiguredLocalHost($host)) {
            $tenantId = trim((string) config('restaurant.local_server.tenant_id'));

            abort_if($tenantId === '', 503, 'Local restaurant tenant is not configured.');

            $tenant = Tenant::query()->find($tenantId);

            abort_unless($tenant, 503, 'Configured local restaurant tenant was not found.');

            return $tenant;
        }

        $centralDomains = array_map(
            static fn (string $domain): string => strtolower(trim($domain)),
            config('tenancy.central_domains', []),
        );

        abort_if(in_array($host, $centralDomains, true), 404);

        $tenant = Tenant::query()
            ->whereHas('domains', fn ($query) => $query->where('domain', $host))
            ->first();

        abort_unless($tenant, 404);

        return $tenant;
    }

    private function isConfiguredLocalHost(string $host): bool
    {
        if (! config('restaurant.local_server.enabled')) {
            return false;
        }

        $allowedHosts = array_map(
            static fn (string $value): string => strtolower(trim($value)),
            config('restaurant.local_server.allowed_hosts', []),
        );

        return in_array($host, $allowedHosts, true);
    }
}
