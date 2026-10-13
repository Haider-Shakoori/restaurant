import 'dart:io';
import 'package:businessos_restaurant_waiter/data/local/local_database.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:path/path.dart' as p;
import 'package:sqflite_common_ffi/sqflite_ffi.dart';

Map<String, Object?> table(String id, String status) => {
  'id': id, 'code': id, 'name': 'Table $id', 'capacity': 4,
  'status': status, 'is_active': true,
  'branch': <String, Object?>{'id': 'branch', 'name': 'Main'},
};

Map<String, Object?> snapshot(List<Map<String, Object?>> tables,
    {List<Map<String, Object?>> orders = const []}) => {
  'cursor': 8,
  'branches': <Object?>[
    <String, Object?>{
      'id': 'branch', 'code': 'B', 'name': 'Main', 'is_active': true,
    },
  ],
  'tables': tables,
  'orders': orders,
  'menu': <Object?>[],
};

Map<String, Object?> serverOrder(String id, String tableId) => {
  'id': id, 'client_order_id': id, 'branch_id': 'branch',
  'service_type': 'dine_in', 'status': 'draft', 'guest_count': 1,
  'table': <String, Object?>{'id': tableId}, 'items': <Object?>[],
};

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  sqfliteFfiInit();
  databaseFactory = databaseFactoryFfi;

  Future<void> withDatabase(
      Future<void> Function(LocalDatabase, String) action) async {
    final dir = await Directory.systemTemp.createTemp('restaurant-reconcile-');
    final path = p.join(dir.path, 'test.db');
    final db = await LocalDatabase.open(databasePath: path);
    try {
      await action(db, path);
    } finally {
      await db.close();
      await dir.delete(recursive: true);
    }
  }

  test('one queued order never keeps unrelated server drafts or tables occupied',
      () async {
    await withDatabase((db, path) async {
      await db.applyBootstrap(snapshot([
        table('t1', 'occupied'),
        table('t2', 'available'),
        table('t3', 'available'),
      ], orders: [serverOrder('server-old', 't1')]), cursorScope: 'local');
      expect((await db.activeOrders()).length, 1);

      await db.createDraftOrder(
        clientOrderId: 'offline-keep', mutationId: 'pending-open',
        tableId: 't2', branchId: 'branch',
        serviceType: 'dine_in', guestCount: 1,
      );
      expect((await db.tables()).singleWhere((t) => t['id'] == 't2')['status'],
          'occupied');

      // The desktop has since closed its prior order; only the unrelated
      // phone draft must remain visible and reserve t2.
      await db.applyBootstrap(snapshot([
        table('t1', 'available'),
        table('t2', 'available'),
        table('t3', 'available'),
      ]), cursorScope: 'local');
      final orders = await db.activeOrders();
      expect(orders.length, 1);
      expect(orders.single['local_order_id'], 'offline-keep');
      final result = {for (final t in await db.tables()) t['id']: t['status']};
      expect(result, {'t1': 'available', 't2': 'occupied', 't3': 'available'});
    });
  });

  test('discarding an abandoned draft deletes outbox and releases local table',
      () async {
    await withDatabase((db, path) async {
      await db.applyBootstrap(snapshot([table('t1', 'available')]),
          cursorScope: 'local');
      await db.createDraftOrder(
        clientOrderId: 'abandoned', mutationId: 'open-1',
        tableId: 't1', branchId: 'branch',
        serviceType: 'dine_in', guestCount: 1,
      );
      expect((await db.tables()).single['status'], 'occupied');
      expect(await db.pendingCount(), 1);
      await db.discardLocalOrder('abandoned');
      expect(await db.pendingCount(), 0);
      expect(await db.activeOrders(), isEmpty);
      expect((await db.tables()).single['status'], 'available');
      await db.applyPull({
        'cursor': 9,
        'changes': <Object?>[],
      }, cursorScope: 'local');
      expect((await db.tables()).single['status'], 'available');
    });
  });

  test('never discards desktop-confirmed orders or half-deletes a mixed batch',
      () async {
    await withDatabase((db, path) async {
      await db.applyBootstrap(snapshot([
        table('t1', 'occupied'), table('t2', 'available'),
      ], orders: [serverOrder('on-desktop', 't1')]), cursorScope: 'local');
      await db.createDraftOrder(
        clientOrderId: 'local', mutationId: 'open-local',
        tableId: 't2', branchId: 'branch',
        serviceType: 'dine_in', guestCount: 1,
      );
      await expectLater(
        db.discardLocalOrders(['local', 'on-desktop']),
        throwsStateError,
      );
      expect(await db.pendingCount(), 1);
      expect((await db.activeOrders()).length, 2);
      expect((await db.tables()).singleWhere((t) => t['id'] == 't1')['status'],
          'occupied');
    });
  });

  test('server pull overrides optimistic occupied after it was abandoned',
      () async {
    await withDatabase((db, path) async {
      await db.applyBootstrap(snapshot([table('t1', 'available')]),
          cursorScope: 'local');
      await db.createDraftOrder(
        clientOrderId: 'old', mutationId: 'draft-mutation',
        tableId: 't1', branchId: 'branch',
        serviceType: 'dine_in', guestCount: 1,
      );
      await db.discardLocalOrder('old');
      await db.applyPull({
        'cursor': 11,
        'changes': <Object?>[
          {
            'entity_type': 'dining_table', 'entity_id': 't1',
            'operation': 'update',
            'data': table('t1', 'available'),
          },
        ],
      }, cursorScope: 'local');
      expect((await db.tables()).single['status'], 'available');
    });
  });

  test('new KOT round action is absent from waiter UI', () async {
    final source = File('lib/features/orders/order_screen.dart').readAsStringSync();
    expect(source.contains('Send New KOT Round'), isFalse);
    expect(source.contains("label: Text('KOT ("), isTrue);
  });
}
