<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Services\Platform\LicenseSigningService;
use Illuminate\Http\JsonResponse;

class LicensePublicKeyController extends Controller
{
    public function __invoke(LicenseSigningService $signing): JsonResponse
    {
        return response()->json([
            'algorithm' => 'Ed25519',
            'key_id' => config('license.signing.key_id'),
            'schema_version' => (int) config('license.schema_version', 1),
            'public_key' => $signing->publicKeyEncoded(),
        ]);
    }
}
