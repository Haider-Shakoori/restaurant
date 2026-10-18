# Waiter mobile release gates

This app is offline-first. A waiter must be able to take orders on a private
restaurant LAN when the internet is unavailable. Production releases require
all automated checks **and** physical iOS/Windows LAN checks below.

## Automated gates

- [ ] `flutter analyze` clean; all Flutter unit/widget tests green
- [ ] SQLite migration test from the previously released iPhone database
- [ ] Menu without modifiers can be quick-added exactly once per tap
- [ ] Required/optional modifier groups always open the options form
- [ ] Mutations have durable IDs and are never duplicated on retry
- [ ] Sending a KOT twice without new unsent items cannot create a new round
- [ ] Partial desktop order events retain price, modifiers, table and quantity
- [ ] Closed/cancelled order edits are rejected without removing local audit
- [ ] Kitchen ready, partially ready, served and completed round states stay distinct
- [ ] No active ticket is hidden just because a different ticket is complete
- [ ] Conflict acknowledgement never replays or falsely reports delivery
- [ ] Offline table statuses are explicitly labeled cached

## Real restaurant acceptance flow

1. Put a Windows desktop and two waiters' iPhones on the **same Wi-Fi**.
2. Open one table from waiter A; send two items with different kitchen stations.
3. Waiter B refreshes the floor. It must show the table occupied, never open a
   second overlapping dining order.
4. Retry the send during a transient timeout; the kitchen should receive one
   idempotent KOT, not multiple duplicates.
5. Add a new item to the existing order. Only the new quantity should be
   eligible for the next KOT round.
6. Mark one kitchen item Ready and leave the other Preparing. The phone shows
   **Partially ready**, with one deduplicated pickup alert for the new item.
7. Mark all food Ready. Both screens must agree; staff can inspect the ticket
   and keep the order available for serving and payment.
8. Serve/close the order in POS. Only historical KOTs remain under History.
9. Disable restaurant internet but keep Wi-Fi LAN enabled. Repeat ordering,
   kitchen delivery and pickup; no cloud connection is required for LAN mode.
10. Disconnect waiter A from LAN. Add a permitted unsent order offline,
    reconnect, and verify exactly-once replay with no missing line.
11. Test a completely-offline subscription, ensuring cloud sync is never
    required. Test expired/locked lease behavior independently.
12. Validate tablet layout, narrow iPhone, larger text, and directionality,
    including multilingual translations when shipped.

## iOS notification gates

- [ ] With restaurant app in foreground: local notification with sound on Ready
- [ ] With app behind another app: test actual iOS delivery, not simulated events
- [ ] With iPhone locked: test real signed APNs delivery if push is offered
- [ ] Confirm iOS notification permission, Focus, silent-mode behavior and
      de-duplication before promising audible delivery in any state

Unsigned/sideloaded builds and LAN long polling alone are **not** proof of
reliable suspended/locked-iPhone notifications. Signed push capabilities and
APNs configuration are separate prerequisites for dependable background pushes.

## Release checklist

- [ ] Mobile/desktop regression gates green
- [ ] At least one full live two-waiter pilot run, logs and result recorded
- [ ] Upgrade installs over existing IPA without deleting offline data
- [ ] Backups and recovery path tested against a failed installation
- [ ] Only after those checks, ship the final IPA and any Windows installer
