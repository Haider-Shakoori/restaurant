import 'package:businessos_restaurant_waiter/core/api/mobile_api_client.dart';
import 'package:businessos_restaurant_waiter/core/connection/connection_mode.dart';
import 'package:businessos_restaurant_waiter/core/connection/connection_resolver.dart';
import 'package:businessos_restaurant_waiter/core/models/session_credentials.dart';
import 'package:businessos_restaurant_waiter/core/security/offline_lease_verifier.dart';
import 'package:businessos_restaurant_waiter/core/security/secure_credential_store.dart';
import 'package:businessos_restaurant_waiter/data/local/outbox_mutation.dart';
import 'package:businessos_restaurant_waiter/sync/sync_engine.dart';
import 'package:businessos_restaurant_waiter/sync/sync_store.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('accepted mutations are applied and pull cursor resumes', () async {
    final store = _MemorySyncStore(
      pending: <OutboxMutation>[
        OutboxMutation(
          id: 1,
          mutationId: 'M-1',
          operation: 'order.open',
          payload: const <String, Object?>{
            'client_order_id': 'ORDER-1',
            'dining_table_id': 'TABLE-1',
          },
          occurredAt: DateTime.utc(2026, 9, 28),
          attempts: 0,
        ),
      ],
    );
    final api = _FakeApi(
      pushResponse: const <String, Object?>{
        'results': <Object?>[
          <String, Object?>{
            'mutation_id': 'M-1',
            'status': 'accepted',
            'entity_type': 'order',
            'entity_id': 'SERVER-ORDER-1',
            'data': <String, Object?>{},
          },
        ],
      },
      pullResponses: <Map<String, Object?>>[
        const <String, Object?>{
          'cursor': 12,
          'has_more': true,
          'changes': <Object?>[],
        },
        const <String, Object?>{
          'cursor': 15,
          'has_more': false,
          'changes': <Object?>[],
        },
      ],
    );

    final engine = SyncEngine(
      api: api,
      store: store,
      credentials: _MemoryCredentials(_session()),
      leaseVerifier: const _AlwaysValidLease(),
    );

    await engine.syncNow();

    expect(store.accepted, <String>['M-1']);
    expect(store.cursors['cloud'], 15);
    expect(api.pullCursors, <int>[0, 12]);
    expect(store.states['last_sync_error'], '');
  });

  test('conflict is persisted without retrying the mutation', () async {
    final mutation = OutboxMutation(
      id: 1,
      mutationId: 'M-CONFLICT',
      operation: 'order.open',
      payload: const <String, Object?>{
        'client_order_id': 'ORDER-2',
        'dining_table_id': 'TABLE-1',
      },
      occurredAt: DateTime.utc(2026, 9, 28),
      attempts: 0,
    );
    final store = _MemorySyncStore(pending: <OutboxMutation>[mutation]);
    final api = _FakeApi(
      pushResponse: const <String, Object?>{
        'results': <Object?>[
          <String, Object?>{
            'mutation_id': 'M-CONFLICT',
            'status': 'conflict',
            'code': 'table_busy',
            'message': 'Table is occupied.',
          },
        ],
      },
      pullResponses: <Map<String, Object?>>[
        const <String, Object?>{
          'cursor': 0,
          'has_more': false,
          'changes': <Object?>[],
        },
      ],
    );

    final engine = SyncEngine(
      api: api,
      store: store,
      credentials: _MemoryCredentials(_session()),
      leaseVerifier: const _AlwaysValidLease(),
    );

    await engine.syncNow();

    expect(store.conflicts, <String>['M-CONFLICT']);
    expect(store.retries, isEmpty);
  });

  test('valid lease keeps LAN sync working when refresh cloud is offline', () async {
    final store = _MemorySyncStore(pending: <OutboxMutation>[]);
    final api = _FakeApi(
      refreshError: const ApiException(
        code: 'offline',
        message: 'Cloud unavailable.',
      ),
      pullResponses: <Map<String, Object?>>[
        const <String, Object?>{
          'cursor': 1,
          'has_more': false,
          'changes': <Object?>[],
        },
      ],
    );

    final engine = SyncEngine(
      api: api,
      store: store,
      credentials: _MemoryCredentials(_session()),
      leaseVerifier: const _ExpiringButValidLease(),
    );

    await engine.syncNow();

    expect(store.cursors['cloud'], 1);
    expect(store.states['last_sync_error'], '');
  });


  test('local channel sends terminal heartbeat before sync', () async {
    final store = _MemorySyncStore(pending: <OutboxMutation>[]);
    final api = _FakeApi(
      pullResponses: <Map<String, Object?>>[
        const <String, Object?>{
          'cursor': 0,
          'has_more': false,
          'changes': <Object?>[],
        },
      ],
    );

    final engine = SyncEngine(
      api: api,
      store: store,
      credentials: _MemoryCredentials(_session(
        activeChannel: ConnectionChannel.local,
        baseUrl: 'http://192.168.1.20:8787',
      )),
      leaseVerifier: const _AlwaysValidLease(),
    );

    await engine.syncNow();

    expect(api.heartbeatCount, 1);
  });

  test('automatic mode prefers LAN and immediately falls back to cloud', () async {
    final store = _MemorySyncStore(pending: <OutboxMutation>[]);
    final api = _FakeApi(
      heartbeatError: const ApiException(
        code: 'offline',
        message: 'LAN disappeared.',
      ),
      pullResponses: <Map<String, Object?>>[
        const <String, Object?>{
          'cursor': 1,
          'has_more': false,
          'changes': <Object?>[],
        },
      ],
    );
    final credentials = _MemoryCredentials(
      _session(
        connectionMode: ConnectionMode.automatic,
        activeChannel: ConnectionChannel.cloud,
        baseUrl: 'https://restaurant.test',
        localBaseUrl: 'http://192.168.1.20:8787',
        cloudBaseUrl: 'https://restaurant.test',
        tenantId: 'tenant-1',
      ),
    );
    final resolver = ConnectionResolver(
      probe: _FakeProbe(<String, ServerHealth>{
        'http://192.168.1.20:8787': const ServerHealth(
          baseUrl: 'http://192.168.1.20:8787',
          tenantId: 'tenant-1',
          service: 'BusinessOS Restaurant Desktop',
        ),
        'https://restaurant.test': const ServerHealth(
          baseUrl: 'https://restaurant.test',
          tenantId: 'tenant-1',
          service: 'BusinessOS Restaurant Cloud',
        ),
      }),
    );

    final engine = SyncEngine(
      api: api,
      store: store,
      credentials: credentials,
      leaseVerifier: const _AlwaysValidLease(),
      connectionResolver: resolver,
    );

    await engine.syncNow();

    expect(api.heartbeatCount, 1);
    expect(api.pullBaseUrls, <String>['https://restaurant.test']);
    expect(store.states['active_connection'], 'cloud');
    expect(credentials.session?.activeChannel, ConnectionChannel.cloud);
  });

  test('automatic mode falls back to cloud when LAN terminal is not paired', () async {
    final store = _MemorySyncStore(pending: <OutboxMutation>[]);
    final api = _FakeApi(
      heartbeatError: const ApiException(
        code: 'unauthenticated',
        message: 'Terminal is not paired.',
        statusCode: 401,
      ),
      pullResponses: <Map<String, Object?>>[
        const <String, Object?>{
          'cursor': 2,
          'has_more': false,
          'changes': <Object?>[],
        },
      ],
    );
    final credentials = _MemoryCredentials(
      _session(
        connectionMode: ConnectionMode.automatic,
        activeChannel: ConnectionChannel.cloud,
        baseUrl: 'https://restaurant.test',
        localBaseUrl: 'http://192.168.1.20:8787',
        cloudBaseUrl: 'https://restaurant.test',
        tenantId: 'tenant-1',
      ),
    );
    final resolver = ConnectionResolver(
      probe: _FakeProbe(<String, ServerHealth>{
        'http://192.168.1.20:8787': const ServerHealth(
          baseUrl: 'http://192.168.1.20:8787',
          tenantId: 'tenant-1',
          service: 'BusinessOS Restaurant Desktop',
        ),
        'https://restaurant.test': const ServerHealth(
          baseUrl: 'https://restaurant.test',
          tenantId: 'tenant-1',
          service: 'BusinessOS Restaurant Cloud',
        ),
      }),
    );

    final engine = SyncEngine(
      api: api,
      store: store,
      credentials: credentials,
      leaseVerifier: const _AlwaysValidLease(),
      connectionResolver: resolver,
    );

    await engine.syncNow();

    expect(api.heartbeatCount, 1);
    expect(api.pullBaseUrls, <String>['https://restaurant.test']);
    expect(store.cursors['cloud'], 2);
    expect(store.states['active_connection'], 'cloud');
  });

  test('automatic mode returns to LAN after cloud fallback', () async {
    final store = _MemorySyncStore(pending: <OutboxMutation>[]);
    final credentials = _MemoryCredentials(
      _session(
        connectionMode: ConnectionMode.automatic,
        activeChannel: ConnectionChannel.cloud,
        baseUrl: 'https://restaurant.test',
        localBaseUrl: 'http://192.168.1.20:8787',
        cloudBaseUrl: 'https://restaurant.test',
        tenantId: 'tenant-1',
      ),
    );
    final api = _FakeApi(
      pullResponses: <Map<String, Object?>>[
        const <String, Object?>{
          'cursor': 1,
          'has_more': false,
          'changes': <Object?>[],
        },
      ],
    );
    final resolver = ConnectionResolver(
      probe: _FakeProbe(<String, ServerHealth>{
        'http://192.168.1.20:8787': const ServerHealth(
          baseUrl: 'http://192.168.1.20:8787',
          tenantId: 'tenant-1',
          service: 'BusinessOS Restaurant Desktop',
        ),
        'https://restaurant.test': const ServerHealth(
          baseUrl: 'https://restaurant.test',
          tenantId: 'tenant-1',
          service: 'BusinessOS Restaurant Cloud',
        ),
      }),
    );

    final engine = SyncEngine(
      api: api,
      store: store,
      credentials: credentials,
      leaseVerifier: const _AlwaysValidLease(),
      connectionResolver: resolver,
    );

    await engine.syncNow();

    expect(api.heartbeatCount, 1);
    expect(api.pullBaseUrls, <String>['http://192.168.1.20:8787']);
    expect(store.states['active_connection'], 'local');
    expect(credentials.session?.activeChannel, ConnectionChannel.local);
    expect(credentials.session?.baseUrl, 'http://192.168.1.20:8787');
  });

  test('cloud cursor never suppresses LAN changes', () async {
    final store = _MemorySyncStore(
      pending: <OutboxMutation>[],
      cursors: <String, int>{'cloud': 57, 'local': 3},
    );
    final credentials = _MemoryCredentials(
      _session(
        connectionMode: ConnectionMode.automatic,
        activeChannel: ConnectionChannel.cloud,
        baseUrl: 'https://restaurant.test',
        localBaseUrl: 'http://192.168.1.20:8787',
        cloudBaseUrl: 'https://restaurant.test',
        tenantId: 'tenant-1',
      ),
    );
    final api = _FakeApi(
      pullResponses: <Map<String, Object?>>[
        const <String, Object?>{
          'cursor': 4,
          'has_more': false,
          'changes': <Object?>[],
        },
      ],
    );
    final resolver = ConnectionResolver(
      probe: _FakeProbe(<String, ServerHealth>{
        'http://192.168.1.20:8787': const ServerHealth(
          baseUrl: 'http://192.168.1.20:8787',
          tenantId: 'tenant-1',
          service: 'BusinessOS Restaurant Desktop',
        ),
        'https://restaurant.test': const ServerHealth(
          baseUrl: 'https://restaurant.test',
          tenantId: 'tenant-1',
          service: 'BusinessOS Restaurant Cloud',
        ),
      }),
    );

    final engine = SyncEngine(
      api: api,
      store: store,
      credentials: credentials,
      leaseVerifier: const _AlwaysValidLease(),
      connectionResolver: resolver,
    );

    await engine.syncNow();

    expect(api.pullCursors, <int>[3]);
    expect(store.cursors['local'], 4);
    expect(store.cursors['cloud'], 57);
  });

  test('automatic mode rejects wrong-tenant LAN and uses tenant cloud', () async {
    final store = _MemorySyncStore(pending: <OutboxMutation>[]);
    final credentials = _MemoryCredentials(
      _session(
        connectionMode: ConnectionMode.automatic,
        activeChannel: ConnectionChannel.cloud,
        baseUrl: 'https://restaurant.test',
        localBaseUrl: 'http://192.168.1.20:8787',
        cloudBaseUrl: 'https://restaurant.test',
        tenantId: 'tenant-1',
      ),
    );
    final api = _FakeApi(
      pullResponses: <Map<String, Object?>>[
        const <String, Object?>{
          'cursor': 1,
          'has_more': false,
          'changes': <Object?>[],
        },
      ],
    );
    final resolver = ConnectionResolver(
      probe: _FakeProbe(<String, ServerHealth>{
        'http://192.168.1.20:8787': const ServerHealth(
          baseUrl: 'http://192.168.1.20:8787',
          tenantId: 'another-restaurant',
          service: 'BusinessOS Restaurant Desktop',
        ),
        'https://restaurant.test': const ServerHealth(
          baseUrl: 'https://restaurant.test',
          tenantId: 'tenant-1',
          service: 'BusinessOS Restaurant Cloud',
        ),
      }),
    );

    final engine = SyncEngine(
      api: api,
      store: store,
      credentials: credentials,
      leaseVerifier: const _AlwaysValidLease(),
      connectionResolver: resolver,
    );

    await engine.syncNow();

    expect(api.heartbeatCount, 0);
    expect(api.pullBaseUrls, <String>['https://restaurant.test']);
    expect(store.states['active_connection'], 'cloud');
    expect(credentials.session?.activeChannel, ConnectionChannel.cloud);
  });

  test('network failure schedules exponential retry', () async {
    final mutation = OutboxMutation(
      id: 1,
      mutationId: 'M-RETRY',
      operation: 'order.open',
      payload: const <String, Object?>{
        'client_order_id': 'ORDER-3',
        'dining_table_id': 'TABLE-2',
      },
      occurredAt: DateTime.utc(2026, 9, 28),
      attempts: 2,
    );
    final store = _MemorySyncStore(pending: <OutboxMutation>[mutation]);
    final api = _FakeApi(
      pushError: const ApiException(
        code: 'offline',
        message: 'No connection.',
      ),
      pullResponses: const <Map<String, Object?>>[],
    );

    final engine = SyncEngine(
      api: api,
      store: store,
      credentials: _MemoryCredentials(_session()),
      leaseVerifier: const _AlwaysValidLease(),
    );

    await expectLater(engine.syncNow(), throwsA(isA<ApiException>()));

    expect(store.retries, <String>['M-RETRY']);
    expect(store.states['last_sync_error'], 'offline');
  });
}

