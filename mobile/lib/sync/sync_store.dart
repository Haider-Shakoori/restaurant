import '../data/local/outbox_mutation.dart';

abstract interface class SyncStore {
  Future<List<OutboxMutation>> pendingMutations({int limit = 50});

  Future<int> syncCursor();

  Future<void> applyBootstrap(Map<String, Object?> data);

  Future<void> applyPull(Map<String, Object?> data);

  Future<void> applyAcceptedResult(Map<String, Object?> result);

  Future<void> markConflict(
    OutboxMutation mutation,
    Map<String, Object?> result,
  );

  Future<void> markRetry(
    OutboxMutation mutation, {
    required String message,
    required DateTime retryAt,
  });

  Future<void> setSystemState(String key, String value);
}
