<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class BootstrapController extends Controller
{
    public function __invoke(Request $request): JsonResponse
    {
        $user = $request->user();

        return response()->json([
            'tenant_id' => tenant('id'),
            'user' => [
                'id' => $user->id,
                'public_id' => $user->public_id,
                'name' => $user->name,
                'email' => $user->email,
                'role' => $user->role,
            ],
            'defaults' => [
                'currency' => 'AFN',
                'timezone' => config('app.timezone', 'Asia/Kabul'),
                'locale' => app()->getLocale(),
            ],
            'sync' => [
                'order_ids' => 'client-generated ULID/UUID supported',
                'idempotency' => true,
            ],
        ]);
    }
}
