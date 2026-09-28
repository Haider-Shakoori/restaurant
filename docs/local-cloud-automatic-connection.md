# Mobile Connection Modes — Local, Cloud and Automatic

BusinessOS Restaurant waiter devices support three explicit connection modes while using the same /api/v1 contract.

## Local

Local mode connects to a single-restaurant Apache/Laravel server on the same LAN. Example: http://192.168.1.10.

The local Laravel installation must configure:

- RESTAURANT_LOCAL_SERVER_ENABLED=true
- RESTAURANT_LOCAL_TENANT_ID=<tenant-id>
- RESTAURANT_LOCAL_ALLOWED_HOSTS=192.168.1.10,restaurant.local

Use a static address or DHCP reservation for the restaurant server.

The tenant middleware initializes only the configured restaurant and only for exact allowed local hosts. Other LAN Host headers do not gain tenant access.

Private/local HTTP is accepted for LAN operation; public HTTP is rejected by the mobile connection resolver.

## Cloud

Cloud mode connects to the hosted restaurant tenant over HTTPS, for example https://restaurant.businessos.af.

## Automatic

Automatic accepts both local and cloud endpoints and resolves them in this order:

1. validate the local endpoint;
2. probe /api/v1/health on local with a short timeout;
3. use Local if it identifies a BusinessOS Restaurant tenant;
4. otherwise probe the HTTPS cloud endpoint;
5. use Cloud if valid;
6. report unreachable if neither endpoint is available.

The chosen endpoint, mode, active channel and tenant ID are persisted with the secure device session.

## Offline behavior

Once authenticated, the selected endpoint is the sync authority for that session. If it disappears, cached tables/menu/orders remain usable and permitted mutations continue into SQLite outbox. Sync resumes when that authority returns.

Automatic intentionally does not switch a live session between two independent databases. Seamless runtime failover requires replicated tenant state, device credentials and mutation authority to avoid split-brain orders.

## Android local HTTP

Android builds enable clear-text transport so private LAN Apache endpoints can work. Application-level validation still restricts clear-text URLs to private/local hosts, while cloud endpoints require HTTPS.
