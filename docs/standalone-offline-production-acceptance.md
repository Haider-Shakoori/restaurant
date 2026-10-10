# BusinessOS Restaurant — Standalone Offline Production Acceptance

**Release scope:** Windows desktop and local LAN kitchen/cashier/waiter operations. The cloud SaaS remains a separate authority while cloud sync is enabled.

**Release gate:** Run the automated checks on GitHub before distributing a new unsigned Windows installer. Complete the hardware tests on an authorized test restaurant and retain signed test evidence. Never use real customer payments to satisfy this test plan.

## Operating mode / license

1. On Platform → Restaurant → License & Devices, enable **Standalone Offline** for the designated test restaurant.
2. Activate the Windows client and sign in the first Owner/Admin while connected (initial activation still requires Internet).
3. In Desktop → Settings → Network & Sync, refresh the signed mode; restart and confirm **Standalone Offline**.
4. Generate a signed renewal JSON from the matching activated Windows device on the platform (requires paid or valid trial access). Transfer it to the Windows PC using an authorized removable drive; on the PC import under Network & Sync with the Internet disconnected.
5. Confirm the device ID and tenant match, term is extended, and no HTTP requests occur. Test a tampered signature, wrong-device file and old-file downgrade: each must be rejected. Never transfer or export private signing keys or device secrets.
6. Disconnect WAN and block outbound TCP 80/443 while allowing loopback and local RFC1918 LAN traffic. Leave the desktop running throughout the test. Confirm POS and signed license remain usable until the signed term end, and automatic cloud activity is zero.

**Important:** An offline-importable file only renews an *already activated* machine. True first-time air-gapped activation is not implemented. Mode changes and initial Owner authentication still require a deliberate online step.

## Database / financial smoke (test data only)

Use an isolated tenant with an explicit baseline backup, currency AFN, and no tax unless legally configured.

1. Create an active branch, dining hall, kitchen station, kitchen route, available table, category, dish, recipe, stock item and supplier locally.
2. Enter a draft dine-in order and a counter order from separate test terminals. Send at least two KOT rounds; verify the correct kitchen station receives each and both appear in the POS. Check duplicate mutation retries and refresh do not create duplicate KOTs.
3. Start and finish the kitchen ticket; serve the order; create the bill. Check AFN subtotal equals persisted item-quantity × unit-price totals and issued bill cannot be edited by modifying current catalog prices.
4. Open a cashier session, apply a manager-authorized discount, and pay part in cash. Verify **table remains occupied**, bill is open, paid amount and balance are correct, and receipt shows the partial payment.
5. Replay the same payment ID via the LAN; verify one posting only. Reject overpayment and wrong-branch cashier sessions. Finish with another method; verify **one final close**, table becomes available and no negative balances.
6. Daily close: verify expected cash, declared cash, variance, discounts, payments by method and any split settlements, with no duplicate movements.
7. Receive an inventory purchase order twice with the same receipt key; verify only one stock movement. Finish/void kitchen production and check recipe consumption and waste/stock reversal cases. Compare inventory balance, valuation and audit ledger before and after.
8. Edit menu item price and disable a category/table after closing. Verify old receipts, order line snapshots, financial totals and stock history stay unchanged.
9. Back up the SQLite restaurant database. Restore on a dedicated test PC and verify integrity, license safety, KOT statuses, payment totals and financial close history. Do not restore over a live terminal without a maintenance window.

## LAN / physical device acceptance

- Verify the configured desktop firewall allows the local server port only on trusted private networks. Ensure other tenant devices and public Internet cannot reach the local service.
- Pair a test waiter device and a kitchen display/terminal to the desktop over the same local Wi-Fi/LAN. Verify the tenant and role permissions; do not bypass authentication.
- Disconnect the router's WAN uplink **without disconnecting its local Wi-Fi**; wait at least 30 minutes while creating and serving orders on separate devices. Verify Android waiter, kitchen, POS and table states converge through LAN without cloud.
- Test device Wi-Fi drop and reconnection during order submission (same client mutation ID). Only one KOT/order must appear after retry.
- Verify kitchen thermal printing and receipt printing on the actual printer model (paper width, printer driver, copy count, text encoding for English/Dari/Pashto, RTL, cut, power-off and retry).
- Test simultaneous cashier and waiter operations and backup while the LAN remains isolated. Review SQLite integrity and financial registers afterward.
- Test printer spooler errors, missing paper and duplicate-print retries separately from payment submission; never repeat a payment to retry a failed print.

## Standalone vs Cloud boundaries

- **Standalone:** no automatic cloud bootstrap, reconciliation, uploads, module changes, pairing tokens, or remote photo downloads. Changes are local.
- **Cloud Sync:** existing authorized sync behavior. When converting from standalone to cloud, back up local SQLite and perform an explicit reconciliation with conflict resolution before enabling uploads. Do not blindly upload standalone financial events.
- **Revocation:** an offline terminal cannot immediately receive a server revocation. Its previously issued signed lease remains usable until its signed expiration. Explain this to subscription administrators.
- **Security:** the production activation gate stays intact. The GitHub install smoke uses a non-distributable CI-only installer and cannot substitute for an activated app/hardware test.

## GitHub automation coverage

- PHPUnit central/tenant financial, subscription and real MySQL migration checks
- Windows .NET tests: standalone catalog safeguards, lease signature renewal, direct-cloud-block (no HTTP), local/LAN order and cashier idempotency, inventory purchasing accounting
- WPF smoke screenshots, packaged EXE startup, real CI-only Windows installation and uninstall
- Android Flutter/static/build tests

## Unfinished production gates

Do **not** mark the system fully production-certified until a responsible operator records:
- [ ] Successful initial activation and authorized account sign-in on a clean target PC
- [ ] 30-minute WAN-disconnected multi-device LAN/KOT/checkout run
- [ ] Physical printer and receipt compatibility checks
- [ ] Real-device SQLite backup/restore drill and reconciliation decision
- [ ] Offline renewal-file exchange, tamper rejection and subscription expiration tests
- [ ] Windows code signing or documented organization approval for unsigned installation
- [ ] First-time air-gapped activation and offline staff-provisioning strategy, if customers require zero Internet ever
- [ ] Explicit reviewed plan for standalone-to-cloud migration of historical transactions
