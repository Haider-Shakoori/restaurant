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

Batch 6 established branches, dining areas/tables, menu, waiter authentication, retry-safe orders and submitted-order handling.

Batch 7 added branch-aware kitchen stations, menu-to-station routing, automatic KOT splitting, General Kitchen fallback, KDS queue/status APIs, kitchen audit events and the submitted → preparing → ready → served lifecycle.

Batch 8 adds cashier sessions, bill snapshots, discounts, split payments, table settlement, cashier variance and immutable versioned daily closing snapshots.

See docs/batch-06-ordering.md, docs/batch-07-kot-kds.md and docs/batch-08-cashier-pos-daily-closing.md.

## Current development status

- Batch 1 — Laravel SaaS foundation: merged.
- Batch 2 — Multi-database tenancy and tenant isolation: merged.
- Batch 3 — Platform Admin / central SaaS control plane: merged.
- Batch 4 — subscriptions, seven-day trial lifecycle and plan enforcement: merged.
- Batch 5 — license activation and signed offline lease foundation: merged.
- Batch 6 — tables, menu and waiter ordering: merged.
- Batch 7 — KOT/KDS and kitchen execution: merged.
- Batch 8 — cashier/POS, payments and daily closing: merged.

Next: **Batch 9 — inventory, purchasing and recipes**.
