# Restaurant Desktop — automated operational regression audit

Status: **source-audited; new-head CI not yet confirmed**. Scope: GitHub-only; no local installation, printer, LAN hardware or 4K acceptance.

## Coverage confirmed in existing test source

| Domain | Existing test file | Existing assertions / scenarios | Next independent automated investigation |
| --- | --- | --- | --- |
| Purchase orders and goods receipts | `InventoryProcurementTests.cs` | Partial/final receipt, idempotent GRN, stock balance, valuation and weighted-average cost | Conflicting replay payload and over-receipt rejection |
| Recipe consumption | `InventoryProcurementTests.cs` | Active recipe, serve-twice idempotency, stock movement and low-stock recalculation | Multiple recipe ingredients and void/refire accounting |
| Cashier and shift close | `DailyClosingShiftAuditTests.cs` | Open shift/session blocks closing; posted payment, cash totals, reopen reason/role, versioned snapshots and audit events | Multiple cashiers, branch isolation and boundary dates |
| KOT and kitchen | `KotRealignmentTests.cs`, `KitchenExecutionTests.cs` | Existing KOT golden-path and kitchen regression suite | Concurrent retries, hold/fire and switchable kitchen stage combinations |
| SQLite upgrade | `LocalDatabaseUpgradeTests.cs` | Missing `kot_rounds` table restored during upgrade | Old-schema data preservation, interrupted upgrade and WAL recovery |
| Printer recovery | `PrinterRecoveryPolicyTests.cs` | No automatic replay of ambiguous `printing`; manager confirmation/audit | Runtime fake-spooler failure injection and retry exhaustion |
| Release provenance | `Batch15ReleasePackagingTests.cs`, desktop CI | Unsigned label, signing prerequisites, commit and artifact hash checks | Validate on each new head before artifact use |

## Priority and release boundaries

1. Add targeted *behavioral* tests for negative cases, concurrency and cross-branch isolation where the service API supports deterministic assertions. Prefer these over source-string assertions.
2. Preserve the existing local SQLite authority and exactly-once mutation semantics; do not modify production logic just to satisfy a test.
3. Keep separate checks for desktop .NET, Laravel and Flutter contract compatibility. Any shared-contract change requires its own integration validation.
4. 4K/1080p visuals, Windows installation, thermal printer output, real LAN waiter recovery and operator-driven golden path remain **deferred, not passed**.
5. CI may run concurrently with independent batches, but a failing or missing check remains a blocker for merging and production release. PR #69 stays draft.

## Batch checkpoint

This audit was performed from the existing repository test source without changing runtime services. It is an inventory of verified *test coverage*, not proof that the latest workflow has completed or that real-device acceptance passed.
