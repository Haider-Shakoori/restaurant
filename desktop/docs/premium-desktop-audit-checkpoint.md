# Premium Restaurant Desktop — Baseline audit checkpoint

Date: 2026-10-08
Scope: `desktop/` only
Base branch: `main`
Base commit: `97c77d1565f75c28e46c298693a13a24dab4d5c2`
Continuation branch: `feat/premium-restaurant-desktop`

## Safety gates

- Continue from `main`, not the older `feat/kot-realignment-desktop`: the latter is 157 commits behind `main` and has no unmerged commits according to GitHub compare at audit time.
- Do not change Laravel, Flutter, shared mutation semantics, licensing rules, or existing SQLite data.
- Implement only verified gaps. The attached 50-section premium-desktop specification is the acceptance target; a claim in documentation alone does not prove runtime acceptance.
- The audited base `main` commit passed both `Restaurant Desktop CI` (run 37696444065) and general `CI` (run 37696444281).

## Initial implementation classification (code / docs existence checks)

| Requirement | Classification | Evidence / follow-up |
| --- | --- | --- |
| Multi-KOT round architecture | ALREADY IMPLEMENTED IN CODE (acceptance pending) | `LocalSyncService`, `KotRealignmentTests`, `docs/kot-realignment-desktop.md` |
| Configurable Queue/Preparing states | ALREADY IMPLEMENTED IN CODE (acceptance pending) | `LocalRestaurantSettingsService`, `LocalKitchenService`, `KotRealignmentTests` |
| Expo / course fire / rush / refire / recall | ALREADY IMPLEMENTED IN CODE (acceptance pending) | Same service and regression coverage; test scenario must still be reviewed |
| Reservation / exactly-once consumption | ALREADY IMPLEMENTED IN CODE (acceptance pending) | `LocalInventoryService`; KOT documentation |
| Dine-in / takeaway / counter | ALREADY IMPLEMENTED IN CODE (acceptance pending) | `LocalCashierService`, order tests |
| LAN-first local host and sync | ALREADY IMPLEMENTED IN CODE (acceptance pending) | `LocalRestaurantServer`, `LocalSyncService`, LAN tests |
| Licensing and Windows installer | ALREADY IMPLEMENTED IN CODE (acceptance pending) | Licensing projects, Inno Setup script, release tests |
| Background image and glass theme | PARTIALLY COMPLETE | `MainWindow.xaml` loads `RestaurantGlassBackground.jpg` using `UniformToFill` and blur; `Glass.xaml` defines translucent shell brushes. Need review of nested page surfaces and visual screenshot acceptance |
| Operational workspace permissions | PARTIALLY VERIFIED | Auth and roles exist; need page-by-page UI access audit |
| Professional role-specific dashboard / cashier workflow | PARTIALLY VERIFIED | WPF operational pages exist; need measured UI acceptance against reference |
| Advanced actual printer / network / power interruption recovery | REQUIRES MANUAL ACCEPTANCE | Automated test coverage and concrete device validation must be separated |
| Golden-path production readiness | NOT VERIFIED | Cannot assert until end-to-end acceptance succeeds |

## Proposed controlled sequence

1. Complete **read-only gap audit** of mainline desktop services, pages, schemas and existing tests; avoid speculative rewrites.
2. Patch verified UX gaps, prioritizing dashboard + glass surfaces + role-aware navigation, without changing runtime restaurant logic.
3. Address validated operational POS/floor/KDS gaps.
4. Validate peripheral, offline/LAN, licensing, data migration, installer and end-to-end golden path.
5. For every implementation batch: tests + Windows build + GitHub Actions green + checkpoint before proceeding.

## CI policy

When CI is pending, stop before the next dependent batch and re-check the result in the next active session. CI red must be investigated and fixed rather than ignored; do not claim an unattended 30–60 minute waiting period or automatic session resumption.

## Batch 1 continuation — Sidebar route state and keyboard usability

