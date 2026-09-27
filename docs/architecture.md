# BusinessOS Restaurant — SaaS Architecture

This document is the authoritative architecture boundary for BusinessOS Restaurant. The product uses the same SaaS architectural pattern as the supplied reference specification, while restaurant workflows remain restaurant-specific.

## 1. Non-negotiable topology

BusinessOS Restaurant is **one product and one shared codebase** with two logically separate applications:

1. **Central SaaS application** on the platform domain.
2. **Tenant restaurant application** on a restaurant-specific subdomain/domain.

Each hosted restaurant tenant has:

- its **own database**;
- tenant-specific users and authentication;
- tenant-private files;
- tenant-scoped cache/session state;
- tenant-aware queued jobs;
- tenant-specific branding/settings.

The central application has a **separate central database**. Central operators do not implicitly read restaurant orders, customers, staff activity, POS payments, inventory, accounting, or operational reports.

A restaurant chain with multiple branches is modeled **inside one tenant** when those branches belong to the same business. Separate businesses must not be mixed merely to simplify deployment.

## 2. Central database versus tenant databases

### Central DB only

The central database owns SaaS/commercial and infrastructure state:

- tenants;
- domains;
- businesses/restaurants;
- central admin/operator accounts;
- plans and plan features;
- trials/subscriptions;
- platform invoices and invoice lines;
- platform payments;
- activation/provisioning requests;
- offline license/lease metadata;
- provisioning events;
- seller/commission records if enabled;
- central audit logs;
- invoice/sequence state.

**Platform payments are money paid to BusinessOS.** They must never share the tenant POS ledger simply because both are named “payments”.

### Tenant DB only

Every restaurant database owns operational data:

- tenant users, roles and permissions;
- restaurant profile/settings;
- branches, service areas and storage locations;
- dining tables/sections;
- menu categories, menu items, modifiers and recipes/BOM where used;
- waiter shifts/attendance references;
- orders and order lines;
- KOT/KDS tickets and kitchen status history;
- bills, discounts, refunds and exchanges;
- tenant POS payments and split payments;
- cashier sessions, shift closing and daily closing;
- customers where enabled;
- inventory, stock movements, purchases and suppliers;
- expenses, accounts and double-entry ledger records;
- tenant reports/export metadata;
- tenant audit/activity logs;
- tenant-scoped job/session/cache tables when database-backed.

The central DB must not become a reporting shortcut for tenant operational data.

## 3. Migration boundary

Use separate migration paths:

- `database/migrations/` — **central migrations only**.
- `database/migrations/tenant/` — **tenant operational migrations only**.

A plain `php artisan migrate` must never create restaurant operating tables in the central database.

Tenant migrations run only through tenant-aware provisioning/upgrade commands. CI must prove the separation.

## 4. Tenancy implementation

Batch 2 will use **stancl/tenancy v3-compatible multi-database tenancy**.

Required bootstrapping:

- domain/subdomain tenant identification;
- central connection retained as the central control-plane connection;
- dynamic tenant DB connection;
- database tenancy bootstrapper;
- cache isolation;
- filesystem isolation;
- queue tenant context;
- tenant route separation;
- central-domain protection;
- tests that deliberately attempt cross-tenant access.

Do not use a simple `tenant_id` column on one shared operational database as the primary isolation model.

## 5. Routing boundary

Recommended route split:

- `routes/web.php` — central/public SaaS routes only.
- `routes/api.php` — central API/health contracts only unless explicitly grouped.
- `routes/tenant.php` — tenant web routes resolved after tenant identification.
- tenant mobile API — versioned under the identified tenant domain, e.g. `/api/v1/*`.

The waiter/mobile application must establish tenant identity before operational authentication. Cross-subdomain cookies must not be assumed.

## 6. Subscription and lock behavior

The server is authoritative for trial/subscription state.

- Hosted trial default: **7 days**.
- Trial starts after successful provisioning/activation, not merely form submission.
- States include at minimum: provisioning, trial, active, due/overdue, expired, cancelled.
- Expiry **locks access; it never deletes tenant data**.
- Login/logout and clearly selected routes may remain available so users understand the lock state.
- API/AJAX/mobile calls receive machine-readable lock errors.
- Plan feature checks are enforced server-side through middleware/policies.
- Future offline operation uses signed, expiring leases/licenses rather than a local boolean.

