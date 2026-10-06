# Local restaurant network

The Windows desktop application is the operational host for the restaurant LAN.

## Default endpoint

The local host listens on TCP port **8787** on all active interfaces.

Examples:

- `http://192.168.1.20:8787`
- `http://10.0.0.15:8787`

The Android app supports Local, Cloud, and Automatic connection modes. In Local/Automatic mode, configure the desktop LAN URL as the local server address.

## Local-first request flow

Android waiter app -> Windows desktop local API -> local SQLite operational database -> KOT/kitchen/cashier.

Cloud synchronization runs separately and is not required for an in-restaurant order to reach the kitchen.

## Pairing a waiter device

Local Android devices do not receive the desktop's cloud device secret and the desktop does not store restaurant user passwords.

1. The manager opens the Windows desktop app.
2. Select an active staff member in **Pair waiter device**.
3. Generate a six-digit pairing code.
4. The code is valid for five minutes and is single-use.
5. On Android choose **Local**, enter the desktop LAN address and pairing code.
6. The desktop creates a unique local Android device credential and local session token.
7. The Android device stores those values using platform secure storage.

Re-pairing the same Android installation rotates its local secret and invalidates its previous local sessions.

## Local API

Public LAN discovery:

- `GET /api/v1/health`
- `GET /api/v1/license/public-key`

Pairing:

- `POST /api/v1/local/pair`

Authenticated local operation:

- `POST /api/v1/license/lease`
- `GET /api/v1/sync/bootstrap`
- `POST /api/v1/sync/push`
- `GET /api/v1/sync/pull`

Desktop-only management:

- `POST /api/v1/local/admin/pairing-code` is restricted to the loopback interface.

## Offline order transaction

The Android app keeps its existing offline outbox. When the desktop is reachable, the same mutations are sent to the LAN host:

- `order.open`
- `order.item.add`
- `order.submit`

The desktop persists mutations idempotently. Re-sending the same mutation ID and content returns the original result; reusing an ID with different content is rejected.

A submitted local order is marked unsynchronized with the cloud but remains fully available to subsequent local KOT, kitchen and cashier workflows. Cloud reconciliation is intentionally separate from restaurant-floor availability.

## Licensing

The desktop host only operates while its authoritative cloud-signed offline lease is valid. Paired Android devices receive the current signed host entitlement for offline validation, but authenticate to the desktop with their own local device secret and session token.

## Firewall

The production installer will add the required Windows Firewall rule for the configured local host port. The rule should be scoped to private networks; the Kestrel host is not intended to be exposed directly to the public Internet.
