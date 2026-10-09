# Restaurant Desktop — completion and UX acceptance backlog

Status: **IN PROGRESS — NOT PRODUCTION APPROVED**. Passing existing CI does not mean these features are complete.

## Operational CRUD (must be usable from the desktop, not read-only grids)

- [ ] Dining floors/areas: add, rename, deactivate; tables: add, edit, move between areas, seat capacity and active state; refresh in local LAN waiter APIs.
- [ ] Menu categories/products: create, edit, archive, availability, price and **image upload**; serve images through authenticated local LAN API with stable URLs and offline-friendly tablet caching.
- [ ] Inventory: create/edit ingredients, units, opening balances, adjustments, reorder alerts, and auditable stock movements.
- [ ] Recipes: dedicated page for each menu item's ingredient quantities, yield and unit conversion; cost preview from local stock valuation; consume stock once when production/sale reaches the configured event.
- [ ] Purchases: create PO with supplier and items; single **Mark Received** action posts inventory/valuation and completes PO atomically; no separate user-facing GRN entry; idempotent re-click/retry.
- [x] Expenses: prominent Create Expense button with modal and blurred background; verify modal keyboard/focus and saving behavior in Windows CI.
- [ ] Daily closing: restore readable text in Glass mode and verify contrast in both themes.

## UX quality gate

- [ ] Replace excessively long vertically stacked pages with compact headers, primary action toolbar, tabs and bounded, independently scrolling tables/forms.
- [ ] Review every button foreground/background combination in Classic and Glass themes (normal, hover, pressed, disabled), including daily closing and modal actions.
- [x] Introduce shared button foreground propagation and consistent Segoe UI typography; **needs runtime visual verification**.
- [ ] Validate at normal laptop resolutions without horizontal clipping; defer 4K-specific manual checks per project instruction.
- [ ] Keep operational error messages actionable; resolve actual Kitchen/Settings load errors, not only duplicate notices.

## Automated GitHub quality gate

- [ ] Build/test .NET solution on Windows; unit/integration tests for CRUD, menu-image LAN responses, recipe costing/consumption, PO receive idempotency, expense saving, daily closing.
- [ ] Publish desktop EXE, launch smoke-test on hosted Windows runner, build branded installer, smoke-test installer wizard, verify release checksums.
- [ ] Add UI automation for navigating each screen, clicking create/save actions and verifying readability/layout at a supported runner resolution.
- [ ] Run a **licensed** installer-install-launch smoke test on a controlled runner with test activation. Do not bypass production licensing or claim this is complete without a test license.
- [ ] Keep PR draft and unsigned artifacts non-production until final hardware acceptance (printer, LAN waiter tablet, offline reconciliation).
