<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\TenantRole;
use App\Services\Tenant\TenantRoleService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class TenantDesktopRolesController extends Controller
{
    public function __construct(
        private readonly TenantRoleService $roles,
    ) {}

    public function index(): JsonResponse
    {
        return response()->json([
            'data' => $this->roles->rolesPayload(),
            'catalog' => $this->roles->catalogPayload(),
        ]);
    }

    public function permissions(): JsonResponse
    {
        return response()->json(['data' => $this->roles->catalogPayload()]);
    }

    public function store(Request $request): JsonResponse
    {
        $data = $request->validate([
            'name' => ['required', 'string', 'max:100'],
            'description' => ['nullable', 'string', 'max:400'],
            'permissions' => ['sometimes', 'array'],
            'permissions.*' => ['string', 'max:100'],
        ]);

        $role = $this->roles->create($data, $request->user());

        return response()->json(['data' => $this->roles->toPayload($role)], 201);
    }

    public function show(string $role): JsonResponse
    {
        $role = TenantRole::query()->findOrFail($role);

        return response()->json(['data' => $this->roles->toPayload($role)]);
    }

    public function update(Request $request, string $role): JsonResponse
    {
        $role = TenantRole::query()->findOrFail($role);
        $data = $request->validate([
            'name' => ['sometimes', 'required', 'string', 'max:100'],
            'description' => ['sometimes', 'nullable', 'string', 'max:400'],
            'permissions' => ['sometimes', 'array'],
            'permissions.*' => ['string', 'max:100'],
        ]);

        $role = $this->roles->update($role, $data, $request->user());

        return response()->json(['data' => $this->roles->toPayload($role)]);
    }

    public function destroy(Request $request, string $role): JsonResponse
    {
        $role = TenantRole::query()->findOrFail($role);
        $this->roles->delete($role, $request->user());

        return response()->json(['data' => ['id' => $role->id, 'key' => $role->key, 'deleted' => true]]);
    }
}
