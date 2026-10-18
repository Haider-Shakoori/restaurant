import 'dart:io';

import 'package:businessos_restaurant_waiter/data/local/local_database.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:path/path.dart' as p;
import 'package:sqflite_common_ffi/sqflite_ffi.dart';

Map<String, Object?> table(String id, String branch) => <String, Object?>{
  'id': id,
  'code': id,
  'name': 'Table $id',
  'capacity': 4,
  'status': 'available',
  'is_active': true,
  'branch': <String, Object?>{'id': branch, 'name': branch},
  'area': <String, Object?>{'id': 'hall-$branch', 'name': 'Dining hall'},
};

Future<LocalDatabase> database(Directory dir) async {
  final db = await LocalDatabase.open(
    databasePath: p.join(dir.path, 'restaurant.db'),
  );
  await db.applyBootstrap(
    <String, Object?>{
      'cursor': 1,
      'branches': <Object?>[
        <String, Object?>{'id': 'B1', 'code': 'B1', 'name': 'One'},
        <String, Object?>{'id': 'B2', 'code': 'B2', 'name': 'Two'},
      ],
      'menu': <Object?>[],
      'tables': <Object?>[
        table('T1', 'B1'),
        table('T2', 'B1'),
        table('T3', 'B1'),
        table('T4', 'B2'),
      ],
      'orders': <Object?>[],
    },
    cursorScope: 'local',
  );
  await db.createDraftOrder(
    clientOrderId: 'O1',
    mutationId: 'OPEN-1',
    branchId: 'B1',
    tableId: 'T1',
    serviceType: 'dine_in',
    guestCount: 2,
  );
  await db.createDraftOrder(
    clientOrderId: 'O2',
    mutationId: 'OPEN-2',
    branchId: 'B1',
    tableId: 'T2',
    serviceType: 'dine_in',
    guestCount: 2,
  );
  return db;
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  sqfliteFfiInit();
  databaseFactory = databaseFactoryFfi;

  test('reject busy, same-table and cross-branch transfers atomically',
      () async {
    final dir = await Directory.systemTemp.createTemp('table-guard-');
    final db = await database(dir);
    try {
      final initial = await db.pendingCount();
      for (final id in <String>['T1', 'T2', 'T4', 'unknown']) {
        await expectLater(
          db.transferOrderTable(
            localOrderId: 'O1',
            targetTableId: id,
            mutationId: 'TRY-$id',
          ),
          throwsA(isA<StateError>()),
        );
      }
      expect((await db.order('O1'))!['table_id'], 'T1');
      expect(await db.pendingCount(), initial);
      expect(
        (await db.tables())
            .where((t) => t['status'] == 'occupied')
            .map((t) => t['id']),
        containsAll(<String>['T1', 'T2']),
      );
      await db.transferOrderTable(
        localOrderId: 'O1',
        targetTableId: 'T3',
        mutationId: 'VALID-TRANSFER',
      );
      expect((await db.order('O1'))!['table_id'], 'T3');
      expect((await db.tables()).firstWhere((t) => t['id'] == 'T1')['status'],
          'available');
      expect((await db.tables()).firstWhere((t) => t['id'] == 'T3')['status'],
          'occupied');
      expect(await db.pendingCount(), initial + 1);
    } finally {
      await db.close();
      await dir.delete(recursive: true);
    }
  });

  test('move is unsent-only and cannot target itself or unknown orders',
      () async {
    final dir = await Directory.systemTemp.createTemp('move-guard-');
    final db = await database(dir);
    try {
      await db.addDraftItem(
        localOrderId: 'O1',
        clientLineId: 'L1',
        mutationId: 'ADD-L1',
        menuItem: <String, Object?>{
          'id': 'M1', 'name': 'Qabuli', 'price': '200.00',
        },
        quantity: 2,
      );
      final count = await db.pendingCount();
      for (final target in <String>['O1', 'UNKNOWN']) {
        await expectLater(
          db.moveUnsentOrderItem(
            sourceLocalOrderId: 'O1',
            targetLocalOrderId: target,
            localLineId: 'L1',
            targetLocalLineId: 'MOVED-$target',
            quantity: 1,
            mutationId: 'MOVE-$target',
          ),
          throwsA(isA<StateError>()),
        );
      }
      await expectLater(
        db.moveUnsentOrderItem(
          sourceLocalOrderId: 'O1',
          targetLocalOrderId: 'O2',
          localLineId: 'L1',
          targetLocalLineId: 'TOO-MANY',
          quantity: 3,
          mutationId: 'MOVE-TOO-MANY',
        ),
        throwsA(isA<StateError>()),
      );
      expect(await db.pendingCount(), count);
      expect((await db.orderItems('O1')).single['quantity'], 2);
      expect(await db.orderItems('O2'), isEmpty);

      await db.moveUnsentOrderItem(
        sourceLocalOrderId: 'O1',
        targetLocalOrderId: 'O2',
        localLineId: 'L1',
        targetLocalLineId: 'L2',
        quantity: 1,
        mutationId: 'MOVE-GOOD',
      );
      expect((await db.orderItems('O1')).single['quantity'], 1);
      expect((await db.orderItems('O2')).single['quantity'], 1);
      expect(await db.pendingCount(), count + 1);
    } finally {
      await db.close();
      await dir.delete(recursive: true);
    }
  });

  test('merge refuses self and unknown target, preserves orders on failure',
      () async {
    final dir = await Directory.systemTemp.createTemp('merge-guard-');
    final db = await database(dir);
    try {
      final initial = await db.pendingCount();
      for (final target in <String>['O1', 'MISSING']) {
        await expectLater(
          db.mergeOrders(
            sourceLocalOrderId: 'O1',
            targetLocalOrderId: target,
            mutationId: 'MERGE-$target',
          ),
          throwsA(isA<StateError>()),
        );
      }
      expect((await db.order('O1'))!['status'], 'draft');
      expect((await db.order('O2'))!['status'], 'draft');
      expect(await db.pendingCount(), initial);
    } finally {
      await db.close();
      await dir.delete(recursive: true);
    }
  });
}
