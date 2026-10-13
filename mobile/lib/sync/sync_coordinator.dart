import 'dart:async';

import 'package:connectivity_plus/connectivity_plus.dart';

import 'sync_engine.dart';

class SyncCoordinator {
  SyncCoordinator({
    required SyncEngine engine,
    Connectivity? connectivity,
    Future<void> Function()? onChanges,
    Duration watchWait = const Duration(seconds: 20),
  }) : _engine = engine,
       _connectivity = connectivity ?? Connectivity(),
       _onChanges = onChanges,
       _watchWait = watchWait;

  final SyncEngine _engine;
  final Connectivity _connectivity;

  /// Invoked after a LAN long-poll wake so ready-for-pickup detection can run
  /// without any visible widget (background LAN push). Null disables watching.
  final Future<void> Function()? _onChanges;
  final Duration _watchWait;

  StreamSubscription<List<ConnectivityResult>>? _connectivitySubscription;
  Timer? _timer;
  bool _watching = false;
  bool _disposed = false;

  void start() {
    _connectivitySubscription ??= _connectivity.onConnectivityChanged.listen(
      (results) {
        if (results.any((result) => result != ConnectivityResult.none)) {
          unawaited(_safeSync());
        }
      },
    );

    _timer ??= Timer.periodic(
      // Full sync (heartbeat, push, drain). The LAN long-poll loop below
      // delivers ready-for-pickup changes far faster when on the restaurant
      // network, so this timer stays modest to avoid needless HTTP.
      const Duration(seconds: 10),
      (_) => unawaited(_safeSync()),
    );

    // Reconcile the active table roster and stale server-backed orders on
    // every app startup, not only when the user manually presses Sync.
    unawaited(_initialBootstrap());
    unawaited(_watch());
  }

  Future<void> _initialBootstrap() async {
    try {
      await _engine.bootstrap();
      await _onChanges?.call();
    } on Object {
      // Keep existing SQLite data accessible offline. The engine persists
      // server errors for the connection diagnostics on the Tables screen.
    }
  }

  Future<void> syncNow() => _safeSync();

  /// Use on explicit table refresh; unlike the background sync, an error must
  /// be surfaced so staff do not mistake cached table data for live data.
  Future<void> syncAndRefresh() => _engine.syncAndRefresh();

  /// Interactive order submission must not falsely report success if the
  /// network push failed. The order remains safely queued in SQLite.
  Future<void> syncNowWithFeedback() => _engine.syncNow();

  Future<void> _safeSync() async {
    try {
      await _engine.syncNow();
    } on Object {
      // SyncEngine persists the actionable error state for the UI.
    }
  }

  /// Long-polls the restaurant LAN while a session exists. The desktop holds
  /// each request open until a change appears or the wait elapses, so a newly
  /// READY dish surfaces in about a second. When the active channel is not the
  /// LAN (or a request fails) the loop pauses; the 10-second timer keeps
  /// performing full syncs so ordering and the cloud relay stay responsive.
  Future<void> _watch() async {
    final onChanges = _onChanges;
    if (_watching || onChanges == null) {
      return;
    }

    _watching = true;
    while (!_disposed) {
      var usedLongPoll = false;
      var pause = const Duration(seconds: 4);

      try {
        usedLongPoll = await _engine.pullOnce(wait: _watchWait);
        if (usedLongPoll) {
          await onChanges();
          pause = Duration.zero;
        }
      } on Object {
        pause = const Duration(seconds: 5);
      }

      if (pause > Duration.zero) {
        await Future<void>.delayed(pause);
      }
    }

    _watching = false;
  }

  Future<void> dispose() async {
    _disposed = true;
    _timer?.cancel();
    _timer = null;
    await _connectivitySubscription?.cancel();
    _connectivitySubscription = null;
  }
}
