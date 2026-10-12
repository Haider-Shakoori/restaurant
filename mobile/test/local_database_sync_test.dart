import 'dart:convert';
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

  Future<LocalDatabase> openDatabaseIn(Directory dir) {
    return LocalDatabase.open(databasePath: p.join(dir.path, 'restaurant.db'));
  }

  Future<void> inTempDatabase(
    Future<void> Function(LocalDatabase database) body,
  ) async {
    final dir = await Directory.systemTemp.createTemp('restaurant-sync-');
    try {
      final database = await openDatabaseIn(dir);
      try {
        await body(database);
      } finally {
        await database.close();
      }
    } finally {
      await dir.delete(recursive: true);
    }
  }

  test(
    'pending mutations preserve creation order while an earlier one backs off',
    () async {
      await inTempDatabase((database) async {
        await database.createDraftOrder(
          clientOrderId: 'order-1',
          mutationId: 'mutation-1',
          branchId: 'branch-1',
          serviceType: 'takeaway',
          guestCount: 1,
        );

        final first = (await database.pendingMutations()).single;
        expect(first.mutationId, 'mutation-1');

        // Defer the first mutation into the future as a transient retry would.
        await database.markRetry(
          first,
          message: 'offline',
          retryAt: DateTime.now().toUtc().add(const Duration(minutes: 5)),
        );

        await database.createDraftOrder(
          clientOrderId: 'order-2',
          mutationId: 'mutation-2',
          branchId: 'branch-1',
          serviceType: 'takeaway',
          guestCount: 1,
        );

        // The later mutation must not overtake the deferred prerequisite.
        expect(await database.pendingMutations(), isEmpty);

        // Once the prerequisite is due again, both are returned in id order.
        final deferred = OutboxMutation(
          id: first.id,
          mutationId: first.mutationId,
          operation: first.operation,
          payload: first.payload,
          occurredAt: first.occurredAt,
          attempts: first.attempts + 1,
        );
        await database.markRetry(
          deferred,
          message: 'due',
          retryAt: DateTime.now().toUtc().subtract(const Duration(seconds: 1)),
        );

        final ready = await database.pendingMutations();
        expect(ready.map((mutation) => mutation.mutationId).toList(), <String>[
          'mutation-1',
          'mutation-2',
        ]);
      });
    },
  );

  test(
    'negative modifier price deltas are applied to the line price',
    () async {
      await inTempDatabase((database) async {
        await database.createDraftOrder(
          clientOrderId: 'order-discount',
          mutationId: 'open-discount',
          branchId: 'branch-1',
          serviceType: 'takeaway',
          guestCount: 1,
        );

        await database.addDraftItem(
          localOrderId: 'order-discount',
          clientLineId: 'line-1',
          mutationId: 'add-discount',
          quantity: 1,
          menuItem: <String, Object?>{
            'id': 'item-1',
            'name': 'Kabuli Pulao',
            'price': '10.00',
            'modifier_groups_json': jsonEncode(<Object?>[
              <String, Object?>{
                'id': 'group-1',
                'name': 'Extras',
                'min_selections': 0,
                'max_selections': 1,
                'options': <Object?>[
                  <String, Object?>{
                    'id': 'option-1',
                    'name': 'No salad',
                    'price_delta': '-1.50',
                  },
                ],
              },
            ]),
          },
          modifiers: <Map<String, Object?>>[
            <String, Object?>{'option_id': 'option-1'},
          ],
        );

        final order = (await database.order('order-discount'))!;
        expect(order['subtotal'], '8.50');
        expect(order['total'], '8.50');

        final line = (await database.orderItems('order-discount')).single;
        expect(line['unit_price'], '8.50');
        expect(line['line_total'], '8.50');
      });
    },
  );

  test(
    'a conflicted order.submit rolls back the optimistic local state',
    () async {
      await inTempDatabase((database) async {
        await database.createDraftOrder(
          clientOrderId: 'order-rollback',
          mutationId: 'open-rollback',
          branchId: 'branch-1',
          serviceType: 'takeaway',
          guestCount: 1,
        );
        await database.addDraftItem(
          localOrderId: 'order-rollback',
          clientLineId: 'line-rollback',
          mutationId: 'add-rollback',
          quantity: 2,
          menuItem: <String, Object?>{
            'id': 'item-2',
            'name': 'Bread',
            'price': '2.00',
          },
        );
        await database.submitDraftOrder(
          localOrderId: 'order-rollback',
          mutationId: 'submit-rollback',
        );

        var order = (await database.order('order-rollback'))!;
        var line = (await database.orderItems('order-rollback')).single;
        expect(order['status'], 'submitted_pending_sync');
        expect(line['dispatched_quantity'], 2);

        await database.markConflict(
          OutboxMutation(
            id: 3,
            mutationId: 'submit-rollback',
            operation: 'order.submit',
            payload: const <String, Object?>{
              'client_order_id': 'order-rollback',
            },
            occurredAt: DateTime.now().toUtc(),
            attempts: 0,
          ),
          const <String, Object?>{
            'code': 'order_state_conflict',
            'message': 'Order was already closed.',
          },
        );

        order = (await database.order('order-rollback'))!;
        line = (await database.orderItems('order-rollback')).single;
        expect(order['status'], 'draft');
        expect(line['dispatched_quantity'], 0);
        expect(await database.conflictCount(), 1);
      });
    },
  );

  test('discarding a conflict clears its rollback state', () async {
    await inTempDatabase((database) async {
      await database.createDraftOrder(
        clientOrderId: 'order-discard',
        mutationId: 'open-discard',
        branchId: 'branch-1',
        serviceType: 'takeaway',
        guestCount: 1,
      );
      await database.addDraftItem(
        localOrderId: 'order-discard',
        clientLineId: 'line-discard',
        mutationId: 'add-discard',
        quantity: 1,
        menuItem: <String, Object?>{
          'id': 'item-3',
          'name': 'Tea',
          'price': '1.00',
        },
      );
      await database.submitDraftOrder(
        localOrderId: 'order-discard',
        mutationId: 'submit-discard',
      );
      await database.markConflict(
        OutboxMutation(
          id: 3,
          mutationId: 'submit-discard',
          operation: 'order.submit',
          payload: const <String, Object?>{'client_order_id': 'order-discard'},
          occurredAt: DateTime.now().toUtc(),
          attempts: 0,
        ),
        const <String, Object?>{'code': 'order_state_conflict'},
      );

      await database.discardMutation('submit-discard');

      expect(await database.conflictCount(), 0);
      expect(await database.pendingCount(), 2);
    });
  });
}
