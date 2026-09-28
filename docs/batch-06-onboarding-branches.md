# Batch 6 — Restaurant Onboarding and Branches

## Delivered scope

Batch 6 starts the restaurant tenant application after the SaaS, subscription and licensing foundations.

It implements:

- tenant restaurant login/logout;
- tenant-aware guest redirects;
- active-subscription enforcement for onboarding and branch management;
- a resumable 13-step onboarding progress record;
- restaurant profile setup;
- primary branch setup;
- multi-branch management;
- plan-driven `max_branches` enforcement;
- English/Dari/Pashto locale selection;
- RTL direction for Dari and Pashto;
- Afghanistan defaults: AF, AFN and Asia/Kabul.

## Tenant-only database additions

These tables exist only in each restaurant tenant database:

- `restaurants`;
- `branches`;
- `onboarding_progress`.

No restaurant profile or branch operational data is written into the central BusinessOS database.

### Restaurant

One primary restaurant profile is maintained per tenant using the unique `profile_key=primary` convention.

Important fields:

- name and contact information;
- address;
- country code;
- currency;
- timezone;
- locale;
- active state.

### Branch

A branch belongs to the tenant restaurant and stores:

- ULID;
- tenant-local code;
- name;
- phone/address;
- primary flag;
- active flag.

Branch codes are unique inside the restaurant.

## Branch invariants

- The first branch automatically becomes primary.
- Promoting a branch to primary clears the primary flag from the others.
- The current primary branch cannot be silently deactivated or demoted if doing so would leave no active primary branch.
- Branches are deactivated rather than destructively deleted in this batch.
- Active branch count respects the subscription snapshot feature `max_branches` when configured.
- No plan name is hard-coded.

## Onboarding sequence

The persistent onboarding engine knows the complete product sequence:

1. Restaurant
2. Branch
3. Floors / Sections
4. Tables
5. Kitchen Stations
6. Menu Categories
7. Menu Items
8. Employees
9. Waiter Assignments
10. Printer
11. Android Devices
12. Opening Inventory
13. Finish

Batch 6 enables steps 1 and 2.

The remaining steps are shown as future stages but do not pretend to be implemented. Their scheduled batches will attach to the same `onboarding_progress` record.

## Authentication and subscription behavior

Tenant login is available independently of subscription state so staff can reach recovery/renewal flows.

Protected tenant setup routes require:

1. domain-based tenant resolution;
2. authenticated tenant user;
3. active trial/subscription.

The Batch 4 behavior is preserved: an expired tenant hitting the protected tenant root receives the existing HTTP 423 subscription lock response rather than losing data or being redirected as if the tenant did not exist.

## Localization

Restaurant locale is applied by tenant middleware when the restaurant profile exists.

Supported defaults:

- English — LTR;
- Dari — RTL;
- Pashto — RTL.

The UI uses local/system fonts and does not add an external font dependency.

## Deferred scope

Batch 6 intentionally does not implement:

- roles and granular tenant permissions — Batch 7;
- floors, sections and tables — Batch 8;
- menu categories/items — Batch 9;
- modifiers — Batch 10;
- waiter assignments/shifts — Batch 11;
- ordering/KOT/KDS/POS/inventory modules — later assigned batches.

## Verification

Batch 6 regression coverage verifies:

- new tables exist only in tenant databases;
- tenant login remains possible while protected onboarding is subscription-locked;
- restaurant and primary branch onboarding is resumable;
- progress advances to Floors / Sections after Batch 6 steps;
- AFN and Asia/Kabul defaults persist;
- Dari switches tenant UI to RTL;
- `max_branches` is enforced server-side;
- the primary branch cannot be removed without replacement;
- branch records cannot cross tenant database boundaries;
- existing subscription lock and license activation/lease tests remain green.
