<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\StoreBranchRequest;
use App\Models\Branch;
use App\Models\Restaurant;
use App\Services\Tenant\BranchService;
use Illuminate\Http\RedirectResponse;
use Illuminate\View\View;

class BranchController extends Controller
{
    public function index(BranchService $branches): View
    {
        $restaurant = Restaurant::where('profile_key', 'primary')->firstOrFail();

        return view('tenant.branches.index', [
            'restaurant' => $restaurant,
            'branches' => $restaurant->branches()->orderByDesc('is_primary')->orderBy('name')->get(),
            'maxBranches' => $branches->maxBranches(),
        ]);
    }

    public function create(): View
    {
        return view('tenant.branches.form', ['branch' => null]);
    }

    public function store(StoreBranchRequest $request, BranchService $branches): RedirectResponse
    {
        $restaurant = Restaurant::where('profile_key', 'primary')->firstOrFail();

        $branches->create($restaurant, [
            ...$request->validated(),
            'is_primary' => $request->boolean('is_primary'),
            'is_active' => $request->boolean('is_active', true),
        ]);

        return redirect('/branches')->with('status', 'Branch created.');
    }

    public function edit(Branch $branch): View
    {
        return view('tenant.branches.form', compact('branch'));
    }

    public function update(
        StoreBranchRequest $request,
        Branch $branch,
        BranchService $branches,
    ): RedirectResponse {
        $branches->update($branch, [
            ...$request->validated(),
            'is_primary' => $request->boolean('is_primary'),
            'is_active' => $request->boolean('is_active'),
        ]);

        return redirect('/branches')->with('status', 'Branch updated.');
    }
}
