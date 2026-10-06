# Local restaurant network

The Windows desktop application is the operational host for the restaurant LAN.

## Default endpoint

The local host listens on TCP port **8787** on all active interfaces.

Examples:

- `http://192.168.1.20:8787`
- `http://10.0.0.15:8787`

The Android app already supports Local, Cloud, and Automatic connection modes. In Local/Automatic mode, configure the desktop LAN URL as the local server address.

## Local-first request flow

Android waiter app -> Windows desktop local API -> local operational database -> KOT/kitchen/cashier.

Cloud synchronization runs separately and is not required for an in-restaurant order to reach the kitchen.

## Health contract

`GET /api/v1/health`

The response deliberately matches the mobile connection probe contract and includes:

- `status`
- `service`
- `tenant_id`
- `mode`
- `api_version`
- `server_time`

## Security

The local host only runs for an activated installation with a valid signed offline lease. Device/user authentication is applied to operational endpoints in subsequent batches.

Production installer work will add the required Windows Firewall rule for the configured local host port.
