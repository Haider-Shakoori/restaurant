<?php

namespace Tests\Feature;

use App\Models\MenuItem;
use App\Models\Tenant;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\File;
use Illuminate\Support\Facades\Storage;
use Illuminate\Support\Str;
use Tests\TestCase;

class MenuItemImageServingTest extends TestCase
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

    public function test_public_menu_image_resolves_its_tenant_item_and_returns_image_content(): void
    {
        $tenant = $this->createTenant('images-a.test');
        tenancy()->initialize($tenant);

        $menuItem = MenuItem::query()->create([
            'name' => 'Kabuli Pulao',
            'price' => '250.00',
            'is_available' => true,
            'sort_order' => 0,
            'image_path' => 'menu-items/kabuli.png',
        ]);

        $image = base64_decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMBAWcXev8AAAAASUVORK5CYII=', true);
        $this->assertIsString($image);
        Storage::disk('public')->put($menuItem->image_path, $image);
        $this->assertTrue(Storage::disk('public')->exists($menuItem->image_path));
        tenancy()->end();

        $response = $this->get('http://images-a.test/media/menu-items/'.$menuItem->id);

        $response->assertOk();
        $this->assertStringStartsWith('image/png', (string) $response->headers->get('Content-Type'));
        $this->assertSame($image, $response->streamedContent());
        $this->assertFalse(tenancy()->initialized);
    }

    public function test_menu_image_does_not_leak_across_tenants_and_missing_files_404(): void
    {
        $first = $this->createTenant('images-owner.test');
        $second = $this->createTenant('images-other.test');

        tenancy()->initialize($first);
        $menuItem = MenuItem::query()->create([
            'name' => 'Mantu',
            'price' => '180.00',
            'is_available' => true,
            'sort_order' => 0,
            'image_path' => 'menu-items/missing.png',
        ]);
        tenancy()->end();

        $this->get('http://images-owner.test/media/menu-items/'.$menuItem->id)
            ->assertNotFound();

        $this->get('http://images-other.test/media/menu-items/'.$menuItem->id)
            ->assertNotFound();
    }

    private function createTenant(string $domain): Tenant
    {
        $id = 'image-'.Str::lower(Str::random(12));
        $storagePath = storage_path(config('tenancy.filesystem.suffix_base').$id);
        File::deleteDirectory($storagePath);
        $this->tenantStoragePaths[] = $storagePath;

        if (config('database.connections.'.config('tenancy.database.central_connection').'.driver') === 'sqlite') {
            @unlink(database_path(config('tenancy.database.prefix').$id.config('tenancy.database.suffix')));
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