Verified gap: the existing Desktop sidebar had hover styling but no active-route state or explicit keyboard-focus treatment. All twelve routes were already wired to working navigation commands, so the commands and pages are preserved.

- Added a reusable rounded sidebar button template and route-aware selected state.
- The active route updates only after its operational page is successfully loaded.
- Keyboard focus is visibly outlined in both Classic and Glass themes.
- The converter changes presentation only; it does not introduce a second navigation or authorization system.
- Added static XAML/view-model regression tests. Windows build and GitHub CI are the acceptance gate.
- **Deferred**: role-specific hiding/authorization until the MainWindow startup/authenticated-session boundary is audited. The existing app starts the main window after licensing without an interactive operator login; hiding menu items without an authenticated operator would be misleading and could block access. Role enforcement must be handled separately at the service boundary.

## Batch 2 — Truthful business-day dashboard trend (CI pending)

- Removed the hard-coded rising sales polyline. Empty days now explicitly say there are no billed sales.
- Daily sales and hourly trend are derived from the same local SQLite bill snapshots, with receipt timestamps converted to the Windows restaurant time zone.
- Exclude future timestamps from the as-of dashboard projection; no accounting records are mutated.
- Added aggregation tests for Afghanistan's UTC+04:30 local-day boundary, differing timestamp offsets, and empty days.
- Existing multi-KOT and other operational logic remain untouched.
- Visual screenshot acceptance (including photographic blur and nested Glass surfaces), real printer testing, and final production golden path are still outstanding.

## Batch 3 — Branch-aware stock alerts and accurate service counts (CI acceptance pending)

- Confirmed that the old dashboard counted cancelled orders as open and every non-available table as occupied. Updated service KPIs to exclude cancelled orders and count only occupied active tables.
- Added a read-only, premium-style Stock Alerts panel to the desktop dashboard using actual inventory balances and reorder levels for active branches. Zero/unconfigured thresholds do not trigger alarms.
- Branch and ingredient names are shown for the five most urgent alerts; the summary makes additional alerts visible without fabricating stock records or multiplying shared item balances across branches.
- When inventory has no balances or no configured thresholds, show explicit setup guidance instead of claiming that stock health is good.
- Added pure domain regression tests for branch separation, empty inventory, out-of-stock ordering, and threshold boundary conditions.
- No changes to Laravel, Flutter, purchasing, pricing, stock movement, KOT, or existing SQLite schema.
- Next acceptance gate: .NET tests, Windows publish/installer smoke tests, then green GitHub Actions before starting another batch.

## Batch 4 — Theme-consistent nested surfaces (deferred final acceptance)

- Confirmed the photo backdrop, Glass/Classic switch, and shared card brushes already exist. Did not replace the supplied restaurant background.
- Removed fixed white WPF DataGrid row paint from the shared application style; nested row, alternate row, header, selected row and topbar action brushes now resolve by theme.
- The Glass palette uses translucent nested surfaces. Classic defines opaque counterparts, while dynamic resources allow live theme switching without recreating the data or resetting operational state.
- Added theme parity tests and a high-quality image scaling setting for the existing blurred backdrop.
- No changes to restaurant data, KOT, role privileges, or licensing. Windows 1080p/4K screenshot acceptance still required.

## Batch 5 — Dashboard restaurant quick actions (final acceptance pending)

- Added quick actions for POS & orders, tables & floor, kitchen/KOT, and inventory directly above the premium dashboard KPIs.
- Each action invokes the existing MainWindow shell navigation command with the existing route key, retaining the same page load and future authorization boundary as sidebar navigation. No duplicate order, kitchen or stock logic was created.
- Buttons inherit transparent/opaque surfaces through the current Glass or Classic theme, with useful tooltips and touch-friendly sizing.
- Added static regression checks for shell command binding and route reuse.
- This is navigational convenience only; it does not establish role-based permission enforcement or prove cashier/floor/KDS golden-path acceptance.

## Batch 6 — Tenant-bound operator sign-in and role-aware shell (final validation pending)

