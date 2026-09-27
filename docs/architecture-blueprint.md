# BusinessOS Restaurant — Architecture Blueprint

This blueprint adapts the supplied SaaS reference architecture to the restaurant product. Pharmacy-specific domain rules are intentionally excluded.

## 1. Concise PRD and module map

### Product goal

Provide a hosted multi-tenant restaurant management platform for Afghanistan-first operation, with a central BusinessOS control plane and an isolated restaurant application per tenant.

### Primary tenant workflow

**Waiter → Table → Order → KOT/KDS → Preparation → Ready → Serve → Bill → Payment → Cashier/Day Close**

### Central SaaS modules

- public product/pricing/trial pages;
- tenant signup and subdomain reservation;
- tenant provisioning;
- central operator/admin authentication;
- restaurant customer/CRM record;
- plans and feature gates;
- seven-day hosted trials;
- subscriptions and renewal;
- platform invoices/payments;
- provisioning health/retry;
- offline license/lease administration;
- operator audit;
- SaaS business reports.

### Restaurant tenant modules

- restaurant dashboard;
- branches and locations;
- users, roles and permissions;
- dining areas/tables;
- menu/categories/items/modifiers;
- waiters and shifts;
- table/order lifecycle;
- KOT/KDS;
- cashier/POS;
- discounts/refunds/split payments;
- daily closing and reconciliation;
- inventory/recipes/BOM where enabled;
- purchasing/suppliers;
- expenses/accounts/ledger;
- reporting/exports;
- restaurant settings/localization;
- Android waiter offline sync.

## 2. Central / tenant ERD boundary

### Central relationships

```text
businesses/restaurants
    ├── hasOne tenant (infrastructure)
    ├── hasMany subscriptions
    ├── belongsTo plan/current plan assignment
    ├── hasMany platform_invoices
    ├── hasMany platform_payments
    └── hasMany provisioning_events

tenants
    ├── hasMany domains
    └── points to tenant database/infrastructure metadata

plans
    └── hasMany plan_features

platform_invoices
    ├── hasMany invoice_lines
    └── hasMany platform_payments
```

Central records may know the tenant/business identifier and commercial status, but not routine restaurant operational rows.

### Tenant relationships

```text
users --< role assignments >-- roles --< permissions

branches
    ├── hasMany dining_sections
    ├── hasMany tables
    ├── hasMany cashier_sessions
    └── hasMany storage_locations

tables
    └── hasMany orders

orders
    ├── belongsTo table
    ├── belongsTo waiter/user
    ├── hasMany order_lines
    ├── hasMany kitchen_tickets
    ├── hasMany bills
    └── hasMany audit/status events

bills
    ├── hasMany bill_lines/snapshots
    ├── hasMany tenant_payments
    ├── hasMany discounts
    └── hasMany refunds/adjustments

menu_items
    ├── belongsTo category
    ├── hasMany modifiers
    └── optionally maps to recipes/BOM

inventory_items
    ├── hasMany stock_movements
    └── participates in recipes/purchases

daily_closings
    ├── summarize cashier sessions
    └── reconcile source payments/ledger entries
```

### Boundary rule

Never add a foreign key from a tenant operational table to the central database. Cross-database identity is carried only by the tenant context and stable identifiers controlled by provisioning.

## 3. Routes and API contracts

### Central web

- `GET /` — product landing.
- `GET /pricing`.
- `GET|POST /trial`.
- `GET|POST /login` — central operators where separate UI is used.
- `/admin/*` — central SaaS control plane.

### Central API

- `GET /api/v1/health`.
- `POST /api/v1/platform/trials`.
- `GET /api/v1/platform/provisioning/{id}`.
- central operator APIs remain under an explicit `platform` namespace.

### Tenant web

Resolved only after tenant identification:

- `/login`, `/logout`;
- `/dashboard`;
- `/tables`;
- `/orders`;
- `/kitchen`;
- `/pos`;
- `/inventory`;
- `/purchases`;
- `/accounts`;
- `/reports`;
- `/settings`.

### Tenant/mobile API

Tenant domain plus versioned API:

