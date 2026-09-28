<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\ReverseJournalRequest;
use App\Http\Requests\Tenant\StoreManualJournalRequest;
use App\Models\ChartAccount;
use App\Models\JournalEntry;
use App\Models\RestaurantBranch;
use App\Models\TenantUser;
use App\Services\Tenant\AccountingService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Validation\ValidationException;

class JournalEntryController extends Controller
{
    public function index(Request $request): JsonResponse
    {
        $entries = JournalEntry::query()
            ->with(['branch', 'postedBy'])
            ->withCount('lines')
            ->when(
                $request->filled('branch_id'),
                fn ($query) => $query->where('branch_id', $request->string('branch_id')->toString()),
            )
            ->when(
                $request->filled('source_type'),
                fn ($query) => $query->where('source_type', $request->string('source_type')->toString()),
            )
            ->when(
                $request->filled('from'),
                fn ($query) => $query->whereDate('entry_date', '>=', $request->string('from')->toString()),
            )
            ->when(
                $request->filled('to'),
                fn ($query) => $query->whereDate('entry_date', '<=', $request->string('to')->toString()),
            )
            ->latest('entry_date')
            ->latest('posted_at')
            ->limit(250)
            ->get();

        return response()->json(['data' => $entries]);
    }

    public function show(JournalEntry $journalEntry): JsonResponse
    {
        return response()->json([
            'data' => $journalEntry->load(['branch', 'postedBy', 'lines.account', 'reversalOf']),
        ]);
    }

    public function store(
        StoreManualJournalRequest $request,
        AccountingService $accounting,
    ): JsonResponse {
        /** @var TenantUser $user */
        $user = $request->user();
        $data = $request->validated();
        $branchId = $data['branch_id'] ?? null;

        if ($branchId !== null && ! RestaurantBranch::query()->whereKey($branchId)->exists()) {
            throw ValidationException::withMessages([
                'branch_id' => 'The selected branch does not exist.',
            ]);
        }

        $accountIds = collect($data['lines'])->pluck('account_id')->unique();
        $accounts = ChartAccount::query()
            ->whereIn('id', $accountIds)
            ->where('is_active', true)
            ->pluck('id');

        if ($accounts->count() !== $accountIds->count()) {
            throw ValidationException::withMessages([
                'lines' => 'One or more journal accounts are missing or inactive.',
            ]);
        }

        $entry = $accounting->post(
            $branchId,
            $user,
            'manual_journal',
            $data['client_journal_id'],
            'posted',
            $data['description'],
            $data['entry_date'],
            $data['lines'],
            'manual-journal:'.$data['client_journal_id'],
        );

        return response()->json(['data' => $entry], 201);
    }

    public function reverse(
        ReverseJournalRequest $request,
        JournalEntry $journalEntry,
        AccountingService $accounting,
    ): JsonResponse {
        /** @var TenantUser $user */
        $user = $request->user();

        return response()->json([
            'data' => $accounting->reverse(
                $journalEntry,
                $user,
                $request->string('reason')->toString(),
            ),
        ]);
    }
}
