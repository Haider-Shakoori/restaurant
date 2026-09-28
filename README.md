# BusinessOS Restaurant

BusinessOS Restaurant is a multi-tenant SaaS restaurant management and waiter ordering platform designed for fast in-restaurant operations, including environments with slow or unreliable internet.

The primary workflow is:

**Waiter → Table → Order → KOT/KDS → Preparation → Ready → Serve → Bill → Payment → Table Close → Daily Closing**

This is not a public food-delivery or customer self-ordering application.

## Architecture

- Laravel 13 / PHP 8.3.
- Central BusinessOS SaaS control plane.
- One isolated database per restaurant tenant.
- Domain/subdomain-based tenant identification.
- Tenant-isolated cache, files, sessions and queued jobs.
- Configurable plans, seven-day hosted trial and subscription enforcement.
- One-time restaurant license keys and per-device activation.
- Ed25519-signed, time-bounded offline leases for the Android waiter app.
- AFN, Asia/Kabul, English/Dari/Pashto and RTL-ready defaults.
- Tailwind CSS 4 + Alpine.js with low-bandwidth defaults.

See docs/architecture.md and docs/architecture-blueprint.md.

## Restaurant operations

Batches 6–9 established waiter ordering, KOT/KDS, cashier/POS, daily closing, purchasing, ingredient inventory and recipe consumption.

Batch 10 added source-document double-entry accounting, perpetual inventory valuation, expenses, supplier payments, journal reversals, P&L, balance sheet, trial balance, ledgers, receivables/payables and management reporting.

Batch 11 adds the Flutter waiter client plus device-bound durable offline synchronization, local SQLite/outbox operation, deterministic conflicts, resumable incremental pull and signed offline-lease enforcement.

See docs/batch-10-accounting-reporting.md and docs/batch-11-flutter-offline-sync.md.

## Current development status

- Batch 1 — Laravel SaaS foundation: merged.
- Batch 2 — Multi-database tenancy and tenant isolation: merged.
- Batch 3 — Platform Admin / central SaaS control plane: merged.
- Batch 4 — subscriptions, seven-day trial lifecycle and plan enforcement: merged.
- Batch 5 — license activation and signed offline lease foundation: merged.
- Batch 6 — tables, menu and waiter ordering: merged.
- Batch 7 — KOT/KDS and kitchen execution: merged.
- Batch 8 — cashier/POS, payments and daily closing: merged.
- Batch 9 — inventory, purchasing and recipes: merged.
- Batch 10 — accounting and reporting: merged.
- Batch 11 — Flutter offline sync hardening: in development on batch/11-flutter-offline-sync.

Next after Batch 11: **Batch 12 — production deployment and full regression**.