- `POST /api/v1/auth/login`;
- `POST /api/v1/auth/logout`;
- `GET /api/v1/bootstrap` — compact tenant/user/device bootstrap;
- `GET /api/v1/menu`;
- `GET /api/v1/tables`;
- `POST /api/v1/orders`;
- `PATCH /api/v1/orders/{order}`;
- `POST /api/v1/orders/{order}/items`;
- `POST /api/v1/sync/push`;
- `GET /api/v1/sync/pull`;
- `GET /api/v1/subscription/lease` when offline lease support is enabled.

Every tenant API request is authorized after tenant resolution. IDs alone never select a different tenant database.

### Machine-readable lock response

Expired/locked tenant API responses should use a stable HTTP status and payload contract, for example:

```json
{
  "code": "subscription_locked",
  "message": "Subscription renewal is required.",
  "renewal_contact": "...",
  "tenant_status": "expired"
}
```

The exact status code will be standardized before the endpoint is released.

## 4. Permissions matrix

| Capability | Owner/Admin | Manager | Waiter | Kitchen | Cashier | Inventory | Accountant | Auditor |
|---|---|---|---|---|---|---|---|---|
| Manage users/roles | Yes | Optional | No | No | No | No | No | Read |
| Configure restaurant | Yes | Limited | No | No | No | No | No | Read |
| Manage menu/prices | Yes | Yes | No | No | Limited | No | No | Read |
| Open table/order | Yes | Yes | Yes | No | Yes | No | No | Read |
| Send KOT | Yes | Yes | Yes | No | Yes | No | No | Read |
| Change kitchen status | Yes | Yes | No | Yes | No | No | No | Read |
| Apply discount | Yes | Permission | Limited permission | No | Permission | No | No | Read |
| Take payment | Yes | Yes | Limited | No | Yes | No | No | Read |
| Refund/void finalized bill | Yes | Permission | No | No | Permission | No | No | Read |
| Adjust stock | Yes | Permission | No | No | No | Yes | No | Read |
| Purchase stock | Yes | Permission | No | No | No | Yes | No | Read |
| Post expenses/ledger | Yes | Permission | No | No | Limited | Limited | Yes | Read |
| Daily close/reopen | Yes | Permission | No | No | Close | No | Accountant permission | Read |
| Financial exports | Yes | Permission | No | No | Limited | Limited | Yes | Read |

All permissions are server-side policies/abilities. UI visibility is only a convenience layer.

## 5. Configurable pricing and plan matrix

The commercial model is configurable and historical invoice snapshots are immutable.

### Subscription state

- provisioning;
- trial;
- active;
- due;
- expired;
- cancelled.

### Default trial

- seven days;
- starts only after successful tenant provisioning/activation;
- no payment is recorded automatically.

### Plan dimensions

Possible feature gates:

- number of branches;
- waiter accounts;
- KDS screens;
- advanced inventory;
- accounting;
- advanced reports;
- exports;
- custom roles;
- offline lease duration/features;
- API integrations.

Do not hard-code Starter/Premium as the only lifetime choices; store plans/features as data.

Early renewal extends future expiry. Late renewal starts from the new activation/payment date according to the configured policy. Expiry locks access without deleting restaurant data.

## 6. Provision / deploy / backup runbook

### Provision

1. Validate business and contact fields.
2. Normalize and reserve subdomain; reject reserved names.
3. Create central business/commercial record with `provisioning` status.
4. Create central tenant/infrastructure record.
5. Allocate/create tenant database.
6. Apply correct charset/collation.
7. Run tenant-only migrations.
8. Seed tenant roles, permissions, restaurant settings, AFN defaults and owner user.
9. Map tenant domain.
10. Request/verify TLS.
11. Run tenant smoke checks.
12. Mark tenant ready.
13. Start trial.
14. Hand user to tenant login through explicit or one-time tenant-bound flow.

### Deploy

1. Build release in a versioned directory.
2. Install Composer dependencies with production flags.
3. Build frontend assets.
4. Run central migration safety check.
5. Back up central DB.
6. Back up tenant DBs affected by a migration.
7. Run central migrations.
8. Run tenant migrations through tenancy-aware command.
9. Verify tenant migration results.
10. Switch `current` symlink atomically.
11. restart/reload workers using the hosting-compatible mechanism.
12. run central and tenant smoke tests.

### Rollback

