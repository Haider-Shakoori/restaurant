import 'dart:io';

import 'package:businessos_restaurant_waiter/data/local/local_database.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:path/path.dart' as p;
import 'package:sqflite_common_ffi/sqflite_ffi.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  sqfliteFfiInit();
  databaseFactory = databaseFactoryFfi;

  late Directory temporary;
  late LocalDatabase database;

  setUp(() async {
    temporary = await Directory.systemTemp.createTemp('restaurant-table-sync-');
    database = await LocalDatabase.open(
      databasePath: p.join(temporary.path, 'restaurant.db'),
    );
  });

  tearDown(() async {
    await database.close();
    await temporary.delete(recursive: true);
  });

  test('cashier clearing a server-backed draft makes the table available',
      () async {
    await database.applyBootstrap(
      _snapshot(
        cursor: 10,
        tables: <Map<String, Object?>>[_table('occupied')],
        orders: <Map<String, Object?>>[_serverOrder()],
      ),
      cursorScope: 'local',
    );

    expect((await database.tables()).single['status'], 'occupied');
    expect((await database.activeOrders()).single['server_id'], 'server-order');

    await database.applyPull(
      <String, Object?>{
        'cursor': 11,
        'changes': <Object?>[
          <String, Object?>{
            'entity_type': 'dining_table',
            'entity_id': 'table-1',
            'operation': 'upsert',
            'data': _table('available'),
          },
        ],
      },
      cursorScope: 'local',
    );

    expect((await database.tables()).single['status'], 'available');
    // Preserve the cached order for audit/offline access, but never show a
    // server-confirmed stale draft as an active order on a free table.
    expect(await database.activeOrders(), isEmpty);
    expect(await database.order('client-order'), isNotNull);

    // A later full resync must not restore the old cached draft occupancy.
    await database.applyBootstrap(
      _snapshot(
        cursor: 11,
        tables: <Map<String, Object?>>[_table('available')],
      ),
      cursorScope: 'local',
    );
    expect((await database.tables()).single['status'], 'available');
  });

  test('a genuinely unsynced offline draft continues to reserve its table',
      () async {
    await database.applyBootstrap(
      _snapshot(tables: <Map<String, Object?>>[_table('available')]),
      cursorScope: 'local',
    );
    await database.createDraftOrder(
      clientOrderId: 'offline-order',
      mutationId: 'offline-open',
      tableId: 'table-1',
      branchId: 'branch-1',
      serviceType: 'dine_in',
      guestCount: 2,
    );

    expect((await database.tables()).single['status'], 'occupied');

    await database.applyPull(
      <String, Object?>{
        'cursor': 1,
        'changes': <Object?>[
          <String, Object?>{
            'entity_type': 'dining_table',
            'entity_id': 'table-1',
            'operation': 'upsert',
            'data': _table('available'),
          },
        ],
      },
      cursorScope: 'local',
    );
    expect((await database.tables()).single['status'], 'occupied');
    expect(await database.pendingCount(), 1);
    expect((await database.activeOrders()).single['local_order_id'], 'offline-order');

    await database.applyBootstrap(
      _snapshot(
        cursor: 2,
        tables: <Map<String, Object?>>[_table('available')],
      ),
      cursorScope: 'local',
    );
    expect((await database.tables()).single['status'], 'occupied');
    expect(await database.pendingCount(), 1);
    expect(await database.order('offline-order'), isNotNull);
  });

  test('authoritative bootstrap hides obsolete tables without deleting drafts',
      () async {
    await database.applyBootstrap(
      _snapshot(
        tables: <Map<String, Object?>>[
          _table('available'),
          _table('available', id: 'old-table', code: 'OLD'),
        ],
      ),
      cursorScope: 'local',
    );
    expect(await database.tables(), hasLength(2));

    await database.applyBootstrap(
      _snapshot(tables: <Map<String, Object?>>[_table('available')]),
      cursorScope: 'local',
    );

    final visible = await database.tables();
    expect(visible, hasLength(1));
    expect(visible.single['id'], 'table-1');
  });

  test('removed table with an unsent offline order remains recoverable',
      () async {
    await database.applyBootstrap(
      _snapshot(tables: <Map<String, Object?>>[_table('available')]),
      cursorScope: 'local',
    );

    await database.createDraftOrder(
      clientOrderId: 'offline-order',
      mutationId: 'offline-open',
      tableId: 'table-1',
      branchId: 'branch-1',
      serviceType: 'dine_in',
      guestCount: 2,
    );
    await database.applyBootstrap(
      _snapshot(tables: <Map<String, Object?>>[]),
      cursorScope: 'local',
    );

    expect(await database.tables(), hasLength(1));
    expect((await database.tables()).single['status'], 'occupied');
    expect(await database.pendingCount(), 1);
    expect(await database.order('offline-order'), isNotNull);
  });
}

Map<String, Object?> _snapshot({
  int cursor = 0,
  List<Map<String, Object?>> tables = const [],
  List<Map<String, Object?>> orders = const [],
}) => <String, Object?>{
  'cursor': cursor,
  'branches': <Object?>[],
  'menu': <Object?>[],
  'tables': tables,
  'orders': orders,
};

Map<String, Object?> _table(
  String status, {
  String id = 'table-1',
  String code = 'T1',
}) => <String, Object?>{
  'id': id,
  'code': code,
  'name': code,
  'capacity': 4,
  'status': status,
  'is_active': true,
  'area': <String, Object?>{'id': 'area-1', 'name': 'Main hall'},
  'branch': <String, Object?>{'id': 'branch-1', 'name': 'Main'},
};

Map<String, Object?> _serverOrder() => <String, Object?>{
  'id': 'server-order',
  'client_order_id': 'client-order',
  'branch_id': 'branch-1',
  'status': 'draft',
  'guest_count': 2,
  'table': <String, Object?>{'id': 'table-1'},
  'items': <Object?>[],
};
