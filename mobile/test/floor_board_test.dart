import 'package:businessos_restaurant_waiter/app/app_theme.dart';
import 'package:businessos_restaurant_waiter/features/tables/floor_board.dart';
import 'package:businessos_restaurant_waiter/features/tables/ready_pickup_alerts.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

final tables = <Map<String, Object?>>[
  {'id': 't1', 'code': 'T01', 'name': 'Table 01',
   'area_name': 'Main Hall', 'status': 'available'},
  {'id': 't2', 'code': 'T02', 'name': 'Table 02',
   'area_name': 'Main Hall', 'status': 'occupied'},
  {'id': 't3', 'code': 'T03', 'name': 'Table 03',
   'area_name': 'Terrace', 'status': 'occupied'},
  {'id': 't4', 'code': 'T04', 'name': 'Table 04',
   'area_name': 'Terrace', 'status': 'reserved'},
];

final orders = <Map<String, Object?>>[
  {'table_id': 't3', 'local_order_id': 'o3',
    'status': 'ready', 'display_number': 23},
];

final ready = <ReadyPickupAlert>[
  const ReadyPickupAlert(
    orderId: 'o3', reference: '#23 · Table 03',
    itemCount: 2, keys: ['k1', 'k2'],
  ),
];

void main() {
  test('state model uses authoritative occupancy and enriches READY', () {
    final floor = buildFloorTables(tables, orders, ready);
    expect(floor.map((t) => t.category).toList(), [
      FloorCategory.available,
      FloorCategory.occupied,
      FloorCategory.ready,
      FloorCategory.other,
    ]);
    expect(floor[2].label, 'Ready for pickup');
    expect(floor[2].orderNumber, '#23');
    expect(floor[3].label, 'Reserved');
  });

  test('closed/cancelled orders do not override the desktop table state', () {
    final result = buildFloorTables(
      [tables.first],
      [
        {'table_id': 't1', 'local_order_id': 'c1',
          'status': 'closed'},
      ],
      [],
    );
    expect(result.single.category, FloorCategory.available);
  });

  testWidgets('search, filters, and tap act on the correct table', (tester) async {
    final tapped = <String>[];
    await tester.pumpWidget(MaterialApp(
      theme: buildRestaurantTheme(),
      home: Scaffold(body: SingleChildScrollView(child: Padding(
        padding: const EdgeInsets.all(12),
        child: FloorBoard(
          tables: tables, orders: orders, readyAlerts: ready,
          offline: false,
          onTableTap: (table) => tapped.add(table['id'].toString()),
        ),
      ))),
    ));
    expect(find.byKey(const Key('floor-table-t1')), findsOneWidget);
    expect(find.byKey(const Key('floor-table-t4')), findsOneWidget);
    expect(find.text('Ready for pickup'), findsOneWidget);

    await tester.tap(find.byKey(const Key('floor-filter-available')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('floor-table-t1')), findsOneWidget);
    expect(find.byKey(const Key('floor-table-t2')), findsNothing);

    await tester.tap(find.byKey(const Key('floor-table-t1')));
    expect(tapped, ['t1']);

    await tester.tap(find.byKey(const Key('floor-filter-all')));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('floor-table-search')), 'terrace');
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('floor-table-t3')), findsOneWidget);
    expect(find.byKey(const Key('floor-table-t4')), findsOneWidget);
    expect(find.byKey(const Key('floor-table-t1')), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('offline mode clearly marks every displayed state cached',
      (tester) async {
    await tester.pumpWidget(MaterialApp(
      theme: buildRestaurantTheme(),
      home: Scaffold(body: SingleChildScrollView(
        child: FloorBoard(
          tables: tables.take(2).toList(),
          orders: const [], readyAlerts: const [], offline: true,
          onTableTap: (_) {},
        ),
      )),
    ));
    expect(find.textContaining('Cached · Available'), findsOneWidget);
    expect(find.textContaining('Cached · Occupied'), findsOneWidget);
    expect(find.textContaining('Offline: table states are cached'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
