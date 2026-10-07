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
