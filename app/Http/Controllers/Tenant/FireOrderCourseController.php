<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\Order;
use App\Models\TenantUser;
use App\Services\Tenant\OrderService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Validation\ValidationException;

class FireOrderCourseController extends Controller
{
    public function __invoke(
        Request $request,
        Order $order,
        int $courseNumber,
        OrderService $orders,
    ): JsonResponse {
        OrderController::authorizeOrder($request, $order);

        $mutationId = $request->header('Idempotency-Key');

        if (! filled($mutationId)) {
            throw ValidationException::withMessages([
                'idempotency_key' => 'Idempotency-Key is required when firing a course.',
            ]);
        }

        /** @var TenantUser $user */
        $user = $request->user();

        return response()->json([
            'data' => $orders->fireCourse(
                $order,
                $user,
                $courseNumber,
                (string) $mutationId,
                $request->string('priority', 'normal')->toString(),
            ),
        ]);
    }
}
