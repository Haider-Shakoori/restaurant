import 'dart:io';

import 'package:businessos_restaurant_waiter/data/local/local_database.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:path/path.dart' as p;
import 'package:sqflite_common_ffi/sqflite_ffi.dart';

Map<String, Object?> snapshot({required String status}) => <String, Object?>{
  'cursor': 10,
  'branches': <Object?>[],
  'menu': <Object?>[],
  'tables': <Object?>[
    <String, Object?>{
      'id': 'T-01',
      'code': 'T01',
      'name': 'Table 01',
      'capacity': 4,
      'status': status,
      'is_active': true,
    },
  ],
  'orders': <Object?>[],
};

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  sqfliteFfiInit();
  databaseFactory = databaseFactoryFfi;

  test('LAN bootstrap hides historical server-backed draft and frees table',
      () async {
    final dir = await Directory.systemTemp.createTemp('restaurant-lan-floor-');
    final db = await LocalDatabase.open(
      databasePath: p.join(dir.path, 'local.db'),
    );
    try {
      await db.applyBootstrap(
        snapshot(status: 'occupied'),
        cursorScope: 'cloud',
      );
      await db.createDraftOrder(
        clientOrderId: 'OLD-DRAFT',
        mutationId: 'OPEN-OLD',
        branchId: 'branch-1',
        tableId: 'T-01',
        serviceType: 'dine_in',
        guestCount: 1,
      );
      await db.applyAcceptedResult(<String, Object?>{
        'mutation_id': 'OPEN-OLD',
        'status': 'accepted',
        'entity_type': 'order',
        'data': <String, Object?>{
          'id': 'SERVER-OLD',
          'client_order_id': 'OLD-DRAFT',
          'branch_id': 'branch-1',
          'status': 'draft',
          'guest_count': 1,
          'table': <String, Object?>{'id': 'T-01'},
          'items': <Object?>[],
        },
      });
      expect(await db.pendingCount(), 0);

      await db.applyBootstrap(
        snapshot(status: 'available'),
        cursorScope: 'local',
      );
      expect((await db.order('OLD-DRAFT'))!['status'], 'inactive_remote');
      expect(await db.activeOrders(), isEmpty);
      expect((await db.tables()).single['status'], 'available');
    } finally {
      await db.close();
      await dir.delete(recursive: true);
    }
  });

  test('offline pending order still occupies the table after LAN refresh',
      () async {
    final dir = await Directory.systemTemp.createTemp('restaurant-lan-outbox-');
    final db = await LocalDatabase.open(
      databasePath: p.join(dir.path, 'local.db'),
    );
    try {
      await db.applyBootstrap(
        snapshot(status: 'available'),
        cursorScope: 'local',
      );
      await db.createDraftOrder(
        clientOrderId: 'OFFLINE-ORDER',
        mutationId: 'OPEN-OFFLINE',
        branchId: 'branch-1',
        tableId: 'T-01',
        serviceType: 'dine_in',
        guestCount: 1,
      );
      await db.applyBootstrap(
        snapshot(status: 'available'),
        cursorScope: 'local',
      );
      expect((await db.tables()).single['status'], 'occupied');
      expect((await db.order('OFFLINE-ORDER'))!['status'], 'draft');
      expect(await db.pendingCount(), 1);
    } finally {
      await db.close();
      await dir.delete(recursive: true);
    }
  });
}
