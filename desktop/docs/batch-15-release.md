# Desktop Batch 15 — Release packaging

Batch 15 turns the validated local-first desktop host into a supportable Windows release without changing operational authority or licensing rules.

## Release contract

- Product branding: BusinessOS Restaurant by BusinessOS.af.
- Windows package is produced from the tested .NET desktop solution.
- The LAN listener remains local restaurant infrastructure; cloud availability is not required for service within a valid signed offline lease.
- Firewall configuration must be narrowly scoped to the configured desktop LAN port and private/domain networks. Public-network exposure is not enabled by default.
- Installer/update artifacts must be checksum verified before staging or execution.
- Production releases are signed only when the CI signing certificate secret is present; unsigned development artifacts must never be presented as production-signed builds.
- Signing credentials are never committed to the repository or included in diagnostics.

## Installation and recovery

The installer must preserve the application data directory so upgrades do not remove SQLite operational data, activation material, backups, pending restore state, or diagnostics. Uninstall must not silently delete restaurant data.

## Architecture boundary

Android waiter -> Desktop LAN host -> local SQLite -> KOT/Kitchen/Cashier remains authoritative. Laravel remains the SaaS control plane and eventual reconciliation target.
