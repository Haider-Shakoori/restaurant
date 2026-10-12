<?php

namespace App\Services\Tenant;

use App\Models\TenantRole;
use App\Models\TenantUser;
use App\Support\TenantPermissions;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;

/**
 * Tenant-scoped role catalog and permission assignment.
 *
 * Every operation relies on the tenant connection resolved from the current
 * authenticated tenant session; role ids and keys are never trusted from the
 * request to pick a tenant.
 */
class TenantRoleService
{
    /**
     * Idempotently create the system role rows for this tenant. Safe to call
     * on every request: it only inserts missing rows and never overwrites
     * permissions configured by the restaurant.
     */
    public function ensureSystemRoles(): void
    {
        $definitions = TenantPermissions::systemRoles();
        $existing = TenantRole::query()
            ->whereIn('key', array_keys($definitions))
            ->pluck('key')
            ->all();
        $missing = array_diff(array_keys($definitions), $existing);

        if ($missing === []) {
            return;
        }

        DB::connection('tenant')->transaction(function () use ($missing, $definitions): void {
            foreach ($missing as $key) {
                TenantRole::query()->firstOrCreate(
                    ['key' => $key],
                    [
                        'public_id' => (string) Str::ulid(),
                        'name' => $definitions[$key]['name'],
                        'description' => $definitions[$key]['description'],
                        'is_system' => true,
                        'permissions' => TenantPermissions::defaultPermissionsFor($key),
                    ],
                );
            }
        });
    }

    /**
     * @return array<int, array<string, mixed>>
     */
    public function rolesPayload(): array
    {
        $this->ensureSystemRoles();

        $counts = TenantUser::query()
            ->selectRaw('role, count(*) as aggregate')
            ->groupBy('role')
            ->pluck('aggregate', 'role');

        return TenantRole::query()
            ->orderByDesc('is_system')
            ->orderBy('name')
            ->get()
            ->map(fn (TenantRole $role): array => $this->toPayload($role, (int) ($counts[$role->key] ?? 0)))
            ->all();
    }

    /**
     * @return array<string, mixed>
     */
    public function toPayload(TenantRole $role, ?int $userCount = null): array
    {
        $permissions = $this->rolePermissions($role);

        if ($userCount === null) {
            $userCount = TenantUser::query()->where('role', $role->key)->count();
        }

        return [
            'id' => $role->id,
            'key' => $role->key,
            'name' => $role->name,
            'description' => $role->description,
            'is_system' => (bool) $role->is_system,
            'protected' => $role->key === 'owner',
            'user_count' => $userCount,
            'permission_count' => count($permissions),
            'permissions' => $permissions,
        ];
    }

    /**
     * @return array<int, string>
     */
    public function rolePermissions(TenantRole $role): array
    {
        if ($role->key === 'owner') {
            return TenantPermissions::allKeys();
        }

        return array_values(array_intersect(
            array_map('strval', $role->permissions ?? []),
            TenantPermissions::allKeys(),
        ));
    }

    /**
     * Effective permissions for the authenticated tenant user. Unknown roles
     * receive nothing: authorization denies by default.
     *
     * @return array<int, string>
     */
    public function effectivePermissions(TenantUser $user): array
    {
        if ($user->role === 'owner') {
            return TenantPermissions::allKeys();
        }

        $role = TenantRole::query()->where('key', $user->role)->first();

        if ($role === null) {
            $this->ensureSystemRoles();
            $role = TenantRole::query()->where('key', $user->role)->first();
        }

        return $role === null ? [] : $this->rolePermissions($role);
    }

    public function userHasPermission(TenantUser $user, string $permission): bool
    {
        return in_array($permission, $this->effectivePermissions($user), true);
    }

    /**
     * @return array<int, string>
     */
    public function assignableRoleKeys(): array
    {
        $this->ensureSystemRoles();

        return TenantRole::query()->orderBy('name')->pluck('key')->all();
    }

