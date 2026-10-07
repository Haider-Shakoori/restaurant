<?php

namespace App\Http\Controllers\Api\V1;

use App\Http\Controllers\Controller;
use App\Services\Platform\LicenseService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class DesktopLicenseResolverController extends Controller
{
    public function __invoke(Request $request, LicenseService $licenses): JsonResponse
    {
        $validated = $request->validate([
            'license_key' => ['required', 'string', 'max:64'],
        ]);

        $resolved = $licenses->resolveDesktopLicense($validated['license_key']);

        return response()->json([
            'tenant_base_url' => $resolved['tenant_base_url'],
            'license' => [
                'version' => $resolved['license']->version,
                'last4' => $resolved['license']->key_last4,
            ],
        ]);
    }
}
