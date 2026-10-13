import 'dart:io';

import 'package:businessos_restaurant_waiter/data/local/local_database.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:path/path.dart' as p;
import 'package:sqflite_common_ffi/sqflite_ffi.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  sqfliteFfiInit();
  databaseFactory = databaseFactoryFfi;

  test('desktop available status wins over a stale server-backed draft',
      () async {
    final dir = await Directory.systemTemp.createTemp('table-state-sync-');
    final path = p.join(dir.path, 'waiter.db');
    final db = await LocalDatabase.open(databasePath: path);
    try {
      final sqlite = await openDatabase(path, singleInstance: false);
      try {
        await sqlite.insert('orders', <String, Object?>{
          'local_order_id': 'old-synced',
          'client_order_id': 'old-synced',
          'server_id': 'server-1',
          'table_id': 't1',
          'status': 'draft',
          'guest_count': 1,
          'updated_at': '2026-10-01T00:00:00Z',
        });
      } finally {
        await sqlite.close();
      }

      await db.applyBootstrap(<String, Object?>{
        'cursor': 8,
        'branches': <Object?>[
          <String, Object?>{
            'id': 'b1', 'code': 'B1', 'name': 'Main',
            'is_active': true,
          },
        ],
        'tables': <Object?>[
          <String, Object?>{
            'id': 't1', 'code': 'T1', 'name': 'Table 1',
            'capacity': 4, 'status': 'available',
            'is_active': true,
            'branch': <String, Object?>{'id': 'b1', 'name': 'Main'},
          },
        ],
        'orders': <Object?>[],
        'menu': <Object?>[],
      }, cursorScope: 'local');

      expect((await db.tables()).single['status'], 'available');

      await db.applyPull(<String, Object?>{
        'cursor': 9,
        'changes': <Object?>[
          <String, Object?>{
            'entity_type': 'dining_table',
            'entity_id': 't1',
            'operation': 'update',
            'data': <String, Object?>{
              'id': 't1', 'code': 'T1', 'name': 'Table 1',
              'capacity': 4, 'status': 'available',
              'is_active': true,
              'branch': <String, Object?>{'id': 'b1', 'name': 'Main'},
            },
          },
        ],
      }, cursorScope: 'local');
      expect((await db.tables()).single['status'], 'available');
    } finally {
      await db.close();
      await dir.delete(recursive: true);
    }
  });

  test('real unsynced offline draft continues to reserve its table',
      () async {
    final dir = await Directory.systemTemp.createTemp('table-draft-');
    final path = p.join(dir.path, 'waiter.db');
    final db = await LocalDatabase.open(databasePath: path);
    try {
      await db.createDraftOrder(
        clientOrderId: 'offline-order',
        mutationId: 'offline-order-open',
        tableId: 't2',
        branchId: 'b1',
        serviceType: 'dine_in',
        guestCount: 2,
      );

      await db.applyBootstrap(<String, Object?>{
        'cursor': 2,
        'branches': <Object?>[
          <String, Object?>{
            'id': 'b1', 'code': 'B1', 'name': 'Main',
            'is_active': true,
          },
        ],
        'tables': <Object?>[
          <String, Object?>{
            'id': 't2', 'code': 'T2', 'name': 'Table 2',
            'capacity': 4, 'status': 'available',
            'is_active': true,
            'branch': <String, Object?>{'id': 'b1', 'name': 'Main'},
          },
        ],
        'orders': <Object?>[],
        'menu': <Object?>[],
      }, cursorScope: 'local');

      expect((await db.tables()).single['status'], 'occupied');
      expect(await db.pendingCount(), 1);
    } finally {
      await db.close();
      await dir.delete(recursive: true);
    }
  });
}
