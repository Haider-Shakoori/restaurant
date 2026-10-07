import '../core/api/mobile_api_client.dart';
import '../core/connection/connection_mode.dart';
import '../core/connection/connection_resolver.dart';
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
    ConnectionResolver? connectionResolver,
  }) : _api = api,
       _store = store,
       _credentials = credentials,
       _leaseVerifier = leaseVerifier,
       _connectionResolver = connectionResolver;

  final SyncApi _api;
  final SyncStore _store;
  final CredentialStore _credentials;
  final LeaseValidator _leaseVerifier;
  final ConnectionResolver? _connectionResolver;

  bool _running = false;

  Future<void> bootstrap() async {
    var session = await _requireSession();
    session = await _resolveActiveChannel(session);
    final refreshed = await _refreshLeaseIfNeeded(session);
    final data = await _api.syncBootstrap(refreshed);
    await _store.applyBootstrap(
      data,
      cursorScope: refreshed.activeChannel.name,
    );
  }

  Future<void> syncNow() async {
    if (_running) {
      return;
    }

    _running = true;

    try {
      var session = await _requireSession();
      session = await _resolveActiveChannel(session);
      session = await _refreshLeaseIfNeeded(session);

      try {
        await _syncUsing(session);
      } on ApiException catch (error) {
        if (!_canFallbackFromLocal(error) ||
            session.connectionMode != ConnectionMode.automatic ||
            session.activeChannel != ConnectionChannel.local ||
            session.cloudBaseUrl == null ||
            session.cloudBaseUrl!.isEmpty) {
          rethrow;
        }

        final fallback = await _switchToCloud(session);
        await _syncUsing(fallback);
      }

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

  Future<void> _syncUsing(SessionCredentials session) async {
    if (session.activeChannel == ConnectionChannel.local) {
      await _api.heartbeat(session);
    }

    await _push(session);
    await _pull(session);
  }

  Future<SessionCredentials> _switchToCloud(SessionCredentials session) async {
    final resolver = _connectionResolver;
    if (resolver == null || session.cloudBaseUrl == null) {
      return session;
    }

    final target = await resolver.resolve(
      mode: ConnectionMode.cloud,
      localUrl: session.localBaseUrl,
      cloudUrl: session.cloudBaseUrl,
    );
    final switched = session.copyWith(
      baseUrl: target.baseUrl,
      activeChannel: ConnectionChannel.cloud,
    );
    await _credentials.saveActiveConnection(
      baseUrl: switched.baseUrl,
      activeChannel: switched.activeChannel,
    );
    await _store.setSystemState('active_connection', 'cloud');
    return switched;
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
    final cursorScope = session.activeChannel.name;
    var cursor = await _store.syncCursor(scope: cursorScope);

    for (var page = 0; page < 10; page++) {
      final response = await _api.pull(
        session,
        cursor: cursor,
        limit: 100,
      );
      await _store.applyPull(
        response,
        cursorScope: cursorScope,
      );

      cursor = (response['cursor'] as num?)?.toInt() ?? cursor;
      final hasMore = response['has_more'] == true;

      if (!hasMore) {
        return;
      }
    }
  }

  Future<SessionCredentials> _resolveActiveChannel(SessionCredentials session) async {
    if (session.connectionMode != ConnectionMode.automatic) return session;
    final resolver = _connectionResolver;
    if (resolver == null) return session;

    try {
      final target = await resolver.resolve(
        mode: ConnectionMode.automatic,
        localUrl: session.localBaseUrl,
        cloudUrl: session.cloudBaseUrl,
      );
      var selected = target;
      final expectedTenant = session.tenantId;

      if (expectedTenant != null && target.tenantId != expectedTenant) {
        if (target.channel == ConnectionChannel.local &&
            session.cloudBaseUrl != null &&
            session.cloudBaseUrl!.isNotEmpty) {
          final cloud = await resolver.resolve(
            mode: ConnectionMode.cloud,
            localUrl: session.localBaseUrl,
            cloudUrl: session.cloudBaseUrl,
          );

          if (cloud.tenantId != expectedTenant) {
            throw const ApiException(
              code: 'tenant_mismatch',
              message: 'The Restaurant cloud endpoint belongs to another tenant.',
            );
          }

          selected = cloud;
        } else {
          throw const ApiException(
            code: 'tenant_mismatch',
            message: 'The selected Restaurant endpoint belongs to another tenant.',
          );
        }
      }

      final switched = session.copyWith(
        baseUrl: selected.baseUrl,
        activeChannel: selected.channel,
      );
      await _credentials.saveActiveConnection(
        baseUrl: selected.baseUrl,
        activeChannel: selected.channel,
      );
      await _store.setSystemState('active_connection', selected.channel.name);
      return switched;
    } on ApiException {
      await _store.setSystemState('active_connection', 'offline');
      return session;
    }
  }

  Future<SessionCredentials> _refreshLeaseIfNeeded(
    SessionCredentials session,
  ) async {
    final verification = await _leaseVerifier.verify(
      signedLease: session.lease,
      publicKey: session.publicKey,
      expectedDeviceId: session.deviceId,
    );

    final expiresAt = verification.expiresAt;
    final needsRefresh = !verification.valid ||
        expiresAt == null ||
        expiresAt.difference(DateTime.now().toUtc()) < const Duration(hours: 12);

    if (!needsRefresh) {
      return session;
    }

    try {
      final refreshSession = session.activeChannel == ConnectionChannel.local &&
              session.cloudBaseUrl != null &&
              session.cloudBaseUrl!.isNotEmpty
          ? session.copyWith(
              baseUrl: session.cloudBaseUrl,
              activeChannel: ConnectionChannel.cloud,
            )
          : session;
      final response = await _api.refreshLease(refreshSession);
      final lease = Map<String, Object?>.from(
        response['lease']! as Map<Object?, Object?>,
      );
      final refreshedVerification = await _leaseVerifier.verify(
        signedLease: lease,
        publicKey: session.publicKey,
        expectedDeviceId: session.deviceId,
      );

      if (!refreshedVerification.valid) {
        throw const ApiException(
          code: 'invalid_offline_lease',
          message: 'The refreshed offline lease failed signature validation.',
        );
      }

      await _credentials.saveLease(lease);
      return session.copyWith(lease: lease);
    } on ApiException catch (error) {
      if (verification.valid && _isRetryable(error)) {
        return session;
      }

      rethrow;
    }
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

  bool _canFallbackFromLocal(ApiException error) {
    return _isRetryable(error) ||
        error.statusCode == 401 ||
        error.statusCode == 403 ||
        error.code == 'unauthenticated' ||
        error.code == 'forbidden';
  }

  bool _isRetryable(ApiException error) {
    return error.statusCode == null ||
        (error.statusCode != null && error.statusCode! >= 500);
  }
}
