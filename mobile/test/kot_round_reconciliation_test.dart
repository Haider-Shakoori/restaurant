import 'dart:convert';
import 'dart:io';

import 'package:businessos_restaurant_waiter/data/local/local_database.dart';
import 'package:businessos_restaurant_waiter/features/tables/ready_pickup_alerts.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:path/path.dart' as p;
import 'package:sqflite_common_ffi/sqflite_ffi.dart';

Map<String, Object?> remoteOrder({
  String status = 'submitted',
  bool includeSecond = false,
}) => <String, Object?>{
  'id': 'SERVER-ORDER',
  'client_order_id': 'LOCAL-ORDER',
  'display_number': 2,
  'branch_id': 'BR-1',
  'service_type': 'dine_in',
  'status': status,
  'guest_count': 1,
  'subtotal': includeSecond ? '400.00' : '200.00',
  'total': includeSecond ? '400.00' : '200.00',
  'table': <String, Object?>{'id': 'T-01'},
  'kot_rounds': <Object?>[
    <String, Object?>{'id': 'ROUND-1', 'round_number': 1},
    if (includeSecond)
      <String, Object?>{'id': 'ROUND-2', 'round_number': 2},
  ],
  'kitchen_tickets': <Object?>[
    <String, Object?>{
      'id': 'TICKET-1',
      'items': <Object?>[
        <String, Object?>{'id': 'KT-1', 'status': status == 'ready' ? 'ready' : 'queued'},
      ],
    },
  ],
  'items': <Object?>[
    <String, Object?>{
      'id': 'SERVER-LINE-1',
      'client_line_id': 'LINE-1',
      'menu_item_id': 'MENU-1',
      'item_name': 'Qabuli',
      'unit_price': '200.00',
      'quantity': 1,
      'kot_round_id': 'ROUND-1',
      'round_number': 1,
      'line_total': '200.00',
      'status': status == 'ready' ? 'ready' : 'submitted',
    },
    if (includeSecond)
      <String, Object?>{
        'id': 'SERVER-LINE-2',
        'client_line_id': 'LINE-2',
        'menu_item_id': 'MENU-1',
        'item_name': 'Qabuli',
        'unit_price': '200.00',
        'quantity': 1,
        'kot_round_id': 'ROUND-2',
        'round_number': 2,
        'line_total': '200.00',
        'status': status == 'ready' ? 'ready' : 'submitted',
      },
  ],
};

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  sqfliteFfiInit();
  databaseFactory = databaseFactoryFfi;

  test('sent KOT rounds cannot be sent twice after server snapshot', () async {
    final dir = await Directory.systemTemp.createTemp('restaurant-sent-kot-');
    final db = await LocalDatabase.open(
      databasePath: p.join(dir.path, 'waiter.db'),
    );
    try {
      await db.applyBootstrap(
        <String, Object?>{
          'cursor': 15,
          'branches': <Object?>[],
          'menu': <Object?>[],
          'tables': <Object?>[],
          'orders': <Object?>[remoteOrder(includeSecond: true)],
        },
        cursorScope: 'local',
      );

      expect(await db.hasUnsentItems('LOCAL-ORDER'), false);
      final lines = await db.orderItems('LOCAL-ORDER');
      expect(lines.length, 2);
      expect(lines.map((line) => line['dispatched_quantity']), [1, 1]);
      await expectLater(
        db.submitDraftOrder(
          localOrderId: 'LOCAL-ORDER',
          mutationId: 'DUPLICATE-KOT',
        ),
        throwsA(isA<StateError>()),
      );
      expect(await db.pendingCount(), 0);
    } finally {
      await db.close();
      await dir.delete(recursive: true);
    }
  });

  test('partial READY order update retains items and table for sound alert',
      () async {
    final dir = await Directory.systemTemp.createTemp('restaurant-ready-');
    final db = await LocalDatabase.open(
      databasePath: p.join(dir.path, 'waiter.db'),
    );
    try {
      await db.applyBootstrap(
        <String, Object?>{
          'cursor': 10,
          'branches': <Object?>[],
          'menu': <Object?>[],
          'tables': <Object?>[],
          'orders': <Object?>[remoteOrder()],
        },
        cursorScope: 'local',
      );
      final ticket = <String, Object?>{
        'id': 'TICKET-1',
        'items': <Object?>[
          <String, Object?>{
            'id': 'KT-1', 'status': 'ready', 'item_name': 'Qabuli',
          },
        ],
      };
      await db.applyPull(
        <String, Object?>{
          'cursor': 11,
          'changes': <Object?>[
            <String, Object?>{
              'entity_type': 'order',
              'entity_id': 'SERVER-ORDER',
              'operation': 'upsert',
              'data': <String, Object?>{
                'id': 'SERVER-ORDER',
                'client_order_id': 'LOCAL-ORDER',
                'display_number': 2,
                'status': 'ready',
                'kot_rounds': <Object?>[],
                'kitchen_tickets': <Object?>[ticket],
              },
            },
          ],
        },
        cursorScope: 'local',
      );

      final order = (await db.order('LOCAL-ORDER'))!;
      expect(order['status'], 'ready');
      expect(order['table_id'], 'T-01');
      expect(order['service_type'], 'dine_in');
      expect(await db.hasUnsentItems('LOCAL-ORDER'), false);
      expect((await db.orderItems('LOCAL-ORDER')).length, 1);

      final alerts = readyPickupAlerts(
        await db.activeOrders(),
        <Map<String, Object?>>[
          <String, Object?>{'id': 'T-01', 'name': 'Table 01'},
        ],
      );
      expect(alerts.single.reference, '#2 · Table 01');
      expect(alerts.single.itemCount, 1);
      expect(jsonDecode(order['kitchen_tickets_json'] as String), [ticket]);
    } finally {
      await db.close();
      await dir.delete(recursive: true);
    }
  });
}
