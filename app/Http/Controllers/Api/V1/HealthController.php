<?php

namespace App\Http\Controllers\Api\V1;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;

final class HealthController extends Controller
{
    public function __invoke(): JsonResponse
    {
        return response()->json([
            'status' => 'ok',
            'service' => 'BusinessOS Restaurant',
            'api' => config('restaurant.api.version'),
            'release' => config('restaurant.release'),
            'server_time' => now()->toIso8601String(),
        ]);
    }
}
