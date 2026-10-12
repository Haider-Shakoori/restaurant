<?php

namespace App\Http\Middleware;

use App\Models\TenantUser;
use App\Services\Tenant\TenantRoleService;
use Closure;
use Illuminate\Http\Request;
use Symfony\Component\HttpFoundation\Response;

/**
 * Deny-by-default permission gate backed by the tenant role catalog.
 *
 * Passes when the authenticated tenant user's role holds at least one of the
 * requested permission keys. The protected Super Admin role always holds
 * every permission. Unknown roles or missing grants are rejected.
 */
class EnsureTenantPermission
{
    public function __construct(
        private readonly TenantRoleService $roles,
    ) {}

    public function handle(Request $request, Closure $next, string ...$permissions): Response
    {
        $user = $request->user();

        abort_unless($user instanceof TenantUser, 403);

        foreach ($permissions as $permission) {
            if ($this->roles->userHasPermission($user, $permission)) {
                return $next($request);
            }
        }

        abort(403, 'Your role does not include the permission required for this action.');
    }
}
