<?php

namespace App\Http\Controllers\Platform;

use App\Http\Controllers\Controller;
use App\Models\Business;
use App\Services\Platform\TenantOwnerAccessService;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Validation\Rules\Password;
use Throwable;

class TenantOwnerAccessController extends Controller
{
    public function __invoke(Request $request, Business $business, TenantOwnerAccessService $access): RedirectResponse
    {
        $validated = $request->validate([
            'owner_email' => ['required', 'email:rfc', 'max:255'],
            'temporary_password' => ['nullable', 'confirmed', Password::min(8)->letters()->numbers(), 'max:128'],
        ]);

        try {
            $credentials = $access->reset(
                $business,
                $validated['owner_email'],
                $validated['temporary_password'] ?? null,
            );
        } catch (Throwable $e) {
            report($e);

            return back()->withErrors([
                'owner_access' => 'Owner access could not be created: '.$e->getMessage(),
            ]);
        }

        return back()
            ->with('status', 'Restaurant owner access is ready. The temporary password has replaced the previous owner password.')
            ->with('owner_credentials', $credentials);
    }
}
