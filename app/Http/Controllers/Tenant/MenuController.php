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
                'items' => $category->items->map(fn ($item) => [
                    'id' => $item->id,
                    'sku' => $item->sku,
                    'name' => $item->name,
                    'description' => $item->description,
                    'price' => $item->price,
                    'currency' => 'AFN',
                ]),
            ]);

        return response()->json(['data' => $categories]);
    }
}
