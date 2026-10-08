# BusinessOS Restaurant Desktop — Premium acceptance matrix

**Status: NOT VERIFIED / NOT PRODUCTION-APPROVED**

Scope: Desktop WPF shell and local services on `feat/premium-restaurant-desktop` (PR #69).
Purpose: Final test protocol after implementing premium batches 1–11 without intermediate CI gating.
Do not merge, deploy, claim golden-path acceptance or distribute a production release based only on automated tests.

## Batch implementation checkpoints

| Premium batch | Expected implementation | Acceptance gate |
| --- | --- | --- |
| 1 | Sidebar navigation and route highlighting | UI keyboard focus, role visibility |
| 2 | Real business-day hourly sales | Time-zone boundary and receipt-based totals |
| 3 | Branch-specific low stock dashboard | Actual inventory/reorder levels |
| 4 | Glass / Classic theme consistency | 1080p and 4K Windows screenshots, switching and persistence |
| 5 | Dashboard quick actions | Correct workspace routing under current operator |
| 6 | Tenant-bound operator sign-in / workspaces | Online login, cached offline session, role denial, account switching |
| 7 | Visual restaurant floor map | Occupancy, selecting active/available tables, table transfer and split |
| 8 | KDS rush/delay/Expo indicators | Timers, configurable Queue/Preparing/Expo workflow |
| 9 | KOT/receipt printer interruption recovery | Thermal drivers, physical check before ambiguous print retry |
| 10 | Backup and cold-start restore | Clean DB, old DB upgrade, WAL/checksum failure and local-data retention |
| 11 | EXE/installer provenance and checksums | Windows build, branded Inno, signed-vs-unsigned validation |
| 12 | Whole-system golden-path acceptance | All CI green plus tests below |

## Automated checks required on final PR head

- `dotnet restore desktop/BusinessOS.Restaurant.Desktop.slnx`
- `dotnet build desktop/BusinessOS.Restaurant.Desktop.slnx --configuration Release`
- `dotnet test desktop/BusinessOS.Restaurant.Desktop.slnx --configuration Release`
- Windows x64 single-file publish, packaged startup smoke, Inno installer compilation and runtime smoke
- Artifact manifest: source commit + unsigned CI label + SHA-256 for EXE and Setup
- General repository CI: Laravel/PHP migrations, PHPUnit, frontend/Vite, Flutter format/analyze/tests and Android build

Existing automated Golden Path/regressions to retain:
`KotRealignmentTests.Desktop_golden_path_handles_two_kot_rounds_inventory_serve_bill_and_payment`,
`KotRealignmentTests.Takeaway_golden_path_settles_without_creating_or_releasing_a_fake_table`,
`CashierPosTests.Served_order_can_be_billed_split_paid_and_closed_with_receipt`,
`KotRealignmentTests.Refire_creates_new_round_and_second_production_without_duplicate_bill_line`,
`KotRealignmentTests.Inventory_is_reserved_on_send_and_consumed_once_per_production_item`,
`LocalDatabaseUpgradeTests`, `Batch14MaintenanceTests`, `PrinterRecoveryPolicyTests`,
`OperatorShellWiringTests` and `RestaurantWorkspaceRoutesTests`.

Automated test existence is not evidence of real Windows visual/peripheral acceptance.

## Manual full-service golden path — REQUIRED

Execute on the actual Windows desktop host with a LAN waiter device and a thermal kitchen/receipt printer.
Use a fresh restaurant and a copy of a realistic existing restaurant database, not production data.

1. Activate computer with a valid license and verify the existing activation is reused on reinstall.
2. Sign in as Owner; switch to Cashier and Kitchen operators, verifying visible/blocked workspaces.
3. Open a waiter shift and a cashier session.
4. Open a dine-in table for 4 guests.
5. Add drinks, starter and main course with modifiers/allergy instructions and optional held course.
6. Send first KOT: confirm human KOT number and station printer routing; preserve immutable snapshot.
7. Confirm drink items route to Drinks; grill items route to Grill; unmapped items use General Kitchen.
8. Move Queue → Preparing → Ready with both options enabled.
9. Fire a held course; add dessert later and send a second KOT without duplicating Round 1 lines.
10. Observe elapsed-time warnings, late labels, Rush priority and real-time dashboard counts.
11. Refire one produced item; verify new production and ingredient usage without duplicate bill quantity.
12. Recall a produced item with audit reason and confirm inventory is not consumed twice.
13. Void one post-KOT item with applicable manager approval and correct reservation/waste records.
14. Check Expo hand-off with Expo ON; confirm direct Ready path with Expo OFF.
15. Transfer a table; move/split unsent items; verify already-fired KOT lines are never rewritten.
16. Serve the order, issue a canonical bill and apply an authorized discount if appropriate.
17. Split bill allocation and settle using two posted payments with idempotent payment IDs.
18. Verify table returns to available and cashier receipts print the correct amount.
19. Review local sales, inventory, KOT rounds and audits; perform end-of-day closing.
20. Restart the app and Windows host; confirm orders, stock movements, payments and closing persist.
21. Repeat short flows with takeaway and counter orders (no phantom table).
22. Repeat KDS flows with Queue OFF/Preparing ON, Queue ON/Preparing OFF and both OFF.
23. Disconnect Internet but keep LAN; waiter app must still submit and receive local KOT updates.
24. Restart router/reconnect LAN and confirm no duplicate orders/KOT/consumption/payments.
25. Simulate unavailable kitchen printer; verify warnings, queued/failed status and controlled reprint.
26. Simulate interruption during spool submission; verify ambiguous `printing` requires physical review before manager retry.
27. Create SQLite online backup; stage and apply a verified restore at cold start, retaining safety backup.
28. Tamper staged restore hash: launch must not replace the live DB.
29. Install branded EXE/Setup at 1080p and 4K; verify Glass, Classic, installer artwork, trial, activation and signed/unsigned provenance.
30. Review Windows event logs and app diagnostics after normal service and forced interruption.

Repeat critical scenarios with user accounts appropriate to their actions. Do not bypass role checks to make a test pass.

## Required final evidence

- Exact feature branch + commit SHA, open PR and changed modules
- Number of tests and failures; complete Windows and general CI conclusions
- Downloadable Windows EXE, installer and their commit-linked SHA-256 manifest
- Physical printer model(s), paper output and duplicate-print safeguards
- Screenshots for Glass and Classic at 1080p and 4K
- Offline/LAN waiter device and recovery traces
- Prior-database migration/restore results
- Actual full-service 30-step checklist outcome and outstanding defects
- Signing status: an unsigned CI artifact must never be described as a signed production release

**Release decision: BLOCKED until full GitHub CI is green and all critical hardware/manual acceptance scenarios have passed.**

## Automated-only acceptance continuation — user-deferred local testing (2026-10-09)

The user has deferred 4K display testing and all activities requiring local installation or hands-on operation. This changes **execution priority**, not the production-release safety gate.

- **Verified on previous head:** both Restaurant Desktop CI and general CI succeeded at `b80b392315609d3094ff386586800beb93de4382`.
- **New automated release control:** the Windows CI now independently reads its generated provenance manifest, checks `source_commit` against `GITHUB_SHA`, enforces the `unsigned_ci` label and compares both EXE and Inno Setup SHA-256 hashes to their built files. This must pass on the new PR head.
- **Automated coverage to retain:** local KOT rounds, idempotent inventory consumption, refire, takeaway, split payments, database upgrade, printer recovery policies and authorization/source regressions. The existing `dotnet test` and general repository CI remain required; passing tests must not be described as a real-device validation.
- **Deferred (not passed):** 4K/1080p appearance, installer activation/upgrade and data retention on an actual workstation, physical thermal printing and ambiguous print replay, real LAN waiter/offline recovery, genuine power-loss SQLite restore and the full operator-driven 30-step acceptance script.
- **Release decision:** continue automated audits and regression fixes only. Keep PR #69 draft and do not declare production-ready or distribute an unsigned CI installer as a signed release until required real-device acceptance is completed.
