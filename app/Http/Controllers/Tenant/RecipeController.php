<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\StoreRecipeRequest;
use App\Models\MenuItem;
use App\Models\Recipe;
use App\Models\RestaurantBranch;
use App\Services\Tenant\RecipeService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class RecipeController extends Controller
{
    public function index(Request $request): JsonResponse
    {
        $recipes = Recipe::query()
            ->with(['branch', 'menuItem', 'items.inventoryItem'])
            ->when(
                $request->filled('branch_id'),
                fn ($query) => $query->where('branch_id', $request->string('branch_id')->toString()),
            )
            ->when(
                $request->filled('menu_item_id'),
                fn ($query) => $query->where('menu_item_id', $request->string('menu_item_id')->toString()),
            )
            ->orderByDesc('is_active')
            ->latest('version')
            ->limit(200)
            ->get();

        return response()->json(['data' => $recipes]);
    }

    public function store(
        StoreRecipeRequest $request,
        MenuItem $menuItem,
        RecipeService $recipes,
    ): JsonResponse {
        $branch = RestaurantBranch::query()->findOrFail(
            $request->string('branch_id')->toString()
        );

        return response()->json([
            'data' => $recipes->createVersion(
                $branch,
                $menuItem,
                $request->validated(),
            ),
        ], 201);
    }
}