    public function roleNameFor(string $roleKey): string
    {
        $role = TenantRole::query()->where('key', $roleKey)->first();

        return $role?->name ?? Str::headline($roleKey);
    }

    /**
     * @return array<string, string>
     */
    public function roleNamesByKey(): array
    {
        $this->ensureSystemRoles();

        return TenantRole::query()->pluck('name', 'key')->all();
    }

    public function assertCanAssignRole(TenantUser $actor, string $roleKey): void
    {
        if (! in_array($roleKey, $this->assignableRoleKeys(), true)) {
            throw ValidationException::withMessages([
                'role' => 'The selected role is not available for this restaurant.',
            ]);
        }

        if ($roleKey === 'owner' && $actor->role !== 'owner') {
            throw ValidationException::withMessages([
                'role' => 'Only the restaurant owner can assign the Super Admin role.',
            ]);
        }
    }

    public function assertCanManageUser(TenantUser $actor, TenantUser $target): void
    {
        if ($target->role === 'owner' && $actor->role !== 'owner') {
            throw ValidationException::withMessages([
                'role' => 'Only the restaurant owner can change a Super Admin account.',
            ]);
        }
    }

    /**
     * @param  array{name: string, description?: string|null, permissions?: array<int, string>}  $data
     */
    public function create(array $data, TenantUser $actor): TenantRole
    {
        $name = trim($data['name']);
        $this->assertNameIsAvailable($name);
        $permissions = $this->guardPermissionChanges($actor, $data['permissions'] ?? [], []);

        return DB::connection('tenant')->transaction(function () use ($name, $data, $permissions): TenantRole {
            return TenantRole::query()->create([
                'public_id' => (string) Str::ulid(),
                'key' => $this->uniqueKey($name),
                'name' => $name,
                'description' => $this->nullableDescription($data['description'] ?? null),
                'is_system' => false,
                'permissions' => $permissions,
            ]);
        });
    }

    /**
     * @param  array{name?: string, description?: string|null, permissions?: array<int, string>}  $data
     */
    public function update(TenantRole $role, array $data, TenantUser $actor): TenantRole
    {
        if ($role->key === 'owner') {
            throw ValidationException::withMessages([
                'name' => 'The Super Admin role is protected and cannot be modified.',
            ]);
        }

        if ($actor->role !== 'owner' && $role->key === 'admin') {
            throw ValidationException::withMessages([
                'name' => 'Administrator permissions can only be changed by the restaurant owner.',
            ]);
        }

        $name = array_key_exists('name', $data) ? trim((string) $data['name']) : $role->name;
        $this->assertNameIsAvailable($name, $role);

        $current = $this->rolePermissions($role);
        $permissions = array_key_exists('permissions', $data)
            ? $this->guardPermissionChanges($actor, array_values((array) ($data['permissions'] ?? [])), $current)
            : $current;

        return DB::connection('tenant')->transaction(function () use ($role, $data, $name, $permissions): TenantRole {
            $locked = TenantRole::query()->lockForUpdate()->findOrFail($role->getKey());

            if ($locked->key === 'owner') {
                throw ValidationException::withMessages([
                    'name' => 'The Super Admin role is protected and cannot be modified.',
                ]);
            }

            $locked->fill([
                'name' => $name,
                'description' => array_key_exists('description', $data)
                    ? $this->nullableDescription($data['description'])
                    : $locked->description,
                'permissions' => $permissions,
            ])->save();

            return $locked;
        });
    }

    public function delete(TenantRole $role, TenantUser $actor): void
    {
        if ($role->is_system) {
            throw ValidationException::withMessages([
                'role' => 'System roles cannot be deleted. Remove their permissions or reassign users instead.',
            ]);
        }

        DB::connection('tenant')->transaction(function () use ($role): void {
            $locked = TenantRole::query()->lockForUpdate()->findOrFail($role->getKey());

            if ($locked->is_system) {
                throw ValidationException::withMessages([
                    'role' => 'System roles cannot be deleted.',
                ]);
            }

            $assigned = TenantUser::query()->where('role', $locked->key)->count();
            if ($assigned > 0) {
                throw ValidationException::withMessages([
                    'role' => "This role is assigned to {$assigned} user(s). Reassign those users before deleting it.",
                ]);
            }

            $locked->delete();
        });
    }

