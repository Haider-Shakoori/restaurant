import 'dart:async';

import 'package:connectivity_plus/connectivity_plus.dart';

import 'sync_engine.dart';

class SyncCoordinator {
  SyncCoordinator({
    required SyncEngine engine,
    Connectivity? connectivity,
  }) : _engine = engine,
       _connectivity = connectivity ?? Connectivity();

  final SyncEngine _engine;
  final Connectivity _connectivity;

  StreamSubscription<List<ConnectivityResult>>? _connectivitySubscription;
  Timer? _timer;

  void start() {
    _connectivitySubscription ??= _connectivity.onConnectivityChanged.listen(
      (results) {
        if (results.any((result) => result != ConnectivityResult.none)) {
          unawaited(_safeSync());
        }
      },
    );

    _timer ??= Timer.periodic(
      const Duration(seconds: 30),
      (_) => unawaited(_safeSync()),
    );
  }

  Future<void> syncNow() => _safeSync();

  Future<void> _safeSync() async {
    try {
      await _engine.syncNow();
    } on Object {
      // SyncEngine persists the actionable error state for the UI.
    }
  }

  Future<void> dispose() async {
    _timer?.cancel();
    _timer = null;
    await _connectivitySubscription?.cancel();
    _connectivitySubscription = null;
  }
}