SessionCredentials _session({
  ConnectionMode connectionMode = ConnectionMode.cloud,
  ConnectionChannel activeChannel = ConnectionChannel.cloud,
  String baseUrl = 'https://restaurant.test',
  String? localBaseUrl,
  String? cloudBaseUrl,
  String? tenantId,
}) {
  return SessionCredentials(
    baseUrl: baseUrl,
    accessToken: 'token',
    deviceId: 'device-1',
    deviceSecret: 'secret',
    deviceUid: 'uid-1',
    publicKey: 'public-key',
    connectionMode: connectionMode,
    activeChannel: activeChannel,
    localBaseUrl: localBaseUrl,
    cloudBaseUrl: cloudBaseUrl,
    tenantId: tenantId,
    lease: const <String, Object?>{
      'payload': <String, Object?>{},
      'signature': 'signature',
      'algorithm': 'Ed25519',
    },
  );
}

class _ExpiringButValidLease implements LeaseValidator {
  const _ExpiringButValidLease();

  @override
  Future<LeaseVerificationResult> verify({
    required Map<String, Object?> signedLease,
    required String publicKey,
    String? expectedDeviceId,
    String? expectedTenantId,
    DateTime? now,
  }) async {
    return LeaseVerificationResult(
      valid: true,
      reason: 'valid',
      expiresAt: DateTime.now().toUtc().add(const Duration(hours: 1)),
    );
  }
}

