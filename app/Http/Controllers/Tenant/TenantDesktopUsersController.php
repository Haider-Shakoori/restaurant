<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\TenantUser;
use App\Services\Tenant\TenantRoleService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Str;
use Illuminate\Validation\Rule;
use Illuminate\Validation\ValidationException;

class TenantDesktopUsersController extends Controller
{
    public function __construct(
        private readonly TenantRoleService $roles,
    ) {}

    public function index(): JsonResponse
    {
        $roleNames = $this->roles->roleNamesByKey();

        $data = TenantUser::query()
            ->orderBy('name')
            ->get(['id', 'name', 'email', 'phone', 'role', 'is_active'])
            ->map(fn (TenantUser $user): array => [
                ...$user->only(['id', 'name', 'email', 'phone', 'role', 'is_active']),
                'role_name' => $roleNames[$user->role] ?? Str::headline($user->role),
            ]);

        return response()->json(['data' => $data]);
    }

    public function store(Request $request): JsonResponse
    {
        $data = $request->validate([
            'name' => ['required', 'string', 'max:255'],
            'email' => ['required', 'email', 'max:255', Rule::unique('users', 'email')],
            'phone' => ['nullable', 'string', 'max:50'],
            'role' => ['required', Rule::in($this->roles->assignableRoleKeys())],
            'password' => ['required', 'string', 'min:8', 'max:255'],
        ]);

        $this->roles->assertCanAssignRole($request->user(), $data['role']);

        $user = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => trim($data['name']),
            'email' => Str::lower(trim($data['email'])),
            'phone' => $data['phone'] ?? null,
            'role' => $data['role'],
            'password' => $data['password'],
            'is_active' => true,
        ]);

        return response()->json(['data' => $this->userPayload($user)], 201);
    }

    public function update(Request $request, string $user): JsonResponse
    {
        $user = TenantUser::query()->findOrFail($user);
        $data = $request->validate([
            'name' => ['required', 'string', 'max:255'],
            'email' => ['required', 'email', 'max:255', Rule::unique('users', 'email')->ignore($user->id)],
            'phone' => ['nullable', 'string', 'max:50'],
            'role' => ['required', Rule::in($this->roles->assignableRoleKeys())],
            'is_active' => ['required', 'boolean'],
            'password' => ['nullable', 'string', 'min:8', 'max:255'],
        ]);

        $actor = $request->user();
        $this->roles->assertCanManageUser($actor, $user);
        $this->roles->assertCanAssignRole($actor, $data['role']);

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

        return response()->json(['data' => $this->userPayload($user)]);
    }

    /**
     * @return array<string, mixed>
     */
    private function userPayload(TenantUser $user): array
    {
        return [
            ...$user->only(['id', 'name', 'email', 'phone', 'role', 'is_active']),
            'role_name' => $this->roles->roleNameFor($user->role),
        ];
    }
}