    /**
     * @return array<string, mixed>
     */
    public function catalogPayload(): array
    {
        $groups = [];

        foreach (TenantPermissions::groups() as $key => $group) {
            $permissions = [];

            foreach ($group['permissions'] as $permission => $label) {
                $permissions[] = [
                    'key' => $permission,
                    'label' => $label,
                    'protected' => TenantPermissions::isProtected($permission),
                ];
            }

            $groups[] = ['key' => $key, 'label' => $group['label'], 'permissions' => $permissions];
        }

        return [
            'groups' => $groups,
            'protected' => TenantPermissions::PROTECTED,
        ];
    }

    /**
     * @param  array<int, string>  $requested
     * @param  array<int, string>  $current
     * @return array<int, string>
     */
    private function guardPermissionChanges(TenantUser $actor, array $requested, array $current): array
    {
        $requested = array_values(array_unique(array_map('strval', $requested)));
        $unknown = array_diff($requested, TenantPermissions::allKeys());

        if ($unknown !== []) {
            throw ValidationException::withMessages([
                'permissions' => 'Unknown permission(s): '.implode(', ', $unknown).'.',
            ]);
        }

        if ($actor->role === 'owner') {
            return $requested;
        }

        $added = array_diff($requested, $current);
        $removed = array_diff($current, $requested);
        $protectedChanges = array_merge(
            array_filter($added, TenantPermissions::isProtected(...)),
            array_filter($removed, TenantPermissions::isProtected(...)),
        );

        if ($protectedChanges !== []) {
            throw ValidationException::withMessages([
                'permissions' => 'Protected permissions can only be changed by the restaurant owner: '.
                    implode(', ', $protectedChanges).'.',
            ]);
        }

        $escalated = array_diff($added, $this->effectivePermissions($actor));

        if ($escalated !== []) {
            throw ValidationException::withMessages([
                'permissions' => 'You cannot grant permissions your own role does not hold: '.
                    implode(', ', $escalated).'.',
            ]);
        }

        return $requested;
    }

    private function assertNameIsAvailable(string $name, ?TenantRole $ignore = null): void
    {
        if ($name === '') {
            throw ValidationException::withMessages(['name' => 'Enter a role name.']);
        }

        if (mb_strlen($name) > 100) {
            throw ValidationException::withMessages(['name' => 'Role names may not exceed 100 characters.']);
        }

        $exists = TenantRole::query()
            ->whereRaw('lower(name) = ?', [mb_strtolower($name)])
            ->when($ignore !== null, fn ($query) => $query->whereKeyNot($ignore->getKey()))
            ->exists();

        if ($exists) {
            throw ValidationException::withMessages([
                'name' => 'Another role already uses this name.',
            ]);
        }
    }

    private function uniqueKey(string $name): string
    {
        $systemKeys = array_keys(TenantPermissions::systemRoles());
        $base = trim((string) Str::limit(Str::slug($name, '_'), 26, ''), '_');

        if ($base === '') {
            $base = 'role';
        }

        if (in_array($base, $systemKeys, true)) {
            $base = 'custom_'.$base;
        }

        $candidate = $base;
        $suffix = 2;

        while (in_array($candidate, $systemKeys, true) ||
            TenantRole::query()->where('key', $candidate)->exists()) {
            $candidate = $base.'_'.$suffix;
            $suffix++;
        }

        return $candidate;
    }

    private function nullableDescription(mixed $description): ?string
    {
        $description = is_string($description) ? trim($description) : '';

        if ($description === '') {
            return null;
        }

        return mb_substr($description, 0, 400);
    }
}
