import 'dart:io';

import 'package:businessos_restaurant_waiter/data/local/local_database.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:path/path.dart' as p;
import 'package:sqflite_common_ffi/sqflite_ffi.dart';

Map<String, Object?> _table(String status) => {
  'id': 'table-1',
  'code': 'T1',
  'name': 'Table 1',
  'capacity': 4,
  'status': status,
  'is_active': true,
  'area': {'id': 'area-1', 'name': 'Ground floor'},
  'branch': {'id': 'branch-1', 'name': 'Main'},
};

Map<String, Object?> _order(String id, String status) => {
  'id': 'server-$id',
  'client_order_id': id,
  'status': status,
  'branch_id': 'branch-1',
  'guest_count': 2,
  'total': '240.00',
  'subtotal': '240.00',
  'table': _table('occupied'),
  'items': <Object?>[],
};

Map<String, Object?> _bootstrap({
  required String tableStatus,
  required List<Map<String, Object?>> orders,
  required int cursor,
}) => {
  'cursor': cursor,
  'branches': <Object?>[],
  'menu': <Object?>[],
  'tables': [_table(tableStatus)],
  'orders': orders,
};

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  sqfliteFfiInit();
  databaseFactory = databaseFactoryFfi;

  test('fresh authoritative bootstrap retires stale remote order but retains history',
      () async {
    final dir = await Directory.systemTemp.createTemp('restaurant-reconcile-');
    try {
      final db = await LocalDatabase.open(
        databasePath: p.join(dir.path, 'restaurant.db'),
      );
      await db.applyBootstrap(
        _bootstrap(
          tableStatus: 'occupied',
          orders: [_order('old', 'submitted')],
          cursor: 1,
        ),
        cursorScope: 'local',
      );
      expect((await db.activeOrders()).length, 1);
      expect((await db.tables()).single['status'], 'occupied');

      await db.applyBootstrap(
        _bootstrap(tableStatus: 'available', orders: [], cursor: 2),
        cursorScope: 'local',
      );
      expect(await db.activeOrders(), isEmpty);
      expect((await db.order('old'))!['status'], 'archived_remote');
      expect((await db.tables()).single['status'], 'available');

      // If reassigned back to the waiter, the same historical local ID returns.
      await db.applyBootstrap(
        _bootstrap(
          tableStatus: 'occupied',
          orders: [_order('old', 'ready')],
          cursor: 3,
        ),
        cursorScope: 'local',
      );
      expect((await db.order('old'))!['status'], 'ready');
      expect((await db.activeOrders()).length, 1);
      await db.close();
    } finally {
      await dir.delete(recursive: true);
    }
  });

  test('server snapshots never discard locally queued unsent order work',
      () async {
    final dir = await Directory.systemTemp.createTemp('restaurant-pending-');
    try {
      final db = await LocalDatabase.open(
        databasePath: p.join(dir.path, 'restaurant.db'),
      );
      await db.createDraftOrder(
        clientOrderId: 'unsent',
        mutationId: 'open-unsent',
        tableId: 'table-1',
        branchId: 'branch-1',
        serviceType: 'dine_in',
        guestCount: 2,
      );
      await db.applyBootstrap(
        _bootstrap(tableStatus: 'available', orders: [], cursor: 1),
        cursorScope: 'local',
      );
      expect((await db.order('unsent'))!['status'], 'draft');
      expect((await db.tables()).single['status'], 'occupied');
      expect(await db.pendingCount(), 1);
      await db.close();
    } finally {
      await dir.delete(recursive: true);
    }
  });

  test('pending mutation protects synced server order absent from bootstrap',
      () async {
    final dir = await Directory.systemTemp.createTemp('restaurant-protect-');
    try {
      final db = await LocalDatabase.open(
        databasePath: p.join(dir.path, 'restaurant.db'),
      );
      await db.createDraftOrder(
        clientOrderId: 'pending-client',
        mutationId: 'mutation-client',
        tableId: 'table-1',
        branchId: 'branch-1',
        serviceType: 'dine_in',
        guestCount: 2,
      );
      await db.applyBootstrap(
        _bootstrap(
          tableStatus: 'occupied',
          orders: [_order('pending-client', 'submitted')],
          cursor: 1,
        ),
        cursorScope: 'local',
      );
      await db.applyBootstrap(
        _bootstrap(tableStatus: 'available', orders: [], cursor: 2),
        cursorScope: 'local',
      );
      expect((await db.order('pending-client'))!['status'], 'submitted');
      expect((await db.activeOrders()).length, 1);
      await db.close();
    } finally {
      await dir.delete(recursive: true);
    }
  });
}
