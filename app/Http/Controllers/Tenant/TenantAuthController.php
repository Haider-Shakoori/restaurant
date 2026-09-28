<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\TenantLoginRequest;
use App\Models\TenantUser;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Hash;
use Illuminate\Validation\ValidationException;

class TenantAuthController extends Controller
{
    public function login(TenantLoginRequest $request): JsonResponse
    {
        $user = TenantUser::query()
            ->where('email', $request->string('email')->lower()->toString())
            ->first();

        if (! $user || ! $user->is_active || ! Hash::check($request->string('password')->toString(), $user->password)) {
            throw ValidationException::withMessages([
                'email' => 'The provided credentials are invalid.',
            ]);
        }

        $token = $user->createToken(
            $request->input('device_name', 'restaurant-client'),
            ['restaurant:operate'],
        );

        return response()->json([
            'token_type' => 'Bearer',
            'access_token' => $token->plainTextToken,
            'user' => [
                'id' => $user->id,
                'public_id' => $user->public_id,
                'name' => $user->name,
                'email' => $user->email,
                'role' => $user->role,
            ],
            'tenant_id' => tenant('id'),
        ]);
    }

    public function logout(Request $request): JsonResponse
    {
        $request->user()?->currentAccessToken()?->delete();

        return response()->json(['status' => 'logged_out']);
    }
}
