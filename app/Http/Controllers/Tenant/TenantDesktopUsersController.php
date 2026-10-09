<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\TenantUser;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Str;
use Illuminate\Validation\Rule;
use Illuminate\Validation\ValidationException;

class TenantDesktopUsersController extends Controller
{
    private const ROLES = ['owner', 'admin', 'manager', 'cashier', 'waiter', 'kitchen', 'inventory'];

    public function index(): JsonResponse
    {
        return response()->json(['data' => TenantUser::query()
            ->orderBy('name')
            ->get(['id', 'name', 'email', 'phone', 'role', 'is_active'])]);
    }

    public function store(Request $request): JsonResponse
    {
        $data = $request->validate([
            'name' => ['required', 'string', 'max:255'],
            'email' => ['required', 'email', 'max:255', Rule::unique('users', 'email')],
            'phone' => ['nullable', 'string', 'max:50'],
            'role' => ['required', Rule::in(self::ROLES)],
            'password' => ['required', 'string', 'min:8', 'max:255'],
        ]);

        $user = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => trim($data['name']),
            'email' => Str::lower(trim($data['email'])),
            'phone' => $data['phone'] ?? null,
            'role' => $data['role'],
            'password' => $data['password'],
            'is_active' => true,
        ]);

        return response()->json(['data' => $user->only(['id', 'name', 'email', 'phone', 'role', 'is_active'])], 201);
    }

    public function update(Request $request, string $user): JsonResponse
    {
        $user = TenantUser::query()->findOrFail($user);
        $data = $request->validate([
            'name' => ['required', 'string', 'max:255'],
            'email' => ['required', 'email', 'max:255', Rule::unique('users', 'email')->ignore($user->id)],
            'phone' => ['nullable', 'string', 'max:50'],
            'role' => ['required', Rule::in(self::ROLES)],
            'is_active' => ['required', 'boolean'],
            'password' => ['nullable', 'string', 'min:8', 'max:255'],
        ]);

        $actor = $request->user();
        if ((int) $actor->id === (int) $user->id &&
            ($data['role'] !== $user->role || ! (bool) $data['is_active'])) {
            throw ValidationException::withMessages(['role' => 'You cannot change your own role or disable your own account.']);
        }

        if ($user->role === 'owner' && ($data['role'] !== 'owner' || ! (bool) $data['is_active']) &&
            TenantUser::query()->where('role', 'owner')->where('is_active', true)->count() <= 1) {
            throw ValidationException::withMessages(['role' => 'The last active restaurant owner cannot be removed.']);
        }

        $previousRole = $user->role;
        $user->fill([
            'name' => trim($data['name']),
            'email' => Str::lower(trim($data['email'])),
            'phone' => $data['phone'] ?? null,
            'role' => $data['role'],
            'is_active' => (bool) $data['is_active'],
        ]);

        if (! empty($data['password'])) {
            $user->password = $data['password'];
        }
        $user->save();

        if (! $user->is_active || ! empty($data['password']) || $previousRole !== $user->role) {
            $user->tokens()->delete();
        }

        return response()->json(['data' => $user->only(['id', 'name', 'email', 'phone', 'role', 'is_active'])]);
    }
}
