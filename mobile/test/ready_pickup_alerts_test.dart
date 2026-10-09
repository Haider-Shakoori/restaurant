import 'dart:convert';

import 'package:businessos_restaurant_waiter/features/tables/ready_pickup_alerts.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final tables = <Map<String, Object?>>[
    {'id': 'table-1', 'name': 'Table 01'},
  ];

  Map<String, Object?> order({
    String status = 'submitted',
    String itemStatus = 'ready',
    String itemId = 'item-1',
  }) => {
    'local_order_id': 'order-1',
    'status': status,
    'table_id': 'table-1',
    'service_type': 'dine_in',
    'kitchen_tickets_json': jsonEncode([
      {
        'id': 'ticket-1',
        'items': [
          {'id': itemId, 'status': itemStatus, 'item_name': 'Qabuli'},
        ],
      },
    ]),
  };

  test('only remotely ready ticket items trigger pickup for the right table', () {
    final pending = readyPickupAlerts([order(itemStatus: 'preparing')], tables);
    expect(pending, isEmpty);

    final ready = readyPickupAlerts([order()], tables);
    expect(ready, hasLength(1));
    expect(ready.single.reference, 'Table 01');
    expect(ready.single.keys, ['item:item-1']);
    expect(ready.single.itemCount, 1);
  });

  test('ready for pickup persists until served, while exception stages do not', () {
    expect(readyPickupAlerts([order(status: 'served')], tables), isEmpty);
    expect(readyPickupAlerts([order(status: 'closed')], tables), isEmpty);
    expect(readyPickupAlerts([order(itemStatus: 'expo')], tables), isEmpty);
    expect(readyPickupAlerts([order(status: 'ready',
        itemStatus: 'preparing')], tables).single.keys, ['order:order-1']);
  });

  test('sound-worthy alerts are once-only for each kitchen item', () {
    final tracker = ReadyPickupTracker();
    final ready = readyPickupAlerts([order()], tables);
    expect(tracker.newlyReady(ready), hasLength(1));
    expect(tracker.newlyReady(ready), isEmpty);
    final reopened = ReadyPickupTracker.decode(tracker.encode());
    expect(reopened.newlyReady(ready), isEmpty);

    final anotherRound = readyPickupAlerts(
      [order(itemId: 'item-2')], tables);
    expect(reopened.newlyReady(anotherRound), hasLength(1));
  });

  test('ready takeaway and counter orders show the service reference', () {
    final takeaway = readyPickupAlerts([
      {
        'local_order_id': 'takeaway-1',
        'service_type': 'takeaway',
        'service_reference': 'TK-07',
        'status': 'ready',
      },
    ], tables);
    expect(takeaway.single.reference, 'TAKEAWAY · TK-07');
  });
}
