# BusinessOS Restaurant

BusinessOS Restaurant is a multi-tenant SaaS restaurant management and waiter ordering platform designed for fast in-restaurant operations, including environments with slow or unreliable internet.

The primary workflow is:

**Waiter → Table → Order → Kitchen/KOT → Preparation → Ready → Serve → Bill → Payment → Table Close**

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

## Restaurant ordering foundation

Batch 6 adds the first operational restaurant flow:

- branches and dining areas;
- dining tables and occupancy state;
- menu categories/items;
- tenant waiter authentication and roles;
- draft orders and order items;
- transactional one-active-order-per-table enforcement;
- retry-safe client order/line identifiers;
- exact AFN price snapshots and totals;
- order audit events;
- waiter ownership enforcement;
- submit-order transition for the upcoming KOT/KDS batch.

See docs/batch-06-ordering.md.

## Local setup

Copy .env.example to .env, install Composer dependencies, generate an application key, create the local SQLite database, migrate, install frontend dependencies, build assets and run php artisan test.

## Current development status

- Batch 1 — Laravel SaaS foundation: merged.
- Batch 2 — Multi-database tenancy and tenant isolation: merged.
- Batch 3 — Platform Admin / central SaaS control plane: merged.
- Batch 4 — subscriptions, seven-day trial lifecycle and plan enforcement: merged.
- Batch 5 — license activation and signed offline lease foundation: merged.
- Batch 6 — tables, menu and waiter ordering: in development on batch/06-tables-menu-waiter-ordering.

Next after Batch 6: **Batch 7 — KOT/KDS and kitchen execution**.
