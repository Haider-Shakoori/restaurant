import 'dart:io';

import 'package:businessos_restaurant_waiter/data/local/local_database.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:path/path.dart' as p;
import 'package:sqflite/sqflite.dart';
import 'package:sqflite_common_ffi/sqflite_ffi.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  sqfliteFfiInit();
  databaseFactory = databaseFactoryFfi;

  test('v6 legacy non-null table ID upgrades without losing orders or outbox',
      () async {
    final dir = await Directory.systemTemp.createTemp('restaurant-sqlite-upgrade-');
    final databasePath = p.join(dir.path, 'legacy.db');
    try {
      final old = await openDatabase(
        databasePath,
        version: 6,
        onCreate: (db, version) async {
          await db.execute('''
            CREATE TABLE orders (
              local_order_id TEXT PRIMARY KEY,
              client_order_id TEXT,
              server_id TEXT,
              table_id TEXT NOT NULL,
              branch_id TEXT,
              service_type TEXT NOT NULL DEFAULT 'dine_in',
              service_reference TEXT,
              status TEXT NOT NULL,
              guest_count INTEGER NOT NULL,
              notes TEXT,
              subtotal TEXT NOT NULL DEFAULT '0.00',
              total TEXT NOT NULL DEFAULT '0.00',
              opened_at TEXT,
              submitted_at TEXT,
              served_at TEXT,
              closed_at TEXT,
              kot_rounds_json TEXT NOT NULL DEFAULT '[]',
              kitchen_tickets_json TEXT NOT NULL DEFAULT '[]',
              updated_at TEXT NOT NULL
            )
          ''');
          await db.execute('CREATE INDEX orders_status_idx ON orders(status, updated_at)');
          await db.execute('''
            CREATE TABLE outbox (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              mutation_id TEXT NOT NULL UNIQUE,
              operation TEXT NOT NULL,
              payload_json TEXT NOT NULL,
              occurred_at TEXT NOT NULL,
              status TEXT NOT NULL DEFAULT 'pending',
              attempts INTEGER NOT NULL DEFAULT 0,
              next_attempt_at TEXT,
              last_error TEXT
            )
          ''');
          await db.execute('''
            CREATE TABLE dining_tables (
              id TEXT PRIMARY KEY,
              status TEXT NOT NULL
            )
          ''');
          await db.execute('''
            INSERT INTO orders
              (local_order_id, client_order_id, table_id, branch_id,
               status, guest_count, updated_at)
            VALUES ('legacy-order', 'legacy-client', 'table-1', 'branch-1',
                    'draft', 2, '2026-10-09T00:00:00Z')
          ''');
          await db.execute('''
            INSERT INTO outbox(mutation_id, operation, payload_json, occurred_at)
            VALUES ('legacy-mutation', 'order.open', '{}', '2026-10-09T00:00:00Z')
          ''');
        },
      );
      await old.close();

      final database = await LocalDatabase.open(databasePath: databasePath);
      final existing = await database.order('legacy-order');
      expect(existing, isNotNull);
      expect(existing!['table_id'], 'table-1');
      expect(await database.pendingCount(), 1);

      await database.createDraftOrder(
        clientOrderId: 'takeaway-1',
        mutationId: 'new-mutation',
        tableId: null,
        branchId: 'branch-1',
        serviceType: 'takeaway',
        guestCount: 1,
      );
      await database.createDraftOrder(
        clientOrderId: 'counter-1',
        mutationId: 'counter-mutation',
        branchId: 'branch-1',
        serviceType: 'counter',
        guestCount: 1,
      );
      expect((await database.order('takeaway-1'))!['table_id'], isNull);
      expect((await database.order('counter-1'))!['table_id'], isNull);
      expect(await database.pendingCount(), 3);

      await database.close();

      // The migration must also retain the order-status query index.
      final migrated = await openDatabase(databasePath);
      final columns = await migrated.rawQuery('PRAGMA table_info(orders)');
      expect(columns.singleWhere((c) => c['name'] == 'table_id')['notnull'], 0);
      final index = await migrated.rawQuery('PRAGMA index_list(orders)');
      expect(index.any((entry) => entry['name'] == 'orders_status_idx'), isTrue);
      await migrated.close();
    } finally {
      await dir.delete(recursive: true);
    }
  });
}