## 7. Provisioning workflow

Provisioning is a persisted state machine:

1. Validate restaurant/contact data and requested subdomain.
2. Create central commercial record in provisioning state.
3. Create infrastructure tenant record and domain reservation.
4. Create tenant database through cPanel-compatible tooling where hosted on cPanel.
5. Run **tenant migrations only** in that database.
6. Seed roles, permissions, restaurant defaults, currencies, settings and owner/admin account.
7. Configure domain mapping and TLS.
8. Mark tenant ready only after application/domain smoke checks pass.
9. Start trial/activation milestone.
10. Redirect through an explicit login or short-lived tenant-bound handoff token.

Provisioning failures retain their reason and are safely retryable. Never drop a tenant database that may contain operational records.

## 8. Offline waiter architecture

The Android waiter application is offline-first for active restaurant operation.

- Local mobile data uses SQLite.
- New mobile-originated transactional records use ULID/UUID identifiers.
- Writes enter a durable local outbox.
- Sync requests carry idempotency keys.
- Server writes are transactional and idempotent.
- Sync is incremental, compact and resumable.
- Tenant identity is part of the authenticated sync context.
- Conflicts are deterministic and logged.
- Orders already accepted by the server are never duplicated by retries.
- Subscription/offline lease state is cached only through signed, time-bounded server-issued data.

Offline support must not weaken tenant isolation or authorization.

## 9. Transaction and audit rules

For stock, order, POS and money-changing operations:

- use database transactions;
- preserve immutable financial snapshots where appropriate;
- link every stock or ledger movement to a source document;
- use reversal/refund/adjustment records instead of silent destructive rewrites;
- prevent duplicate posting through idempotency;
- use fixed-decimal money values, never floating point;
- prevent finalized daily closings from being silently rewritten;
- audit privileged changes and support access.

## 10. Deployment model

Deployment must remain compatible with cPanel/WHM and must not assume root access.

Recommended release layout:

```text
/home/<cpanel-user>/
├── current -> /home/<cpanel-user>/restaurant/releases/<release-id>
└── restaurant/
    ├── releases/<release-id>/
    └── shared/
        ├── .env
        └── storage/
```

Use versioned releases, shared environment/storage, an atomic `current` symlink switch, migration/backup checks, queue/scheduler configuration, TLS verification and smoke tests.

Environments remain isolated: local, staging and production.

## 11. Afghanistan defaults

- Primary currency: AFN.
- Timezone: Asia/Kabul.
- Languages: English, Dari and Pashto.
- Dari and Pashto remain RTL-safe.
- Core operation must not depend on externally hosted fonts or unstable third-party UI assets.
- Low-bandwidth mode is enabled by default.
- Server-side pagination is mandatory for large lists.
- API/sync payloads should be incremental and compact.

## 12. Product layers

1. BusinessOS central SaaS/control plane.
2. Restaurant tenant web application.
3. Android waiter application.
4. Kitchen Display / KOT workflow.
5. Cashier / POS workflow.
6. Inventory/purchasing/accounting.
7. Daily closing and reconciliation.
8. Offline synchronization engine.

This product is for **internal restaurant operations**. It is not a public food-delivery/customer ordering marketplace.

## 13. Quality gates

Before any tenancy implementation is considered complete, automated tests must prove isolation across:

- database queries/models;
- routes and authentication;
- cache;
- files/storage;
- queues/jobs;
- sessions;
- public URLs/tokens;
- reports and exports;
- mobile API/sync operations.

Two test restaurants must be unable to read or mutate one another’s records.

## 14. Batch boundary

Batch 1 established the Laravel/API/frontend foundation.

**Batch 2 must implement this multi-database tenancy architecture and its isolation tests before restaurant operational modules proceed.**

See `docs/architecture-blueprint.md` for the detailed ERD boundaries, contracts, permissions, plan rules, provisioning/deployment runbook, staged backlog and risk register.
