import '../core/api/mobile_api_client.dart';
import '../core/connection/connection_mode.dart';
import '../core/models/session_credentials.dart';
import '../core/security/offline_lease_verifier.dart';
import '../core/security/secure_credential_store.dart';
import '../data/local/outbox_mutation.dart';
import 'sync_store.dart';

class SyncEngine {
  SyncEngine({
    required SyncApi api,
    required SyncStore store,
    required CredentialStore credentials,
    required LeaseValidator leaseVerifier,
  }) : _api = api,
       _store = store,
       _credentials = credentials,
       _leaseVerifier = leaseVerifier;

  final SyncApi _api;
  final SyncStore _store;
  final CredentialStore _credentials;
  final LeaseValidator _leaseVerifier;

  bool _running = false;

  Future<void> bootstrap() async {
    final session = await _requireSession();
    final refreshed = await _refreshLeaseIfNeeded(session);
    final data = await _api.syncBootstrap(refreshed);
    await _store.applyBootstrap(data);
  }

  Future<void> syncNow() async {
    if (_running) {
      return;
    }

    _running = true;

    try {
      var session = await _requireSession();
      session = await _refreshLeaseIfNeeded(session);
      await _push(session);
      await _pull(session);
      await _store.setSystemState('last_sync_error', '');
      await _store.setSystemState(
        'last_sync_at',
        DateTime.now().toUtc().toIso8601String(),
      );
    } on ApiException catch (error) {
      await _store.setSystemState('last_sync_error', error.code);

      if (error.code == 'subscription_locked') {
        await _store.setSystemState('server_locked', '1');
      }

      rethrow;
    } finally {
      _running = false;
    }
  }

  Future<void> _push(SessionCredentials session) async {
    final mutations = await _store.pendingMutations();

    if (mutations.isEmpty) {
      return;
    }

    try {
      final response = await _api.push(
        session,
        mutations.map((mutation) => mutation.toApiJson()).toList(),
      );
      final rawResults = response['results'] as List<Object?>? ?? const [];
      final byId = <String, Map<String, Object?>>{
        for (final value in rawResults)
          if (value is Map<Object?, Object?>)
            value['mutation_id'].toString(): Map<String, Object?>.from(value),
      };

      for (final mutation in mutations) {
        final result = byId[mutation.mutationId];

        if (result == null) {
          await _markRetry(mutation, 'missing_server_result');
          continue;
        }

        switch (result['status']) {
          case 'accepted':
            await _store.applyAcceptedResult(result);
            break;
          case 'conflict':
          case 'rejected':
            await _store.markConflict(mutation, result);
            break;
          default:
            await _markRetry(mutation, 'unknown_server_result');
        }
      }
    } on ApiException catch (error) {
      if (_isRetryable(error)) {
        for (final mutation in mutations) {
          await _markRetry(mutation, error.code);
        }
      }

      rethrow;
    }
  }

  Future<void> _pull(SessionCredentials session) async {
    var cursor = await _store.syncCursor();

    for (var page = 0; page < 10; page++) {
      final response = await _api.pull(
        session,
        cursor: cursor,
        limit: 100,
      );
      await _store.applyPull(response);

      cursor = (response['cursor'] as num?)?.toInt() ?? cursor;
      final hasMore = response['has_more'] == true;

      if (!hasMore) {
        return;
      }
    }
  }

  Future<SessionCredentials> _refreshLeaseIfNeeded(
    SessionCredentials session,
  ) async {
    final verification = await _leaseVerifier.verify(
      signedLease: session.lease,
      publicKey: session.publicKey,
      expectedDeviceId: session.activeChannel == ConnectionChannel.local
          ? null
          : session.deviceId,
    );

    final expiresAt = verification.expiresAt;
    final needsRefresh = !verification.valid ||
        expiresAt == null ||
        expiresAt.difference(DateTime.now().toUtc()) < const Duration(hours: 12);

    if (!needsRefresh) {
      return session;
    }

    final response = await _api.refreshLease(session);
    final lease = Map<String, Object?>.from(
      response['lease']! as Map<Object?, Object?>,
    );
    final refreshedVerification = await _leaseVerifier.verify(
      signedLease: lease,
      publicKey: session.publicKey,
      expectedDeviceId: session.activeChannel == ConnectionChannel.local
          ? null
          : session.deviceId,
    );

    if (!refreshedVerification.valid) {
      throw const ApiException(
        code: 'invalid_offline_lease',
        message: 'The refreshed offline lease failed signature validation.',
      );
    }

    await _credentials.saveLease(lease);
    return session.copyWith(lease: lease);
  }

  Future<SessionCredentials> _requireSession() async {
    final session = await _credentials.readSession();

    if (session == null) {
      throw const ApiException(
        code: 'not_configured',
        message: 'This device has not been activated and signed in.',
      );
    }

    return session;
  }

  Future<void> _markRetry(OutboxMutation mutation, String message) {
    final exponent = mutation.attempts.clamp(0, 6).toInt();
    final seconds = (5 * (1 << exponent)).clamp(5, 300).toInt();

    return _store.markRetry(
      mutation,
      message: message,
      retryAt: DateTime.now().toUtc().add(Duration(seconds: seconds)),
    );
  }

  bool _isRetryable(ApiException error) {
    return error.statusCode == null ||
        (error.statusCode != null && error.statusCode! >= 500);
  }
}