class _AlwaysValidLease implements LeaseValidator {
  const _AlwaysValidLease();

  @override
  Future<LeaseVerificationResult> verify({
    required Map<String, Object?> signedLease,
    required String publicKey,
    String? expectedDeviceId,
    String? expectedTenantId,
    DateTime? now,
  }) async {
    return LeaseVerificationResult(
      valid: true,
      reason: 'valid',
      expiresAt: DateTime.now().toUtc().add(const Duration(days: 2)),
    );
  }
}

class _MemoryCredentials implements CredentialStore {
  _MemoryCredentials(this.session);

  SessionCredentials? session;

  @override
  Future<void> clearSession() async {
    session = null;
  }

  @override
  Future<String> deviceUid() async => session?.deviceUid ?? 'uid';

  @override
  Future<SessionCredentials?> readSession() async => session;

  @override
  Future<void> saveAccessToken(String token) async {
    final current = session;
    if (current != null) {
      session = current.copyWith(accessToken: token);
    }
  }

  @override
  Future<void> saveActivation({
    required String baseUrl,
    required String deviceId,
    required String deviceSecret,
    required String deviceUid,
    required String publicKey,
    required Map<String, Object?> lease,
    ConnectionMode connectionMode = ConnectionMode.cloud,
    ConnectionChannel activeChannel = ConnectionChannel.cloud,
    String? localBaseUrl,
    String? cloudBaseUrl,
    String? tenantId,
  }) async {}

