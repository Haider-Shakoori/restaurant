<?php

namespace Tests\Feature;

use App\Models\MenuCategory;
use App\Models\MenuItem;
use App\Models\RestaurantBranch;
use App\Models\Tenant;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\File;
use Illuminate\Support\Facades\Storage;
use Illuminate\Support\Str;
use Tests\TestCase;
use ZipArchive;

class AfghanMenuSeederTest extends TestCase
{
    use RefreshDatabase;

    private array $tenantDatabases = [];

    private array $tenantStoragePaths = [];

    private array $temporaryArchives = [];

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

        foreach ($this->temporaryArchives as $archive) {
            @unlink($archive);
        }

        parent::tearDown();
    }

    public function test_installer_seeds_a_single_tenant_and_is_idempotent(): void
    {
        $tenant = $this->createTenant('afghan-menu.test');
        tenancy()->initialize($tenant);
        RestaurantBranch::query()->create(['code' => 'MAIN', 'name' => 'Main Branch']);
        tenancy()->end();

        $archive = $this->createSampleArchive();
        $params = ['--tenant' => $tenant->id, '--archive' => $archive];

        $this->artisan('restaurant:seed-afghan-menu', [...$params, '--dry-run' => true])->assertExitCode(0);

        tenancy()->initialize($tenant);
        $this->assertSame(0, MenuItem::query()->count());
        tenancy()->end();

        $this->artisan('restaurant:seed-afghan-menu', $params)->assertExitCode(0);

        tenancy()->initialize($tenant);
        $this->assertSame(7, MenuCategory::query()->count());
        $this->assertSame(60, MenuItem::query()->count());
        $this->assertSame(5, DB::connection('tenant')->table('kitchen_stations')->count());
        $this->assertSame(60, DB::connection('tenant')->table('menu_item_kitchen_routes')->count());

        $kabuli = MenuItem::query()->where('sku', 'AFG-001')->firstOrFail();
        $this->assertSame('400.00', $kabuli->price);
        $this->assertSame('قابلی پلو', $kabuli->name_dari);
        $this->assertSame(25, $kabuli->preparation_time_minutes);
        $this->assertSame('menu-items/afghan-sample/kabuli-pulao.webp', $kabuli->image_path);
        $this->assertTrue(Storage::disk('public')->exists($kabuli->image_path));
        $kabuli->update(['price' => 555]);
        tenancy()->end();

        $this->artisan('restaurant:seed-afghan-menu', $params)->assertExitCode(0);
        tenancy()->initialize($tenant);

        $this->assertSame(60, MenuItem::query()->count());
        $this->assertSame(60, DB::connection('tenant')->table('menu_item_kitchen_routes')->count());
        $this->assertSame('555.00', MenuItem::query()->where('sku', 'AFG-001')->firstOrFail()->price);
        $this->assertNull(MenuItem::query()->where('sku', 'AFG-002')->firstOrFail()->image_path);
    }

    public function test_missing_tenant_and_missing_archive_do_not_create_menu_data(): void
    {
        $this->artisan('restaurant:seed-afghan-menu', [
            '--tenant' => 'does-not-exist',
            '--archive' => '/tmp/missing-afghan-menu.zip',
        ])->assertExitCode(1);
    }

    private function createSampleArchive(): string
    {
        $path = tempnam(sys_get_temp_dir(), 'afghan-menu-');
        $this->temporaryArchives[] = $path;
        $zip = new ZipArchive;
        $this->assertTrue($zip->open($path, ZipArchive::OVERWRITE) === true);

        $catalog = json_decode(file_get_contents(database_path('data/afghan_menu.json')), true, 512, JSON_THROW_ON_ERROR);
        foreach ($catalog['items'] as $item) {
            if ($item['image_path']) {
                $zip->addFromString(
                    'storage/app/public/menu-items/'.basename($item['image_path']),
                    'RIFF'.pack('V', 8).'WEBP'.str_repeat('x', 8),
                );
            }
        }

        $this->assertTrue($zip->close());

        return $path;
    }

    private function createTenant(string $domain): Tenant
    {
        $id = 'sample-'.Str::lower(Str::random(12));
        $path = storage_path(config('tenancy.filesystem.suffix_base').$id);
        File::deleteDirectory($path);
        $this->tenantStoragePaths[] = $path;

        if (config('database.connections.'.config('tenancy.database.central_connection').'.driver') === 'sqlite') {
            @unlink(database_path(config('tenancy.database.prefix').$id.config('tenancy.database.suffix')));
        }

        $tenant = Tenant::create(['id' => $id, 'provisioning_state' => 'ready']);
        $tenant->domains()->create(['domain' => $domain]);
        $this->tenantDatabases[] = $tenant->database()->getName();

        return $tenant;
    }
}
