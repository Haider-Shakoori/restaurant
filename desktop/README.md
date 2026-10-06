# BusinessOS Restaurant Desktop

Native Windows operational client and local-network host for the existing BusinessOS Restaurant/KOT platform.

## Architecture

- .NET 10
- WPF
- MVVM
- EF Core + SQLite
- Embedded ASP.NET Core/Kestrel LAN host
- Offline-first local persistence
- Existing Laravel platform remains the SaaS control plane and cloud synchronization authority
- Flutter waiter app can operate against the desktop over the restaurant LAN
- Windows CI through GitHub Actions

## Desktop delivery sequence

1. ✅ Foundation and CI
2. ✅ Licensing, activation and configuration
3. ✅ Authentication, sessions and role authorization
4. ✅ Local Restaurant Host / LAN server
5. ✅ Local branches, tables, menu, modifiers and staff snapshots
6. ✅ Android -> Desktop waiter ordering, idempotent local mutations and pull cursors
7. ✅ Kitchen stations, KOT routing, KDS and thermal printing
8. ✅ Cashier/POS, serve/billing, discounts, split allocations, payments, transfer/merge and receipts
9. ✅ Daily closing, waiter/staff shifts and audit controls
10. ✅ Inventory, recipes, weighted-average valuation and purchasing
11. ✅ Desktop -> cloud reconciliation, two-way synchronization and conflict handling
12. ✅ LAN terminal management, diagnostics and degraded-network controls
13. Reports and accounting surfaces
14. Backup/restore, updater and diagnostics
15. Branding, Windows Firewall/installer integration and signed release pipeline

## Operational authority

During normal in-restaurant service:

Android waiter -> Desktop LAN host -> local SQLite -> KOT/Kitchen/Cashier.

Cloud availability must not be required for an already activated restaurant to continue taking orders within its valid signed offline lease.

The Laravel and Flutter applications remain part of the same product and share compatible API/sync contracts with the desktop host.
