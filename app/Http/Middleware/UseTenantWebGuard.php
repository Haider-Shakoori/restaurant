<?php

namespace App\Http\Middleware;

use Closure;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Auth;
use Symfony\Component\HttpFoundation\Response;

class UseTenantWebGuard
{
    public function handle(Request $request, Closure $next): Response
    {
        Auth::shouldUse('tenant');

        // Disabling a staff member must also end an existing browser session,
        // not merely reject their next password-based login.
        $user = Auth::guard('tenant')->user();
        // Re-read the tenant record: an already-authenticated SessionGuard
        // can hold an earlier copy of the user after an owner disables it.
        $current = $user?->fresh();
        if ($user !== null && ($current === null || ! (bool) $current->is_active)) {
            Auth::guard('tenant')->logout();
            if ($request->hasSession()) {
                $request->session()->invalidate();
                $request->session()->regenerateToken();
            }

            return redirect('/login');
        }

        return $next($request);
    }
}
