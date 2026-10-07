<?php

namespace App\Http\Controllers\Platform;

use App\Enums\PlatformRole;
use App\Http\Controllers\Controller;
use App\Http\Requests\Platform\StoreOperatorRequest;
use App\Models\AdminUser;
use Illuminate\Http\RedirectResponse;
use Illuminate\Validation\Rules\Password;
use Illuminate\View\View;

class OperatorController extends Controller
{
    public function index(): View
    {
        $operators = AdminUser::query()
            ->withCount('assignedBusinesses')
            ->orderBy('name')
            ->get();

        return view('platform.operators.index', compact('operators'));
    }

    public function create(): View
    {
        return view('platform.operators.create', [
            'roles' => PlatformRole::cases(),
        ]);
    }

    public function store(StoreOperatorRequest $request): RedirectResponse
    {
        AdminUser::create([
            ...$request->validated(),
            'is_active' => true,
        ]);

        return redirect('/platform/operators')->with('status', 'Platform operator created.');
    }

    public function toggle(AdminUser $adminUser): RedirectResponse
    {
        abort_if($adminUser->is(request()->user()), 422, 'You cannot disable your own account.');

        $adminUser->update(['is_active' => ! $adminUser->is_active]);

        return back()->with('status', 'Operator status updated.');
    }

    public function updatePassword(AdminUser $adminUser): RedirectResponse
    {
        $validated = request()->validate([
            'password' => ['required', 'confirmed', Password::min(10)->letters()->numbers()],
        ]);

        $adminUser->update([
            'password' => $validated['password'],
        ]);

        return back()->with('status', 'Operator password updated.');
    }
}
