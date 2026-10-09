<?php

namespace Database\Seeders;

use App\Models\KitchenStation;
use App\Models\MenuCategory;
use App\Models\MenuItem;
use App\Models\MenuItemKitchenRoute;
use App\Models\RestaurantBranch;
use Illuminate\Database\Seeder;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;
use Illuminate\Support\Facades\Storage;
use RuntimeException;

class AfghanMenuSeeder extends Seeder
{
    public function run(): void
    {
        if (! tenancy()->initialized) {
            throw new RuntimeException('Initialize the intended restaurant tenant before seeding the Afghan menu.');
        }

        if (! Schema::connection('tenant')->hasColumn('menu_items', 'name_dari') ||
            ! Schema::connection('tenant')->hasColumn('menu_items', 'preparation_time_minutes') ||
            ! Schema::connection('tenant')->hasColumn('menu_categories', 'name_pashto')) {
            throw new RuntimeException('Run the localized-menu tenant migration first.');
        }

        $catalog = json_decode(file_get_contents(database_path('data/afghan_menu.json')), true, 512, JSON_THROW_ON_ERROR);

        DB::connection('tenant')->transaction(function () use ($catalog): void {
            $categoryIds = [];
            foreach ($catalog['categories'] as $category) {
                $model = MenuCategory::query()->where('name', $category['name'])->first();
                if (! $model) {
                    $model = new MenuCategory;
                    $model->forceFill([
                        'name' => $category['name'],
                        'name_dari' => $category['name_dari'],
                        'name_pashto' => $category['name_pashto'],
                        'sort_order' => $category['sort_order'],
                        'is_active' => true,
                    ])->save();
                }

                $categoryIds[$category['name']] = $model->getKey();
            }

            $itemIds = [];
            foreach ($catalog['items'] as $index => $item) {
                $model = MenuItem::query()->where('sku', $item['sku'])->first();
                if ($model && $model->name !== $item['name']) {
                    throw new RuntimeException('Sample SKU conflicts with another menu item: '.$item['sku']);
                }

                if (! $model) {
                    $model = new MenuItem;
                    $image = $item['image_path'] ?? null;
                    $model->forceFill([
                        'menu_category_id' => $categoryIds[$item['category']],
                        'sku' => $item['sku'],
                        'name' => $item['name'],
                        'name_dari' => $item['name_dari'],
                        'name_pashto' => $item['name_pashto'],
                        'description' => $item['description'],
                        'image_path' => $image && Storage::disk('public')->exists($image) ? $image : null,
                        'price' => $item['price_afn'],
                        'preparation_time_minutes' => $item['preparation_time_minutes'],
                        'is_available' => $item['is_available'],
                        'sort_order' => $index,
                    ])->save();
                } elseif (! $model->image_path &&
                    ! empty($item['image_path']) &&
                    Storage::disk('public')->exists($item['image_path'])) {
                    $model->forceFill(['image_path' => $item['image_path']])->save();
                }

                $itemIds[$item['sku']] = $model->getKey();
            }

            $stations = [
                'MAIN' => 'Main Kitchen',
                'GRILL' => 'Grill Station',
                'SIDES' => 'Bread & Sides',
                'DESSERT' => 'Dessert Station',
                'DRINKS' => 'Beverage Station',
            ];

            foreach (RestaurantBranch::query()->get() as $branch) {
                $stationIds = [];
                foreach ($stations as $code => $name) {
                    $station = KitchenStation::query()
                        ->where('branch_id', $branch->id)
                        ->where('code', $code)
                        ->first();

                    if (! $station) {
                        $station = KitchenStation::query()->create([
                            'branch_id' => $branch->id,
                            'code' => $code,
                            'name' => $name,
                            'sort_order' => array_search($code, array_keys($stations), true),
                            'is_active' => true,
                        ]);
                    }

                    $stationIds[$code] = $station->getKey();
                }

                foreach ($catalog['items'] as $item) {
                    MenuItemKitchenRoute::query()->firstOrCreate([
                        'menu_item_id' => $itemIds[$item['sku']],
                        'branch_id' => $branch->id,
                    ], [
                        'kitchen_station_id' => $stationIds[$item['station_code']],
                    ]);
                }
            }
        });

        $this->command?->info('Afghan menu seeding complete: 7 categories and 60 sample dishes (existing entries preserved).');
    }
}
