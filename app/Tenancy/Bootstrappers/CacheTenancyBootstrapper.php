<?php

namespace App\Tenancy\Bootstrappers;

use Illuminate\Cache\CacheManager;
use Illuminate\Contracts\Foundation\Application;
use Illuminate\Support\Facades\Cache;
use Stancl\Tenancy\Contracts\TenancyBootstrapper;
use Stancl\Tenancy\Contracts\Tenant;

class CacheTenancyBootstrapper implements TenancyBootstrapper
{
    private string $originalPrefix;

    private ?string $originalDatabaseConnection;

    public function __construct(
        private readonly Application $app,
        private readonly CacheManager $cache,
    ) {
        $this->originalPrefix = (string) config('cache.prefix', '');
        $this->originalDatabaseConnection = config('cache.stores.database.connection');
    }

    public function bootstrap(Tenant $tenant): void
    {
        config([
            'cache.prefix' => $this->originalPrefix.'tenant_'.$tenant->getTenantKey().'_',
            'cache.stores.database.connection' => 'tenant',
        ]);

        $this->resetResolvedStores();
    }

    public function revert(): void
    {
        config([
            'cache.prefix' => $this->originalPrefix,
            'cache.stores.database.connection' => $this->originalDatabaseConnection,
        ]);

        $this->resetResolvedStores();
    }

    private function resetResolvedStores(): void
    {
        $this->cache->forgetDriver();
        Cache::clearResolvedInstances();
    }
}
