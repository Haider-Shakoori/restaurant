# BusinessOS Restaurant Desktop

Native Windows client for the existing BusinessOS Restaurant/KOT platform.

## Architecture

- .NET 10
- WPF
- MVVM
- EF Core + SQLite
- Offline-first local persistence
- Existing Laravel platform remains the SaaS control plane/API
- Windows CI through GitHub Actions

## Planned desktop modules

1. Foundation and CI
2. Licensing, activation and configuration
3. Authentication, roles and permissions
4. Branches, dining areas and tables
5. Menu, modifiers and pricing
6. Waiter ordering and offline outbox
7. Kitchen stations, KOT routing, KDS and thermal printing
8. Cashier/POS, split/merge/transfer and payments
9. Daily closing, waiter shifts and audit controls
10. Inventory, recipes and purchasing
11. Two-way synchronization and conflict handling
12. LAN server/client mode and degraded-internet operation
13. Reports and accounting surfaces
14. Backup/restore, updater and diagnostics
15. Branding, installer and signed release pipeline

The Laravel and Flutter applications remain independent and unchanged by the desktop client.
