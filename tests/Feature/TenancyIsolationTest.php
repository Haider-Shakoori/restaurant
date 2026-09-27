<?php

namespace Tests\Feature;

use App\Models\Tenant;
use Illuminate\Contracts\Queue\ShouldQueue;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Cache;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\File;
use Illuminate\Support\Facades\Hash;
use Illuminate\Support\Facades\Queue;
use Illuminate\Support\Facades\Schema;
use Illuminate\Support\Facades\Storage;
use Illuminate\Support\Str;
use Tests\TestCase;

class TenancyIsolationTest extends TestCase
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

    public function test_central_schema_does_not_contain_tenant_operational_users(): void
    {
        $this->assertTrue(Schema::hasTable('admin_users'));
        $this->assertTrue(Schema::hasTable('tenants'));
        $this->assertTrue(Schema::hasTable('domains'));
        $this->assertFalse(Schema::hasTable('users'));
    }

    public function test_tenant_creation_builds_an_isolated_operational_database(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');

        tenancy()->initialize($tenant);

        $this->assertSame('restaurant-a', tenant('id'));
        $this->assertSame($tenant->database()->getName(), basename((string) DB::connection()->getDatabaseName()));
        $this->assertTrue(Schema::hasTable('users'));
        $this->assertTrue(Schema::hasTable('sessions'));
        $this->assertTrue(Schema::hasTable('cache'));
        $this->assertTrue(Schema::hasTable('personal_access_tokens'));
        $this->assertFalse(Schema::hasTable('admin_users'));

        tenancy()->end();

        $this->assertTrue(Schema::hasTable('admin_users'));
        $this->assertFalse(Schema::hasTable('users'));
    }

    public function test_database_cache_and_files_are_isolated_between_restaurants(): void
    {
        $restaurantA = $this->createTenant('restaurant-a', 'a.test');
        $restaurantB = $this->createTenant('restaurant-b', 'b.test');

        tenancy()->initialize($restaurantA);

        DB::table('users')->insert([
            'public_id' => (string) Str::ulid(),
            'name' => 'Restaurant A Waiter',
            'email' => 'waiter@a.test',
            'password' => Hash::make('secret'),
            'is_active' => true,
            'created_at' => now(),
            'updated_at' => now(),
        ]);
        Cache::put('isolation-check', 'restaurant-a', 60);
        Storage::disk('local')->put('isolation.txt', 'restaurant-a');
        DB::table('sessions')->insert([
            'id' => 'session-a',
            'user_id' => null,
            'ip_address' => '127.0.0.1',
            'user_agent' => 'test',
            'payload' => 'restaurant-a',
            'last_activity' => now()->timestamp,
        ]);

        tenancy()->end();

        tenancy()->initialize($restaurantB);

        $this->assertSame(0, DB::table('users')->where('email', 'waiter@a.test')->count());
        $this->assertNull(Cache::get('isolation-check'));
        $this->assertFalse(Storage::disk('local')->exists('isolation.txt'));
        $this->assertFalse(DB::table('sessions')->where('id', 'session-a')->exists());

        DB::table('users')->insert([
            'public_id' => (string) Str::ulid(),
            'name' => 'Restaurant B Waiter',
            'email' => 'waiter@b.test',
            'password' => Hash::make('secret'),
            'is_active' => true,
            'created_at' => now(),
            'updated_at' => now(),
        ]);
        Cache::put('isolation-check', 'restaurant-b', 60);
        Storage::disk('local')->put('isolation.txt', 'restaurant-b');

        tenancy()->end();

        tenancy()->initialize($restaurantA);

        $this->assertSame(1, DB::table('users')->where('email', 'waiter@a.test')->count());
        $this->assertSame(0, DB::table('users')->where('email', 'waiter@b.test')->count());
        $this->assertSame('restaurant-a', Cache::get('isolation-check'));
        $this->assertSame('restaurant-a', Storage::disk('local')->get('isolation.txt'));
        $this->assertTrue(DB::table('sessions')->where('id', 'session-a')->exists());
    }

    public function test_tenant_queue_payload_keeps_the_restaurant_context(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');

        config([
            'queue.default' => 'database',
            'queue.connections.database.connection' => config('tenancy.database.central_connection'),
        ]);

        tenancy()->initialize($tenant);
        Queue::connection('database')->push(new TenantQueueProbe);
        tenancy()->end();

        $payload = DB::connection(config('tenancy.database.central_connection'))
            ->table('jobs')
            ->value('payload');

        $this->assertNotNull($payload);
        $this->assertSame('restaurant-a', json_decode($payload, true, flags: JSON_THROW_ON_ERROR)['tenant_id']);
    }

    public function test_deleting_tenant_metadata_does_not_delete_operational_database(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');
        $database = database_path($tenant->database()->getName());

        $this->assertFileExists($database);

        $tenant->delete();

        $this->assertFileExists($database);
    }

    public function test_domain_identification_selects_the_correct_restaurant(): void
    {
        $tenant = $this->createTenant('restaurant-a', 'a.test');

        $this->getJson('http://a.test/api/v1/health')
            ->assertOk()
            ->assertJsonPath('service', 'BusinessOS Restaurant Tenant')
            ->assertJsonPath('tenant_id', 'restaurant-a');

        $this->getJson('http://localhost/api/v1/health')
            ->assertOk()
            ->assertJsonPath('service', 'BusinessOS Restaurant');
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

class TenantQueueProbe implements ShouldQueue
{
    public function handle(): void
    {
        //
    }
}
