# Batch 3 — Platform Admin / Central SaaS Control Plane

## Delivered scope

Batch 3 adds the central BusinessOS Restaurant operator application.

### Central entities

- `admin_users` — BusinessOS platform operators only.
- `businesses` — commercial restaurant/customer records.
- `plans` and `plan_features` — configurable entitlement foundation.
- `provisioning_events` — central infrastructure/provisioning audit history.
- existing `tenants` and `domains` — infrastructure identity and domain mapping.

No restaurant order, KOT, POS payment, inventory, employee, customer or accounting tables were added to the central database.

## Platform roles

- **Super Admin** — full central control, including operator management.
- **Operator** — manages restaurant commercial records and plan configuration.
- **Support** — read-only central visibility.

Disabled operator accounts cannot use the Platform Admin.

## Restaurant lifecycle boundary

Creating a restaurant in Platform Admin creates only a commercial record with:

- status: `provisioning`;
- provisioning state: `pending`;
- optional requested subdomain;
- contact/location metadata;
- optional plan/operator assignment;
- a `business.created` provisioning event.

It deliberately does **not** create tenant infrastructure directly. The cPanel-aware provisioning workflow will be implemented separately so production cannot accidentally bypass domain, database, TLS, migration and health checks.

## Provisioning visibility

Platform Admin shows:

- infrastructure tenant count;
- tenant IDs and domains;
- provisioning state;
- provisioning failure count;
- latest provisioning events;
- last health timestamp where available.

Deleting central tenant metadata still does not automatically delete an operational database.

## Plan foundation

Plans and feature entitlements are database-driven rather than hard-coded. Batch 3 does not create subscription charges, invoices or platform payment records. Those money flows belong to the subscription/billing batch.

## Security controls

- platform login rate limiting;
- active-account enforcement;
- server-side Gate authorization;
- reserved subdomain blocking;
- central-domain-only Platform Admin routes;
- tenant domains cannot access Platform Admin routes;
- no implicit Platform Admin query into tenant operating databases.

## Verification

The Batch 3 suite covers login, disabled accounts, role authorization, restaurant creation, provisioning audit events, configurable plans/features, operator creation, reserved subdomains, Platform Admin view rendering, central-domain restrictions and the full Batch 2 isolation suite.
