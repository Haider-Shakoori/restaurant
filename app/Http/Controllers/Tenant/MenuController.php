<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\MenuCategory;
use Illuminate\Http\JsonResponse;

class MenuController extends Controller
{
    public function index(): JsonResponse
    {
        $categories = MenuCategory::query()
            ->where('is_active', true)
            ->with(['items' => fn ($query) => $query
                ->where('is_available', true)
                ->orderBy('sort_order')
                ->orderBy('name')])
            ->orderBy('sort_order')
            ->orderBy('name')
            ->get()
            ->map(fn (MenuCategory $category) => [
                'id' => $category->id,
                'name' => $category->name,
                'name_dari' => $category->name_dari,
                'name_pashto' => $category->name_pashto,
                'items' => $category->items->map(fn ($item) => [
                    'id' => $item->id,
                    'sku' => $item->sku,
                    'name' => $item->name,
                    'name_dari' => $item->name_dari,
                    'name_pashto' => $item->name_pashto,
                    'preparation_time_minutes' => $item->preparation_time_minutes,
                    'description' => $item->description,
                    'image_url' => $item->image_url,
                    'price' => $item->price,
                    'currency' => 'AFN',
                ]),
            ]);

        return response()->json(['data' => $categories]);
    }
}
