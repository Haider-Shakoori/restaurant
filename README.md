# BusinessOS Restaurant

BusinessOS Restaurant is a multi-tenant SaaS restaurant management and waiter ordering platform designed for fast in-restaurant operations, including environments with slow or unreliable internet.

The primary workflow is:

**Waiter → Table → Order → Kitchen/KOT → Preparation → Ready → Serve → Bill → Payment → Table Close**

This is not a public food-delivery or customer self-ordering application.

## Foundation

Batch 1 establishes:

- Laravel 13 application foundation.
- Blade-first architecture with Tailwind CSS.
- Alpine.js for lightweight interaction.
- Laravel Sanctum API authentication foundation.
- Versioned REST API under `/api/v1`.
- AFN and Asia/Kabul defaults.
- English, Dari and Pashto locale metadata with RTL readiness.
- Low-bandwidth configuration defaults.
- Database-backed queue/cache/session defaults with Redis compatibility.
- Automated tests, formatting checks, Composer audit and frontend build in CI.

## Local setup

```bash
cp .env.example .env
composer install
php artisan key:generate
touch database/database.sqlite
php artisan migrate
npm ci
npm run build
php artisan test
```

For local development:

```bash
composer dev
```

Health endpoints:

- Framework health: `GET /up`
- API health: `GET /api/v1/health`

## Product principles

Restaurant operations must remain fast and usable during temporary connectivity failures. Mobile-originated transactional records will use ULID/UUID identifiers, synchronization will be idempotent, and financial records will not be destructively rewritten after finalization.

Tenant isolation, subscriptions, license leasing, restaurant onboarding, ordering, KOT/KDS, billing, inventory and Flutter offline synchronization are implemented incrementally in the numbered development batches.

See [docs/architecture.md](docs/architecture.md) for the authoritative SaaS boundaries and [docs/architecture-blueprint.md](docs/architecture-blueprint.md) for the central/tenant ERD, API contracts, permissions, plan rules, provisioning/deployment runbook, staged backlog, and risk register.

## Current development status

**Batch 1 — Laravel SaaS foundation: merged into `main`.**

Architecture baseline: **central SaaS database + one isolated database per restaurant tenant + domain-based identification + separate central/tenant migrations**.

**Batch 2 — Multi-database tenancy and tenant isolation: implemented.**

**Batch 3 — Platform Admin / central SaaS control plane: implemented.**

Batch 3 adds central operator authentication/roles, restaurant commercial records, provisioning health/history, tenant infrastructure visibility, configurable plans/features, reserved-subdomain controls, and a responsive Platform Admin UI. Tenant operational data remains isolated from the central application.

**Batch 4 — subscriptions, seven-day trial lifecycle and plan enforcement: implemented.**

Batch 4 adds configurable plan prices, immutable subscription-period snapshots, the provisioning-gated 7-day trial, early/late/custom renewal rules, hourly lifecycle refresh, tenant HTTP 423 locking, a recovery/status API, and server-side feature enforcement from the active subscription snapshot.

Next: **Batch 5 — license generation, activation and signed offline lease foundation.**