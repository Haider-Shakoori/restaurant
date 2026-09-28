<?php

namespace Tests\Feature;

use App\Models\Tenant;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\File;
use Tests\TestCase;

class LocalLanTenancyTest extends TestCase
{
    use RefreshDatabase;

    /** @var array<int, string> */
    private array $tenantDatabases = [];

    /** @var array<int, string> */
    private array $tenantStoragePaths = [];

    protected function tearDown(): void
    {
        if (tenancy()->initialized) {
            tenancy()->end();
        }

        foreach ($this->tenantDatabases as $database) {
            @unlink(database_path($database));
        }

        foreach ($this->tenantStoragePaths as $path) {
            File::deleteDirectory($path);
        }

        parent::tearDown();
    }

    public function test_configured_lan_ip_resolves_the_single_local_restaurant_tenant(): void
    {
        $tenant = $this->createTenant('local-restaurant', 'restaurant.example.test');

        config([
            'restaurant.local_server.enabled' => true,
            'restaurant.local_server.tenant_id' => $tenant->id,
            'restaurant.local_server.allowed_hosts' => ['192.168.50.10', 'restaurant.local'],
        ]);

        $this->getJson('http://192.168.50.10/api/v1/health')
            ->assertOk()
            ->assertJsonPath('status', 'ok')
            ->assertJsonPath('tenant_id', $tenant->id);
    }

    public function test_domain_tenancy_still_works_when_local_mode_is_enabled(): void
    {
        $tenant = $this->createTenant('cloud-restaurant', 'cloud-restaurant.test');

        config([
            'restaurant.local_server.enabled' => true,
            'restaurant.local_server.tenant_id' => $tenant->id,
            'restaurant.local_server.allowed_hosts' => ['192.168.50.10'],
        ]);

        $this->getJson('http://cloud-restaurant.test/api/v1/health')
            ->assertOk()
            ->assertJsonPath('tenant_id', $tenant->id);
    }

    public function test_unlisted_lan_ip_does_not_gain_local_tenant_access(): void
    {
        $tenant = $this->createTenant('local-restaurant', 'restaurant.example.test');

        config([
            'restaurant.local_server.enabled' => true,
            'restaurant.local_server.tenant_id' => $tenant->id,
            'restaurant.local_server.allowed_hosts' => ['192.168.50.10'],
        ]);

        $this->getJson('http://192.168.50.11/api/v1/health')
            ->assertNotFound();
    }

    private function createTenant(string $id, string $domain): Tenant
    {
        $tenantStoragePath = storage_path(config('tenancy.filesystem.suffix_base').$id);
        File::deleteDirectory($tenantStoragePath);
        $this->tenantStoragePaths[] = $tenantStoragePath;

        if (config('database.connections.'.config('tenancy.database.central_connection').'.driver') === 'sqlite') {
            @unlink(database_path(
                config('tenancy.database.prefix').$id.config('tenancy.database.suffix')
            ));
        }

        $tenant = Tenant::create([
            'id' => $id,
            'provisioning_state' => 'ready',
        ]);

        $tenant->domains()->create(['domain' => $domain]);
        $this->tenantDatabases[] = $tenant->database()->getName();

        return $tenant;
    }
}
