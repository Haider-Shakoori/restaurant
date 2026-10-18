import 'dart:io';

import 'package:businessos_restaurant_waiter/data/local/local_database.dart';
import 'package:businessos_restaurant_waiter/data/local/outbox_mutation.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:path/path.dart' as p;
import 'package:sqflite_common_ffi/sqflite_ffi.dart';

Map<String, Object?> desktopOrder({required String status, bool full = false}) =>
    <String, Object?>{
      'id': 'DESKTOP-ORDER-1',
      'client_order_id': 'ORDER-1',
      'display_number': 3,
      'branch_id': 'BRANCH-1',
      'status': status,
      'guest_count': 2,
      'subtotal': '400.00',
      'total': '400.00',
      'table': <String, Object?>{'id': 'TABLE-1'},
      'kot_rounds': <Object?>[
        <String, Object?>{'id': 'ROUND-1', 'round_number': 1},
      ],
      'items': <Object?>[
        <String, Object?>{
          'id': 'SERVER-LINE-1',
          'client_line_id': 'LINE-1',
          'item_name': 'Qabuli',
          'quantity': 2,
          'line_total': '400.00',
          'status': status == 'closed' ? 'served' : 'submitted',
          if (full) 'unit_price': '200.00',
          if (full) 'menu_item_id': 'MENU-1',
          if (full) 'modifiers_snapshot': <Object?>[],
        },
      ],
    };

Map<String, Object?> snapshot(Map<String, Object?> order) =>
    <String, Object?>{
      'cursor': 12,
      'branches': <Object?>[],
      'menu': <Object?>[],
      'tables': <Object?>[],
      'orders': <Object?>[order],
    };

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  sqfliteFfiInit();
  databaseFactory = databaseFactoryFfi;

  test('real desktop closed event without unit_price does not crash', () async {
    final dir = await Directory.systemTemp.createTemp('restaurant-missing-price-');
    final db = await LocalDatabase.open(
      databasePath: p.join(dir.path, 'restaurant.db'),
    );
    try {
      await db.applyBootstrap(snapshot(desktopOrder(status: 'closed')),
          cursorScope: 'local');
      final item = (await db.orderItems('ORDER-1')).single;
      expect(item['unit_price'], '200.00');
      expect(item['line_total'], '400.00');
      expect(item['dispatched_quantity'], 2);
      expect(item['status'], 'served');
      expect((await db.order('ORDER-1'))!['status'], 'closed');
    } finally {
      await db.close();
      await dir.delete(recursive: true);
    }
  });

  test('partial event retains known menu item, price, and sent quantity',
      () async {
    final dir = await Directory.systemTemp.createTemp('restaurant-price-merge-');
    final db = await LocalDatabase.open(
      databasePath: p.join(dir.path, 'restaurant.db'),
    );
    try {
      await db.applyBootstrap(
          snapshot(desktopOrder(status: 'submitted', full: true)),
          cursorScope: 'local');
      await db.applyPull(
        <String, Object?>{
          'cursor': 13,
          'changes': <Object?>[
            <String, Object?>{
              'entity_type': 'order',
              'entity_id': 'DESKTOP-ORDER-1',
              'operation': 'upsert',
              'data': desktopOrder(status: 'closed'),
            },
          ],
        },
        cursorScope: 'local',
      );
      final item = (await db.orderItems('ORDER-1')).single;
      expect(item['menu_item_id'], 'MENU-1');
      expect(item['unit_price'], '200.00');
      expect(item['dispatched_quantity'], 2);
    } finally {
      await db.close();
      await dir.delete(recursive: true);
    }
  });

  test('confirmed duplicate KOT is archived, rejected item addition is not',
      () async {
    final dir = await Directory.systemTemp.createTemp('restaurant-conflicts-');
    final db = await LocalDatabase.open(
      databasePath: p.join(dir.path, 'restaurant.db'),
    );
    try {
      Future<void> conflict(String id, String operation) async {
        await db.markConflict(
          OutboxMutation(
            id: 1,
            mutationId: id,
            operation: operation,
            payload: const <String, Object?>{
              'client_order_id': 'ORDER-1',
            },
            occurredAt: DateTime.utc(2026, 10, 18),
            attempts: 0,
          ),
          <String, Object?>{
            'mutation_id': id,
            'status': 'conflict',
            'code': 'order_state_conflict',
            'message': 'Cannot submit to an already closed order.',
          },
        );
      }

      await conflict('DUPLICATE-SUBMIT', 'order.submit');
      await conflict('REJECTED-ITEM-ADD', 'order.item.add');
      expect(await db.conflictCount(), 2);
      await db.applyBootstrap(snapshot(desktopOrder(status: 'closed')),
          cursorScope: 'local');

      expect(await db.conflictCount(), 1);
      expect((await db.conflicts()).single['operation'], 'order.item.add');
      final history = await db.conflicts(archived: true);
      expect(history.length, 1);
      expect(history.single['operation'], 'order.submit');
      expect(history.single['resolved_at'], isNotNull);
      expect(history.single['resolution'], contains('not replayed'));
    } finally {
      await db.close();
      await dir.delete(recursive: true);
    }
  });
}
