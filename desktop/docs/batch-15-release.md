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

## Batch 15 implementation checkpoint (2026-10-09)

- Optional signing remains supported. With no signing certificate path configured, `Build-Release.ps1` publishes an **unsigned development/CI EXE** and reports `SIGNED_RELEASE=false`; no certificate is required for CI packaging.
- When signing is explicitly configured, the script now fails clearly if the certificate file is missing or its password is blank. It retains the existing Authenticode signing and signature verification path. Signing secrets are not printed or committed.
- The Windows CI release validation explicitly checks `Get-AuthenticodeSignature` returns `NotSigned` for the CI EXE. A signed or otherwise unexpected signature state fails the unsigned artifact job instead of silently mislabeling it.
- Existing CI steps remain responsible for .NET restore/build/tests, Windows single-file publish, packaged-app startup smoke, branded Inno Setup build/startup smoke, firewall scope checks, release SHA-256 manifest and three artifact uploads.
- The existing manifest binds the executable and installer hashes to the GitHub source commit and labels the build `unsigned_ci`. Consumers must independently compare downloaded file hashes to the matching manifest before use.
- **Acceptance:** the optional-signing preflight commit `c2f9c30920575b9f2f8541a000996bb00ff2b63c` passed both Restaurant Desktop CI and general CI. The subsequent unsigned-artifact CI assertion and this documentation update require fresh CI on the final PR head before Batch 15 is declared automated-CI complete.
- **Not production-approved:** no Authenticode certificate, real Windows installer upgrade/uninstall and data-retention exercise, 4K screenshot validation, LAN/offline recovery, thermal printer test or full 30-step golden path is claimed. PR #69 remains draft; do not merge or distribute unsigned CI artifacts as production-signed.