  @override
  Future<void> saveLease(Map<String, Object?> lease) async {
    final current = session;
    if (current != null) {
      session = current.copyWith(lease: lease);
    }
  }

  @override
  Future<void> saveActiveConnection({
    required String baseUrl,
    required ConnectionChannel activeChannel,
  }) async {
    final current = session;
    if (current != null) {
      session = current.copyWith(
        baseUrl: baseUrl,
        activeChannel: activeChannel,
      );
    }
  }
}

class _MemorySyncStore implements SyncStore {
  _MemorySyncStore({
    required List<OutboxMutation> pending,
    Map<String, int>? cursors,
  }) : _pending = pending,
       cursors = <String, int>{...?cursors};

  final List<OutboxMutation> _pending;
  final List<String> accepted = <String>[];
  final List<String> conflicts = <String>[];
  final List<String> retries = <String>[];
  final Map<String, String> states = <String, String>{};
  final Map<String, int> cursors;

  @override
  Future<void> applyAcceptedResult(Map<String, Object?> result) async {
    accepted.add(result['mutation_id']!.toString());
    _pending.removeWhere(
      (mutation) => mutation.mutationId == result['mutation_id'],
    );
  }

  @override
  Future<void> applyBootstrap(
    Map<String, Object?> data, {
    required String cursorScope,
  }) async {
    cursors[cursorScope] =
        (data['cursor'] as num?)?.toInt() ?? (cursors[cursorScope] ?? 0);
  }

