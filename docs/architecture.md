# Architecture Foundation

BusinessOS Restaurant is organized as a commercial SaaS platform rather than a single restaurant POS.

## Product layers

1. BusinessOS SaaS platform / control plane.
2. Restaurant web management application.
3. Android waiter application.
4. Kitchen Display / KOT workflow.
5. Cashier / POS workflow.
6. Offline synchronization engine.

## Foundation rules

- Laravel remains Blade-first; do not introduce a heavy SPA without a specific need.
- APIs are versioned from the first release.
- Server-side authorization is mandatory for every privileged action.
- Tenant ownership will be enforced across queries, policies, jobs, reports and exports.
- Money must use fixed decimal database values, never floating point.
- Offline-originated transactional records will use ULID/UUID identifiers.
- Critical mutations must support idempotency.
- Finalized financial activity must use reversal/refund/adjustment rather than destructive edits.
- The waiter experience must remain usable when external internet becomes unavailable.
- Subscription state is authoritative on the server and later extended through signed offline leases.

## Afghanistan defaults

- Currency: AFN.
- Timezone: Asia/Kabul.
- Languages: English, Dari and Pashto.
- Dari and Pashto are RTL.
- Core operation must not depend on externally hosted fonts.
- Low-bandwidth mode is enabled by default.
- API payloads and mobile synchronization should remain incremental and compact.

## Batch boundaries

Batch 1 intentionally establishes framework, API, frontend, localization metadata, performance defaults and CI only.

Batch 2 introduces strong multi-tenancy and automated isolation tests. Subscription plans, seven-day trials and licensing follow in later batches before restaurant ordering is built. This keeps the implementation reviewable and prevents unrelated concerns from being mixed into one large change.