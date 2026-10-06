# LAN terminal management and degraded-network controls

Batch 12 hardens the BusinessOS Restaurant local-first operating model.

## Terminal lifecycle

Every successfully authenticated local terminal is tracked separately from its pairing credentials.

Runtime state records:

- device ID and display name
- terminal/client type
- app version
- last LAN IP address
- user agent
- first seen and last heartbeat timestamps
- enabled/disabled state
- disable audit actor and timestamp

A disabled terminal keeps its historical pairing record but is rejected by the local host until an owner or manager enables it again. Unpairing removes the cached local credential and runtime state, requiring the device to validate again through the normal licensing/authentication flow.

## Heartbeat behavior

The Flutter waiter app sends a local heartbeat during its normal 30-second synchronization cycle whenever its active connection channel is Local.

Every authenticated LAN request also refreshes terminal telemetry, so active KDS, waiter, cashier, or other local clients remain visible even when they are not creating orders.

Terminal health windows:

- **Online:** seen within 45 seconds
- **Stale:** seen between 45 seconds and 3 minutes
- **Offline:** not seen for more than 3 minutes
- **Disabled:** explicitly disabled by management

## Network operating modes

The desktop classifies cloud connectivity without making cloud availability a prerequisite for local service.

- **Healthy:** recent successful cloud reconciliation and no current cloud error
- **Degraded:** cloud reconciliation is delayed or recently failing
- **Local-only:** cloud has been unavailable for an extended period, cloud sync is disabled, or there is no successful cloud connection while local changes are queued

Local ordering, KOT, kitchen, cashier, inventory, and closing workflows remain authoritative on the LAN while the signed offline lease is valid.

Queued cloud mutations and reconciliation conflicts are shown in diagnostics and continue retrying through the existing reconciliation processor.

## Offline lease enforcement

The signed offline lease is now checked on every authenticated LAN request, not only when the desktop host starts.

If the lease expires:

- existing local data remains on disk
- the local host does not silently extend entitlement
- authenticated operational requests are rejected until a valid lease is available again

This preserves the same licensing rules used by the cloud and Android clients.

## Management endpoints

Authenticated owner/manager terminals can use:

- `POST /api/v1/local/heartbeat`
- `GET /api/v1/local/diagnostics`
- `GET /api/v1/local/terminals`
- `PUT /api/v1/local/terminals/{deviceId}/enabled`
- `DELETE /api/v1/local/terminals/{deviceId}`

A management terminal cannot disable or unpair itself through these endpoints.

## Windows dashboard

The desktop home screen now surfaces:

- LAN/network operating mode
- signed offline lease validity
- last successful cloud reconciliation
- pending cloud mutations
- open cloud conflicts
- online/stale/offline/disabled terminal counts
- terminal identity, role, app version, IP address, and last-seen time
- enable/disable and unpair controls for owner/manager sessions
