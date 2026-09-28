# BusinessOS Restaurant Waiter

This directory contains the offline-first Flutter waiter application for BusinessOS Restaurant.

It is an internal restaurant operations client, not a public customer ordering application.

## Operating model

The mobile app keeps operational data in SQLite and secure credentials in platform secure storage.

A waiter can:

1. activate the device with the restaurant license key;
2. verify and cache the signed Ed25519 offline lease;
3. sign in to the restaurant tenant;
4. cache tables, menu and active orders;
5. open a table/order while offline;
6. add menu items while offline;
7. queue the order for the kitchen while offline;
8. synchronize automatically when usable connectivity returns;
9. see deterministic conflicts instead of silently losing work.

The license key is used only for activation and is not persisted by the app. The device secret, access token, public verification key and signed lease are stored with Flutter secure storage.

## Durable outbox

SQLite contains a durable outbox. Every mobile mutation has a stable UUID and is sent in creation order.

Supported Batch 11 mutations:

- order.open
- order.item.add
- order.submit

The server records each device/mutation ID pair. Retrying an accepted mutation returns the previously accepted result and does not duplicate the order or line.

Transient network/server failures use bounded exponential retry. Domain conflicts are moved out of the retry loop and shown to the waiter.

## Conflict codes

The server returns deterministic conflict codes including:

- table_busy
- order_state_conflict
- menu_unavailable
- dependency_missing
- unsupported_operation
- invalid_payload
- mutation_id_reused

The local outbox remains inspectable until the mutation is accepted or explicitly classified as a conflict/rejection.

## Incremental pull

The tenant server emits a monotonic sync change sequence for mobile-visible menu, table and order data.

The mobile client stores the last sequence cursor, pulls compact pages, applies them transactionally to SQLite and resumes from the last durable cursor after interruption.

Server order snapshots are merged into local SQLite rather than deleting local lines first, protecting still-unsynced local work.

## Offline authorization

Offline mutations are allowed only while the locally cached server-signed lease verifies successfully and its offline/subscription expiry has not passed.

When the server returns a subscription lock, the local store records the lock and blocks new offline mutations. Existing local data is retained.

## Android platform scaffold

Batch 11 keeps Flutter business logic in source control and generates the standard Android platform scaffold in CI with Flutter's own project generator. CI then runs formatting, analysis, tests and an Android debug APK build.

Batch 12 owns the production Android packaging/signing and deployment artifacts.


## Three connection modes

The waiter setup screen supports Local, Cloud and Automatic connection modes.

Local connects directly to an Apache/Laravel restaurant server on the same LAN. Cloud connects to the restaurant HTTPS tenant endpoint. Automatic probes Local first and uses Cloud only when Local is unavailable during connection establishment.

Private/local HTTP is allowed only for LAN/local hosts. Public HTTP is rejected and cloud requires HTTPS.

The active route is stored with the device session and shown on the table dashboard. Automatic does not silently switch a live authenticated session between independent databases; if the selected server later disappears, SQLite/outbox operation continues offline until that same authority returns. True runtime local-to-cloud failover requires replicated tenant state and credentials to avoid split-brain orders.
