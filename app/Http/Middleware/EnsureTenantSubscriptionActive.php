<?php

namespace App\Http\Middleware;

use App\Models\Business;
use App\Services\Platform\SubscriptionService;
use Closure;
use Illuminate\Http\Request;
use Symfony\Component\HttpFoundation\Response;

class EnsureTenantSubscriptionActive
{
    public function __construct(private readonly SubscriptionService $subscriptions) {}

    public function handle(Request $request, Closure $next): Response
    {
        $business = Business::query()->where('tenant_id', tenant('id'))->first();

        if (! $business) {
            return $this->lockedResponse($request, 'subscription_unavailable', 'Restaurant subscription record was not found.');
        }

        $access = $this->subscriptions->access($business);

        if (! $access->allowed) {
            return $this->lockedResponse(
                $request,
                $access->code,
                'An active trial or subscription is required to create new restaurant transactions.',
                $access->status,
                $access->endsAt?->toIso8601String(),
            );
        }

        $request->attributes->set('platform_business', $business);
        $request->attributes->set('subscription_access', $access);

        return $next($request);
    }

    private function lockedResponse(
        Request $request,
        string $code,
        string $message,
        ?string $status = null,
        ?string $endsAt = null,
    ): Response {
        $payload = [
            'code' => $code,
            'message' => $message,
            'tenant_status' => $status,
            'ends_at' => $endsAt,
            'renewal_contact' => config('platform.renewal_contact'),
        ];

        if ($request->expectsJson() || $request->is('api/*')) {
            return response()->json($payload, 423);
        }

        return response()->view('tenant.subscription-locked', $payload, 423);
    }
}