- application rollback uses prior release symlink;
- database rollback requires explicit migration-specific strategy;
- never drop a tenant DB as part of an automated rollback;
- preserve tenant operational records;
- prefer additive/backwards-compatible migrations.

### Backup

Backups must include central DB, every tenant DB, shared storage and tenant-private files as applicable. Restore drills must be tested, not assumed.

## 7. Milestone backlog and dependencies

### Batch 2 — Multi-tenancy and tenant isolation

Depends on Batch 1.

Deliver:

- stancl/tenancy v3 installation/configuration;
- central Tenant/Domain infrastructure;
- central vs tenant migrations;
- domain identification;
- dynamic DB switching;
- cache/filesystem/queue/session boundary decisions;
- tenant route file/provider;
- central domain protection;
- two-tenant isolation tests.

Acceptance: two test restaurants cannot access one another’s records by URL, model query, job, cache, storage, report or API.

### Batch 3 — Central SaaS provisioning

Depends on Batch 2.

Deliver central business records, plans/features, trial registration, safe provisioning state machine, owner bootstrap, domain/TLS readiness and provisioning retry.

### Batch 4 — Subscription and licensing

Depends on Batch 3.

Deliver lifecycle enforcement, renewal rules, platform billing records, machine-readable locks and signed offline lease foundation.

### Batch 5 — Tenant auth / roles / restaurant setup

Depends on tenancy and provisioning.

### Batch 6 — Tables / menu / waiter ordering

Build restaurant golden path only after isolation, subscriptions and tenant auth are proven.

### Batch 7 — KOT/KDS

### Batch 8 — Cashier/POS, payments, daily closing

### Batch 9 — Inventory/purchasing/recipes

### Batch 10 — Accounting/reporting

### Batch 11 — Flutter offline sync hardening

### Batch 12 — production subdomain/deployment and full regression

Batch numbering may be expanded, but dependencies must not be bypassed.

## 8. Security and compliance risk register

### Critical

**Cross-tenant data exposure**
- Mitigation: database-per-tenant, domain identification, tenant-aware policies, isolated cache/storage/jobs/sessions, adversarial tests.

**Provisioning into the wrong database**
- Mitigation: explicit central/tenant connections, tenant-only migration command, database name verification, test assertions.

**Subscription expiry destroys or corrupts operations**
- Mitigation: authorization/lock middleware only; no data deletion.

**Offline replay duplicates orders or payments**
- Mitigation: stable client IDs, idempotency keys, unique constraints and transactional writes.

**Support/operator access becomes implicit**
- Mitigation: no central operational queries; explicit support access workflow, limited permissions and audit trail if added.

### High

**Cross-subdomain session leakage**
- Mitigation: tenant-specific session strategy and no accidental shared authentication cookie dependency.

**Queue job runs in wrong tenant**
- Mitigation: tenant context serialization/bootstrap and tests.

**File/cache key collision**
- Mitigation: tenancy bootstrapper/prefix/root isolation and tests.

**Double-posted finance/stock**
- Mitigation: atomic transactions, source-document uniqueness, immutable snapshots and reversals.

**Daily closing silently rewritten**
- Mitigation: finalize/lock, permissioned reopen, audit and adjustment trail.

### Operational

**cPanel limitations**
- Mitigation: no root-only assumptions; use supported cPanel APIs/jobs/cron patterns.

**Poor internet**
- Mitigation: server-side pagination, compact API payloads, local waiter outbox, incremental sync and no external font dependency.

**RTL/localization regressions**
- Mitigation: English/Dari/Pashto test fixtures and UI regression coverage.

## 9. Architecture approval assumptions

The following are treated as architectural defaults unless explicitly changed:

1. One hosted database per restaurant tenant.
2. One shared Laravel codebase serves all hosted tenants.
3. Dedicated subdomain/domain identifies the tenant.
4. Central commercial/SaaS data and restaurant operational data remain separate.
5. Multi-branch restaurants keep branches inside the tenant database.
6. Seven-day trial begins after successful provisioning.
7. Expiry locks access without deleting data.
8. Android waiter app remains offline-first.
9. AFN and Asia/Kabul are defaults.
10. Production deployment remains cPanel/WHM compatible.

These assumptions are now the baseline for future implementation batches.
