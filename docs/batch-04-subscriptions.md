# Batch 4 — Subscriptions, Trial Lifecycle and Plan Enforcement

## Delivered scope

Batch 4 makes central subscription state authoritative for hosted restaurant access.

### Commercial records

Central-only tables:

- `plan_prices` — configurable billing-cycle prices.
- `subscriptions` — immutable subscription-period snapshots.
- `subscription_events` — trial/renewal/cancellation audit trail.

Restaurant tenant databases do not receive subscription billing tables.

## Seven-day trial

The default hosted trial is seven days and is configurable through `PLATFORM_TRIAL_DAYS`.

A trial:

- starts only after the restaurant has a tenant, an active plan and provisioning state `ready`;
- uses server time;
- may be used only once per restaurant;
- snapshots the plan and feature entitlements at activation;
- never creates a payment record automatically.

## Renewal rules

Supported price cycles:

- monthly;
- quarterly;
- semiannual;
- annual;
- custom.

Early renewal begins at the latest existing future expiry so remaining paid/trial time is preserved.

Late renewal begins at the current server time.

Each renewal stores:

- plan code/name snapshot;
- feature-entitlement snapshot;
- price/currency snapshot;
- billing cycle and duration;
- start/end timestamps;
- operator identity;
- audit event.

Editing a plan or price later does not rewrite historical subscription periods.

## Server-side access enforcement

Tenant protected routes use `subscription.active`.

Access is calculated from server time on each request, independently of the scheduled lifecycle refresh job.

Open recovery/status routes remain available:

- tenant health;
- tenant subscription status.

Locked API/mobile requests receive HTTP **423 Locked** with a stable machine-readable code such as:

- `subscription_required`;
- `subscription_expired`;
- `subscription_due`;
- `subscription_cancelled`;
- `subscription_scheduled`.

Expiry/cancellation never deletes restaurant records.

## Feature enforcement

`plan.feature:<feature-key>` checks the active subscription's feature snapshot, not the mutable current plan record.

This prevents later plan edits from silently changing historical entitlements for an already-active subscription period.

## Lifecycle refresh

`php artisan subscriptions:refresh` synchronizes central display/status fields from authoritative server time.

The command is scheduled hourly. Security does not depend on it; request middleware still calculates expiry from timestamps directly.

## Current billing boundary

Batch 4 records subscription periods and configured prices. It does not claim that money was received.

Platform invoices, payment receipts/reconciliation or payment-provider workflows must use separate central billing records when implemented.

## Verification

Coverage includes:

- provisioning-gated trial;
- exact configurable trial duration;
- one-trial-only enforcement;
- early renewal stacking;
- late renewal from server time;
- custom-duration renewal;
- tenant 423 lock behavior;
- open health/subscription recovery endpoints;
- snapshot-based feature enforcement;
- cancellation without tenant data loss;
- lifecycle refresh command;
- Platform Admin price/trial/renewal HTTP actions;
- all previous tenancy-isolation regression tests.
