<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\KitchenTicketItem;
use App\Models\TenantUser;
use App\Services\Tenant\KitchenService;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;

class TenantWebKitchenController extends Controller
{
    public function start(
        Request $request,
        KitchenTicketItem $kitchenTicketItem,
        KitchenService $kitchen,
    ): RedirectResponse {
        /** @var TenantUser $user */
        $user = $request->user();

        $kitchen->startItem($kitchenTicketItem, $user);

        return $this->back('Kitchen item started.');
    }

    public function ready(
        Request $request,
        KitchenTicketItem $kitchenTicketItem,
        KitchenService $kitchen,
    ): RedirectResponse {
        /** @var TenantUser $user */
        $user = $request->user();

        $kitchen->readyItem($kitchenTicketItem, $user);

        return $this->back('Kitchen item marked ready.');
    }

    public function void(
        Request $request,
        KitchenTicketItem $kitchenTicketItem,
        KitchenService $kitchen,
    ): RedirectResponse {
        $data = $request->validate([
            'reason' => ['required', 'string', 'max:1000'],
        ]);

        /** @var TenantUser $user */
        $user = $request->user();

        $kitchen->voidItem($kitchenTicketItem, $user, $data['reason']);

        return $this->back('Kitchen item voided.');
    }

    public function recall(
        Request $request,
        KitchenTicketItem $kitchenTicketItem,
        KitchenService $kitchen,
    ): RedirectResponse {
        $data = $request->validate([
            'reason' => ['required', 'string', 'max:1000'],
        ]);

        /** @var TenantUser $user */
        $user = $request->user();

        $kitchen->recallItem($kitchenTicketItem, $user, $data['reason']);

        return $this->back('Kitchen item recalled.');
    }

    public function refire(
        Request $request,
        KitchenTicketItem $kitchenTicketItem,
        KitchenService $kitchen,
    ): RedirectResponse {
        $data = $request->validate([
            'reason' => ['required', 'string', 'max:1000'],
            'client_operation_id' => ['required', 'string', 'max:80'],
        ]);

        /** @var TenantUser $user */
        $user = $request->user();

        $kitchen->refireItem(
            $kitchenTicketItem,
            $user,
            $data['reason'],
            $data['client_operation_id'],
        );

        return $this->back('Kitchen item re-fired as new production.');
    }

    public function waste(
        Request $request,
        KitchenTicketItem $kitchenTicketItem,
        KitchenService $kitchen,
    ): RedirectResponse {
        $data = $request->validate([
            'quantity' => ['required', 'integer', 'min:1'],
            'reason' => ['required', 'string', 'max:1000'],
        ]);

        /** @var TenantUser $user */
        $user = $request->user();

        $kitchen->recordWaste(
            $kitchenTicketItem,
            $user,
            (int) $data['quantity'],
            $data['reason'],
        );

        return $this->back('Production waste recorded.');
    }

    private function back(string $message): RedirectResponse
    {
        return redirect('/kitchen')->with('status', $message);
    }
}
