<?php

namespace App\Http\Middleware;

use App\Models\Business;
use App\Services\Platform\SubscriptionService;
use Closure;
use Illuminate\Http\Request;
use Symfony\Component\HttpFoundation\Response;

class RequirePlanFeature
{
    public function __construct(private readonly SubscriptionService $subscriptions) {}

    public function handle(Request $request, Closure $next, string $feature): Response
    {
        $business = $request->attributes->get('platform_business')
            ?? Business::query()->where('tenant_id', tenant('id'))->first();

        if (! $business || ! $this->subscriptions->featureEnabled($business, $feature)) {
            if ($request->expectsJson() || $request->is('api/*')) {
                return response()->json([
                    'code' => 'feature_not_available',
                    'message' => 'This feature is not enabled for the current subscription.',
                    'feature' => $feature,
                ], 403);
            }

            abort(403, 'This feature is not enabled for the current subscription.');
        }

        return $next($request);
    }
}