- Audited a verified critical gap: main window started immediately after computer activation even though desktop operations require a DPAPI-protected operator session. Cached/cloud authentication services and authorization rules already existed.
- Wired startup to reuse a cached tenant-bound operator session offline, or require a new online sign-in through the existing tenant authentication API before displaying the shell. First-time login saves the configured tenant URL only after a matching licensed tenant and supported operator role are verified.
- Added a themed operator sign-in window using WPF PasswordBox (not plain-text password fields). Activation and sign-in modal windows no longer trigger premature application shutdown before the main window is assigned.
- Exposed the current operator in the topbar and provided a switch-operator action that reconstructs the entire shell/view model on successful re-authentication.
- Added a fail-closed route visibility and navigation policy for owner, manager, cashier, waiter and kitchen; the kitchen role lands on KOT and does not see sales-dashboard workspaces. Waiters see order entry without cashier-only billing/session controls. Existing local service authorizers are still authoritative.
- Added tests for route policy, startup ordering, login tenant check, sidebar role visibility and PasswordBox wiring.
- Does NOT prove enterprise-grade re-authentication or offline revocation handling; shared Windows-user kiosk policy, session expiry, device tests and the full service permission audit remain for final security and manual acceptance. No new schema, Web/Flutter changes, licensing contract changes or disabled KOT flows.

## Batch 7 — Visual table/floor board (final acceptance pending)

- Verified the existing live floor page relied on a CRUD-like DataGrid despite working table transfer, order merge, unsent-line move and split services.
- Added an area-grouped, touch-sized visual table board based entirely on existing local dining table and active order projections. Cards show table capacity, status, order reference and amount without synthetic/demo data.
- Selecting an occupied card chooses its existing order for table operations; selecting an available card chooses the destination table. Unavailable cards cannot be selected as destinations.
- Kept the complete original table list behind a show/hide toggle for operations/auditing; backend service operations and the immutable KOT round policy remain unchanged.
- Added static coverage for actual row projection and reuse of transfer/merge/move/split business services.
- A real touch-device walkthrough, card layout screenshot checks and complete waiter-to-cashier end-to-end acceptance remain outstanding.

## Batch 8 — Live kitchen KDS triage indicators (final acceptance pending)

- Audited the existing KDS: independent optional Queue/Preparing steps, Expo transitions, rush, refire, recall, item-level timers and two-second live refresh were already present and were not recreated.
- Added visible live counts for selected station/state: in-production, Expo, Ready, Rush and delayed items. Counts use actual ticket-item projections, the current filter and the configured kitchen late threshold.
- Counts refresh on data changes and each age-timer tick; no change to KOT submission, item state mutation, recipe consumption, billing, or notification rules.
- Added the existing "expo" operator role to the constrained KDS-only workspace route policy, preserving Expo actions already implemented at service/UI level.
- Added regression tests for state/role preservation and live-summary wiring.
- Exact in-kitchen hardware/performance and visual screenshot acceptance remain unverified.

## Batch 9 — Interrupt-safe printer queues and audited recovery (final validation pending)

- Audited KOT and receipt spool workers. A durable job left in `printing` after process crash was automatically resent on restart; its true physical paper state is unknowable, so replay could lead to duplicate production.
- Removed automatic replay of ambiguous `printing` jobs. Failed spool submissions retain automatic retry with exponential in-process delay, rather than exhausting ten attempts in a rapid loop. Explicit manager retry resets the job to `pending` and bypasses failed-job backoff.
- Added a real Settings printer-queue summary (pending/failed/interrupted) and a physically-confirmed Owner/Manager retry action for original KOT/receipt document IDs. The workflow records a local audit event and does not create another order, KOT round, recipe consumption or customer bill.
- Added policy regressions for worker selectors, failure backoff, approval confirmation and audit recording.
- These safeguards reduce but cannot eliminate printer/spooler ambiguity. Real thermal printers, driver faults, copy counts and network interruptions still need hands-on acceptance. There is no guaranteed exactly-once physical printing across power loss.
