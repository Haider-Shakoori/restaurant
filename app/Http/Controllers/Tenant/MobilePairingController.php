<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\Business;
use App\Models\MobilePairingToken;
use App\Services\Platform\LicenseService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Validation\ValidationException;

class MobilePairingController extends Controller
{
    public function create(Request $request): JsonResponse
    {
        $user = $request->user();

        abort_unless(
            $user && in_array(strtolower((string) $user->role), ['owner', 'admin', 'manager'], true),
            403,
        );

        $business = Business::query()
            ->where('tenant_id', tenant('id'))
            ->firstOrFail();

        $rawToken = rtrim(strtr(base64_encode(random_bytes(32)), '+/', '-_'), '=');
        $expiresAt = now()->addMinutes(5);

        MobilePairingToken::query()->create([
            'business_id' => $business->id,
            'created_by_user_id' => $user->id,
            'created_by_name' => $user->name,
            'token_hash' => hash('sha256', $rawToken),
            'expires_at' => $expiresAt,
        ]);

        return response()->json([
            'pairing_token' => $rawToken,
            'expires_at' => $expiresAt->toIso8601String(),
            'tenant_id' => $business->tenant_id,
        ], 201);
    }

    public function redeem(Request $request, LicenseService $licenses): JsonResponse
    {
        $validated = $request->validate([
            'pairing_token' => ['required', 'string', 'min:32', 'max:255'],
            'device_uid' => ['required', 'string', 'max:191'],
            'device_name' => ['nullable', 'string', 'max:191'],
            'platform' => ['required', 'in:android,ios'],
            'app_version' => ['nullable', 'string', 'max:64'],
        ]);

        $result = DB::connection(config('tenancy.database.central_connection'))->transaction(
            function () use ($validated, $licenses): array {
                $pairing = MobilePairingToken::query()
                    ->where('token_hash', hash('sha256', $validated['pairing_token']))
                    ->whereNull('consumed_at')
                    ->lockForUpdate()
                    ->first();

                if ($pairing === null || $pairing->expires_at->isPast()) {
                    throw ValidationException::withMessages([
                        'pairing_token' => 'The pairing QR code is invalid or has expired.',
                    ]);
                }

                $pairing->update([
                    'consumed_at' => now(),
                ]);

                return $licenses->activatePairedMobile(
                    $pairing->business()->firstOrFail(),
                    $validated['device_uid'],
                    $validated['device_name'] ?? null,
                    $validated['platform'],
                    $validated['app_version'] ?? null,
                );
            },
        );

        return response()->json($result, 201);
    }
}