  @override
  Future<void> applyPull(
    Map<String, Object?> data, {
    required String cursorScope,
  }) async {
    cursors[cursorScope] =
        (data['cursor'] as num?)?.toInt() ?? (cursors[cursorScope] ?? 0);
  }

  @override
  Future<void> markConflict(
    OutboxMutation mutation,
    Map<String, Object?> result,
  ) async {
    conflicts.add(mutation.mutationId);
    _pending.removeWhere(
      (value) => value.mutationId == mutation.mutationId,
    );
  }

  @override
  Future<void> markRetry(
    OutboxMutation mutation, {
    required String message,
    required DateTime retryAt,
  }) async {
    retries.add(mutation.mutationId);
  }

  @override
  Future<List<OutboxMutation>> pendingMutations({int limit = 50}) async {
    return _pending.take(limit).toList(growable: false);
  }

  @override
  Future<void> setSystemState(String key, String value) async {
    states[key] = value;
  }

  @override
  Future<int> syncCursor({required String scope}) async =>
      cursors[scope] ?? 0;
}

class _FakeApi implements SyncApi {
  _FakeApi({
    this.pushResponse,
    this.pushError,
    this.refreshError,
    this.heartbeatError,
    required List<Map<String, Object?>> pullResponses,
  }) : _pullResponses = pullResponses;

  final Map<String, Object?>? pushResponse;
  final ApiException? pushError;
  final ApiException? refreshError;
  final ApiException? heartbeatError;
  final List<Map<String, Object?>> _pullResponses;
  final List<int> pullCursors = <int>[];
  final List<String> pullBaseUrls = <String>[];
  int _pullIndex = 0;
  int heartbeatCount = 0;

  @override
  Future<Map<String, Object?>> heartbeat(
    SessionCredentials credentials,
  ) async {
    heartbeatCount += 1;
    final error = heartbeatError;
    if (error != null) {
      throw error;
    }
    return const <String, Object?>{
      'network_mode': 'healthy',
      'local_operations_allowed': true,
    };
  }

  @override
  Future<Map<String, Object?>> pull(
    SessionCredentials credentials, {
    required int cursor,
    int limit = 100,
  }) async {
    pullCursors.add(cursor);
    pullBaseUrls.add(credentials.baseUrl);
    return _pullResponses[_pullIndex++];
  }

  @override
  Future<Map<String, Object?>> push(
    SessionCredentials credentials,
    List<Map<String, Object?>> mutations,
  ) async {
    final error = pushError;
    if (error != null) {
      throw error;
    }
    return pushResponse!;
  }

  @override
  Future<Map<String, Object?>> refreshLease(
    SessionCredentials credentials,
  ) async {
    final error = refreshError;
    if (error != null) {
      throw error;
    }

    return <String, Object?>{'lease': credentials.lease};
  }

  @override
  Future<Map<String, Object?>> syncBootstrap(
    SessionCredentials credentials,
  ) async {
    return const <String, Object?>{'cursor': 0};
  }
}


class _FakeProbe implements ServerProbe {
  _FakeProbe(this.healthByUrl);

  final Map<String, ServerHealth> healthByUrl;

  @override
  Future<ServerHealth> probe(String baseUrl) async {
    final health = healthByUrl[baseUrl];
    if (health == null) {
      throw const ApiException(code: 'offline', message: 'Unreachable.');
    }
    return health;
  }
}
