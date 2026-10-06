# Desktop Batch 14 — Backup, restore, updater and diagnostics

Batch 14 adds local maintenance and recovery without changing the Android -> Desktop LAN authority model.

## Backup and restore

Backups use SQLite online backup, so an active database is not copied byte-for-byte while it may be changing. Each backup receives a SHA-256 fingerprint. Restore candidates must pass SQLite integrity checking and are staged instead of replacing the live database. A pre-restore safety copy is retained before a staged restore is swapped into place during startup, before local operational services open SQLite.

## Updater

The desktop updater accepts HTTPS manifests and HTTPS packages only. Packages are downloaded to the application data update area and must match the manifest SHA-256 before they can be considered staged. Batch 15 owns installer/signing integration; Batch 14 deliberately does not execute a downloaded package automatically.

## Diagnostics

The maintenance diagnostic snapshot reports database integrity/size, backup count, pending-restore state and free disk space. Diagnostic bundles contain operational metadata only; the SQLite database, activation material, terminal secrets and access tokens are excluded.

## Local-first and licensing boundaries

Backup, restore and diagnostics do not require cloud connectivity. They do not alter the signed offline lease or bypass licensing. Android waiter ordering continues through Desktop LAN -> SQLite. Cloud reconciliation remains secondary.

## Recovery rules

- Never restore directly over a running SQLite database.
- Never accept a restore that fails integrity checking.
- Keep a pre-restore safety copy.
- Never include credentials, license secrets or the restaurant database in diagnostic bundles.
- Never execute an update whose checksum is not verified.

## Release gate

Batch 14 may merge only after the repository's Windows desktop and cross-stack CI workflows complete successfully, including Laravel/PHP, frontend, Flutter and Android validation where configured.
