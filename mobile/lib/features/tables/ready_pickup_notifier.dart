import 'dart:async';

import 'package:flutter/services.dart';

import '../../core/models/session_credentials.dart';
import '../../core/notifications/restaurant_local_notifications.dart';
import '../../core/security/secure_credential_store.dart';
import '../../data/local/local_database.dart';
import 'ready_pickup_alerts.dart';
import 'ready_pickup_chime.dart';

/// One refresh's worth of ready-for-pickup state.
class ReadyPickupRefresh {
  const ReadyPickupRefresh({required this.alerts, required this.freshAlerts});

  /// Every still-open order with prepared food, for the in-app banner.
  final List<ReadyPickupAlert> alerts;

  /// Only alerts the waiter has not been told about yet; drives the sound,
  /// haptic and OS notification.
  final List<ReadyPickupAlert> freshAlerts;
}

/// Owns ready-for-pickup detection, de-duplication and the operating-system
/// notification so the behaviour is identical whether it is triggered by the
/// visible table screen or by the background LAN long-poll loop.
///
/// It reads only committed SQLite snapshots, so it never needs the network and
/// works with no internet: the LAN sync (or cloud sync, when connected) simply
/// has to have stored the READY state first.
class ReadyPickupNotifier {
  ReadyPickupNotifier({
    required LocalDatabase database,
    required CredentialStore credentials,
    required RestaurantLocalNotifications notifications,
    ReadyPickupChime? chime,
  }) : _database = database,
       _credentials = credentials,
       _notifications = notifications,
       _chime = chime ?? ReadyPickupChime();

  final LocalDatabase _database;
  final CredentialStore _credentials;
  final RestaurantLocalNotifications _notifications;
  final ReadyPickupChime _chime;

  ReadyPickupTracker? _tracker;
  String? _scope;
  Future<ReadyPickupRefresh>? _running;

  /// Clears in-memory de-duplication state on sign-out so the next waiter
  /// session starts from its own persisted acknowledged set.
  void reset() {
    _tracker = null;
    _scope = null;
  }

  /// Recomputes alerts, announces any newly-ready dishes once, and returns the
  /// current state. Concurrent callers share a single in-flight refresh so the
  /// banner and the notification cannot fire twice for the same dish.
  Future<ReadyPickupRefresh> refresh() {
    final running = _running;
    if (running != null) {
      return running;
    }

    final future = _refresh();
    _running = future;
    return future.whenComplete(() => _running = null);
  }

  Future<ReadyPickupRefresh> _refresh() async {
    final values = await Future.wait<Object?>([
      _database.activeOrders(),
      _database.tables(),
      _credentials.readSession(),
    ]);

    final alerts = readyPickupAlerts(
      List<Map<String, Object?>>.from(values[0]! as List<Map<String, Object?>>),
      List<Map<String, Object?>>.from(values[1]! as List<Map<String, Object?>>),
    );
    final session = values[2] as SessionCredentials?;
    // Never share an alert's acknowledged state across tenant installations.
    final scope = session?.tenantId ?? session?.deviceId ?? 'signed-out';
    final setting = 'ready_pickup_seen_$scope';
    if (_tracker == null || _scope != scope) {
      _scope = scope;
      _tracker = ReadyPickupTracker.decode(await _database.systemState(setting));
    }

    final freshAlerts = _tracker!.newlyReady(alerts);
    if (freshAlerts.isEmpty) {
      return ReadyPickupRefresh(alerts: alerts, freshAlerts: const []);
    }

    await _database.setSystemState(setting, _tracker!.encode());

    if (session == null) {
      return ReadyPickupRefresh(alerts: alerts, freshAlerts: const []);
    }

    unawaited(_chime.play());
    unawaited(HapticFeedback.heavyImpact());
    unawaited(_notifications.showReadyPickup(freshAlerts));

    return ReadyPickupRefresh(alerts: alerts, freshAlerts: freshAlerts);
  }
}
