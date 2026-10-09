<?php

namespace App\Console\Commands;

use App\Models\MenuItem;
use App\Models\Tenant;
use Illuminate\Console\Command;
use Illuminate\Support\Facades\Schema;
use Illuminate\Support\Facades\Storage;
use JsonException;
use RuntimeException;
use ZipArchive;

class SeedAfghanMenu extends Command
{
    protected $signature = 'restaurant:seed-afghan-menu
        {--tenant= : Exact existing tenant ID}
        {--archive= : Absolute path to the cumulative food-photo ZIP}
        {--dry-run : Validate only; do not write}';

    protected $description = 'Install the Afghan sample menu and photos into exactly one restaurant tenant';

    public function handle(): int
    {
        $id = trim((string) $this->option('tenant'));
        $path = trim((string) $this->option('archive'));
        if ($id === '' || $path === '') {
            $this->error('Specify both --tenant and --archive. Bulk tenant installation is not supported.');

            return self::FAILURE;
        }

        $tenant = Tenant::query()->find($id);
        if (! $tenant || ! is_file($path) || ! is_readable($path)) {
            $this->error('The specified tenant or readable archive was not found.');

            return self::FAILURE;
        }

        $zip = new ZipArchive;
        if ($zip->open($path, ZipArchive::RDONLY) !== true) {
            $this->error('The archive is invalid.');

            return self::FAILURE;
        }

        try {
            $catalog = json_decode(file_get_contents(database_path('data/afghan_menu.json')), true, 512, JSON_THROW_ON_ERROR);
            $photos = [];
            foreach ($catalog['items'] as $item) {
                if (empty($item['image_path'])) {
                    continue;
                }

                $imagePath = (string) $item['image_path'];
                if (! preg_match('~^menu-items/afghan-sample/[a-z0-9-]+\\.webp$~', $imagePath)) {
                    throw new RuntimeException('Invalid sample image destination: '.$imagePath);
                }

                // Never extract untrusted ZIP paths. Read only catalog-declared filenames.
                $zipEntry = 'storage/app/public/menu-items/'.basename($imagePath);
                $contents = $zip->getFromName($zipEntry);
                if (! is_string($contents) || strlen($contents) < 16 ||
                    substr($contents, 0, 4) !== 'RIFF' || substr($contents, 8, 4) !== 'WEBP') {
                    throw new RuntimeException('Missing or invalid image in ZIP: '.$zipEntry);
                }

                $photos[$imagePath] = $contents;
            }

            $this->info('Validated '.count($catalog['items']).' dishes and '.count($photos).' photos for tenant '.$id.'.');
            if ($this->option('dry-run')) {
                return self::SUCCESS;
            }

            tenancy()->initialize($tenant);
            try {
                if (! Schema::connection('tenant')->hasColumn('menu_items', 'name_dari') ||
                    ! Schema::connection('tenant')->hasColumn('menu_categories', 'name_pashto')) {
                    throw new RuntimeException('Run tenants:migrate for this tenant first.');
                }

                foreach ($catalog['items'] as $item) {
                    $existing = MenuItem::query()->where('sku', $item['sku'])->first();
                    if ($existing && $existing->name !== $item['name']) {
                        throw new RuntimeException('Sample SKU conflicts with existing dish: '.$item['sku']);
                    }
                }

                $disk = Storage::disk('public');
                foreach ($photos as $photoPath => $bytes) {
                    if ($disk->exists($photoPath) && hash('sha256', $disk->get($photoPath)) !== hash('sha256', $bytes)) {
                        throw new RuntimeException('Refusing to overwrite custom image: '.$photoPath);
                    }
                }

                foreach ($photos as $photoPath => $bytes) {
                    if (! $disk->exists($photoPath) && ! $disk->put($photoPath, $bytes)) {
                        throw new RuntimeException('Unable to write tenant image: '.$photoPath);
                    }
                }

                if ($this->call('db:seed', [
                    '--class' => 'Database\\Seeders\\AfghanMenuSeeder',
                    '--force' => true,
                ]) !== self::SUCCESS) {
                    throw new RuntimeException('Tenant menu seeder failed.');
                }

                $this->info('Installed Afghan sample menu for tenant '.$id.'.');
            } finally {
                tenancy()->end();
            }

            return self::SUCCESS;
        } catch (RuntimeException|JsonException $e) {
            $this->error($e->getMessage());

            return self::FAILURE;
        } finally {
            $zip->close();
        }
    }
}
