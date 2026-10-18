import 'dart:io';

import 'package:businessos_restaurant_waiter/data/local/local_database.dart';
import 'package:businessos_restaurant_waiter/data/local/outbox_mutation.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:path/path.dart' as p;
import 'package:sqflite_common_ffi/sqflite_ffi.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  sqfliteFfiInit();
  databaseFactory = databaseFactoryFfi;

  test('LAN conflict closes stale order but preserves the conflict record',
      () async {
    final dir = await Directory.systemTemp.createTemp('restaurant-closed-order-');
    final db = await LocalDatabase.open(
      databasePath: p.join(dir.path, 'restaurant.db'),
    );
    try {
      await db.createDraftOrder(
        clientOrderId: 'ORDER-CANCELLED',
        mutationId: 'M-OPEN',
        branchId: 'branch-1',
        tableId: null,
        serviceType: 'takeaway',
        guestCount: 1,
      );
      final mutation = OutboxMutation(
        id: 1,
        mutationId: 'M-OPEN',
        operation: 'order.open',
        payload: const <String, Object?>{
          'client_order_id': 'ORDER-CANCELLED',
        },
        occurredAt: DateTime.utc(2026, 10, 18),
        attempts: 0,
      );

      await db.markConflict(mutation, const <String, Object?>{
        'mutation_id': 'M-OPEN',
        'status': 'conflict',
        'code': 'order_state_conflict',
        'message': 'Order has already been cancelled.',
        'client_order_id': 'ORDER-CANCELLED',
        'server_order_status': 'cancelled',
      });

      expect((await db.order('ORDER-CANCELLED'))!['status'], 'cancelled');
      expect(await db.conflictCount(), 1);
      expect(await db.pendingCount(), 0);
      await expectLater(
        db.submitDraftOrder(
          localOrderId: 'ORDER-CANCELLED',
          mutationId: 'M-SUBMIT-LATE',
        ),
        throwsA(isA<StateError>()),
      );
      expect(await db.pendingCount(), 0);
    } finally {
      await db.close();
      await dir.delete(recursive: true);
    }
  });

  test('conflict status from another order cannot change local order', () async {
    final dir = await Directory.systemTemp.createTemp('restaurant-conflict-id-');
    final db = await LocalDatabase.open(
      databasePath: p.join(dir.path, 'restaurant.db'),
    );
    try {
      await db.createDraftOrder(
        clientOrderId: 'ORDER-A',
        mutationId: 'M-OPEN',
        branchId: 'branch-1',
        tableId: null,
        serviceType: 'takeaway',
        guestCount: 1,
      );
      await db.markConflict(
        OutboxMutation(
          id: 1,
          mutationId: 'M-OPEN',
          operation: 'order.open',
          payload: const <String, Object?>{
            'client_order_id': 'ORDER-A',
          },
          occurredAt: DateTime.utc(2026, 10, 18),
          attempts: 0,
        ),
        const <String, Object?>{
          'code': 'order_state_conflict',
          'client_order_id': 'ORDER-B',
          'server_order_status': 'closed',
        },
      );
      expect((await db.order('ORDER-A'))!['status'], 'draft');
    } finally {
      await db.close();
      await dir.delete(recursive: true);
    }
  });
}
