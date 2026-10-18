import 'package:flutter/material.dart';

import '../../app/app_theme.dart';
import 'ready_pickup_alerts.dart';

enum FloorCategory { all, available, occupied, ready, other }

class FloorTable {
  const FloorTable(this.raw, this.category, this.label, this.orderNumber);

  final Map<String, Object?> raw;
  final FloorCategory category;
  final String label;
  final String? orderNumber;

  String get id => raw['id']?.toString() ?? '';
  String get name => raw['name']?.toString() ?? raw['code']?.toString() ?? 'Table';
  String get area => raw['area_name']?.toString() ?? '';
}

/// The desktop table state stays authoritative. A ready order enriches it;
/// no local view calculation alters the database or overwrites occupancy.
List<FloorTable> buildFloorTables(
  List<Map<String, Object?>> tables,
  List<Map<String, Object?>> orders,
  List<ReadyPickupAlert> readyAlerts,
) {
  final active = <String, Map<String, Object?>>{};
  for (final order in orders) {
    final id = order['table_id']?.toString();
    if (id == null || id.isEmpty ||
        const {'closed', 'cancelled', 'billed', 'inactive_remote'}
            .contains(order['status'])) {
      continue;
    }
    active.putIfAbsent(id, () => order);
  }
  final ready = readyAlerts.map((a) => a.orderId).toSet();
  return tables.map((table) {
    final order = active[table['id']?.toString()];
    final rawStatus = table['status']?.toString().toLowerCase() ?? '';
    final isReady = order != null &&
        (order['status'] == 'ready' ||
            ready.contains(order['local_order_id']?.toString()));
    final category = isReady
        ? FloorCategory.ready
        : rawStatus == 'available' && order == null
            ? FloorCategory.available
            : rawStatus == 'occupied' || order != null
                ? FloorCategory.occupied
                : FloorCategory.other;
    final label = isReady
        ? 'Ready for pickup'
        : rawStatus == 'available' && order == null
            ? 'Available'
            : rawStatus == 'reserved'
                ? 'Reserved'
                : rawStatus == 'waiting_to_pay'
                    ? 'Payment due'
                    : rawStatus == 'occupied' || order != null
                        ? (order?['status'] == 'preparing' ? 'Preparing' : 'Occupied')
                        : rawStatus.isEmpty ? 'Unknown' : rawStatus.replaceAll('_', ' ');
    final number = (order?['display_number'] as num?)?.toInt();
    return FloorTable(table, category, label,
        number != null && number > 0 ? '#' + number.toString() : null);
  }).toList(growable: false);
}

/// Standalone, testable and responsive floor UI. The parent owns order actions.
class FloorBoard extends StatefulWidget {
  const FloorBoard({
    required this.tables,
    required this.orders,
    required this.readyAlerts,
    required this.offline,
    required this.onTableTap,
    super.key,
  });

  final List<Map<String, Object?>> tables;
  final List<Map<String, Object?>> orders;
  final List<ReadyPickupAlert> readyAlerts;
  final bool offline;
  final ValueChanged<Map<String, Object?>> onTableTap;

  @override
  State<FloorBoard> createState() => _FloorBoardState();
}

class _FloorBoardState extends State<FloorBoard> {
  FloorCategory _filter = FloorCategory.all;
  String _query = '';

  @override
  Widget build(BuildContext context) {
    final floor = buildFloorTables(
        widget.tables, widget.orders, widget.readyAlerts);
    final available = floor.where((x) => x.category == FloorCategory.available).length;
    final occupied = floor.where((x) => x.category == FloorCategory.occupied).length;
    final ready = floor.where((x) => x.category == FloorCategory.ready).length;
    final other = floor.where((x) => x.category == FloorCategory.other).length;
    final query = _query.trim().toLowerCase();
    final visible = floor.where((table) =>
      (_filter == FloorCategory.all || _filter == table.category) &&
      (query.isEmpty || table.name.toLowerCase().contains(query) ||
          table.area.toLowerCase().contains(query) ||
          (table.raw['code']?.toString().toLowerCase().contains(query) ?? false)))
        .toList(growable: false);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(children: [
          const Expanded(child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('Dining floor',
                  style: TextStyle(fontSize: 21, fontWeight: FontWeight.w900)),
              SizedBox(height: 3),
              Text('Choose a table to take an order',
                  style: TextStyle(color: RestaurantColors.textSecondary)),
            ],
          )),
          Text(floor.length.toString() + ' tables',
              style: const TextStyle(color: RestaurantColors.textMuted,
                  fontWeight: FontWeight.w700)),
        ]),
        const SizedBox(height: 14),
        Row(children: [
          _FloorStat('Available', available, Icons.event_seat_outlined,
              RestaurantColors.success),
          const SizedBox(width: 8),
          _FloorStat('Occupied', occupied, Icons.table_restaurant,
              RestaurantColors.brandHover),
          const SizedBox(width: 8),
          _FloorStat('Ready', ready, Icons.notifications_active_outlined,
              RestaurantColors.success),
        ]),
        const SizedBox(height: 14),
        TextField(
          key: const Key('floor-table-search'),
          onChanged: (value) => setState(() => _query = value),
          decoration: InputDecoration(
            isDense: true,
            prefixIcon: const Icon(Icons.search_rounded),
            hintText: 'Find table or dining area',
            contentPadding: const EdgeInsets.symmetric(vertical: 13),
            filled: true,
            fillColor: RestaurantColors.surface,
            enabledBorder: OutlineInputBorder(
              borderRadius: BorderRadius.circular(RestaurantRadii.button),
              borderSide: const BorderSide(color: RestaurantColors.border),
            ),
          ),
        ),
        const SizedBox(height: 10),
        SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: Row(children: [
            _chip(FloorCategory.all, 'All', floor.length),
            _chip(FloorCategory.available, 'Free', available),
            _chip(FloorCategory.occupied, 'Occupied', occupied),
            _chip(FloorCategory.ready, 'Ready', ready),
            _chip(FloorCategory.other, 'Other', other),
          ]),
        ),
        if (widget.offline) ...[
          const SizedBox(height: 10),
          const Row(children: [
            Icon(Icons.wifi_off_rounded, size: 16,
                color: RestaurantColors.warning),
            SizedBox(width: 6),
            Expanded(child: Text(
              'Offline: table states are cached until desktop sync succeeds.',
              style: TextStyle(fontSize: 12,
                  color: RestaurantColors.warning,
                  fontWeight: FontWeight.w600),
            )),
          ]),
        ],
        const SizedBox(height: 12),
        if (visible.isEmpty)
          Container(
            width: double.infinity,
            padding: const EdgeInsets.symmetric(vertical: 34, horizontal: 18),
            decoration: BoxDecoration(
              color: RestaurantColors.surface,
              borderRadius: BorderRadius.circular(RestaurantRadii.card),
              border: Border.all(color: RestaurantColors.border),
            ),
            child: const Column(children: [
              Icon(Icons.search_off_rounded, size: 30,
                  color: RestaurantColors.textMuted),
              SizedBox(height: 8),
              Text('No tables match this search'),
            ]),
          )
        else
          LayoutBuilder(builder: (context, constraints) {
            final columns = constraints.maxWidth >= 840
                ? 4 : constraints.maxWidth >= 570 ? 3 : 2;
            return GridView.builder(
              key: const Key('floor-table-grid'),
              physics: const NeverScrollableScrollPhysics(),
              shrinkWrap: true,
              itemCount: visible.length,
              gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
                crossAxisCount: columns,
                mainAxisExtent: 155,
                mainAxisSpacing: 10,
                crossAxisSpacing: 10,
              ),
              itemBuilder: (context, index) {
                final table = visible[index];
                return _FloorTableCard(
                  key: Key('floor-table-' + table.id),
                  table: table,
                  offline: widget.offline,
                  onTap: () => widget.onTableTap(table.raw),
                );
              },
            );
          }),
      ],
    );
  }

  Widget _chip(FloorCategory category, String label, int count) {
    final selected = _filter == category;
    return Padding(
      padding: const EdgeInsetsDirectional.only(end: 7),
      child: ChoiceChip(
        key: Key('floor-filter-' + category.name),
        selected: selected,
        label: Text(label + ' ' + count.toString()),
        onSelected: (_) => setState(() => _filter = category),
        selectedColor: RestaurantColors.brandSoft,
        labelStyle: TextStyle(
          fontSize: 12,
          fontWeight: FontWeight.w700,
          color: selected
              ? RestaurantColors.brandHover
              : RestaurantColors.textSecondary,
        ),
      ),
    );
  }
}

class _FloorStat extends StatelessWidget {
  const _FloorStat(this.label, this.count, this.icon, this.color);

  final String label;
  final int count;
  final IconData icon;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Expanded(child: Container(
      padding: const EdgeInsets.fromLTRB(10, 11, 7, 11),
      decoration: BoxDecoration(
        color: RestaurantColors.surface,
        borderRadius: BorderRadius.circular(RestaurantRadii.button),
        border: Border.all(color: RestaurantColors.border),
      ),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Icon(icon, size: 18, color: color),
        const SizedBox(height: 7),
        Text(count.toString(),
            style: const TextStyle(fontSize: 21, fontWeight: FontWeight.w900)),
        Text(label, maxLines: 1, overflow: TextOverflow.ellipsis,
            style: const TextStyle(fontSize: 11, fontWeight: FontWeight.w600,
                color: RestaurantColors.textMuted)),
      ]),
    ));
  }
}

class _FloorTableCard extends StatelessWidget {
  const _FloorTableCard({required this.table, required this.offline,
      required this.onTap, super.key});

  final FloorTable table;
  final bool offline;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final color = switch (table.category) {
      FloorCategory.available || FloorCategory.ready => RestaurantColors.success,
      FloorCategory.occupied => RestaurantColors.brandHover,
      _ => RestaurantColors.textMuted,
    };
    final background = switch (table.category) {
      FloorCategory.available || FloorCategory.ready => RestaurantColors.successSoft,
      FloorCategory.occupied => RestaurantColors.brandSoft,
      _ => RestaurantColors.appBackground,
    };
    return Semantics(
      button: true,
      label: table.name + ', ' + table.label + (offline ? ', cached' : ''),
      child: Material(
        color: RestaurantColors.surface,
        borderRadius: BorderRadius.circular(RestaurantRadii.card),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          child: Container(
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(RestaurantRadii.card),
              border: Border.all(color: RestaurantColors.border),
            ),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Row(children: [
                Icon(table.category == FloorCategory.available
                    ? Icons.event_seat_outlined : Icons.table_restaurant,
                    size: 23, color: color),
                const Spacer(),
                if (table.orderNumber != null)
                  Text(table.orderNumber!,
                      style: const TextStyle(fontSize: 12,
                          fontWeight: FontWeight.w800)),
              ]),
              const Spacer(),
              Text(table.name, maxLines: 1, overflow: TextOverflow.ellipsis,
                  style: const TextStyle(fontSize: 15,
                      fontWeight: FontWeight.w800,
                      color: RestaurantColors.textPrimary)),
              if (table.area.isNotEmpty)
                Text(table.area, maxLines: 1, overflow: TextOverflow.ellipsis,
                    style: const TextStyle(fontSize: 11,
                        color: RestaurantColors.textMuted)),
              const SizedBox(height: 9),
              Container(
                padding: const EdgeInsets.symmetric(
                    horizontal: 8, vertical: 4),
                decoration: BoxDecoration(
                  color: background,
                  borderRadius: BorderRadius.circular(20),
                ),
                child: Text(
                  (offline ? 'Cached · ' : '') + table.label,
                  maxLines: 1, overflow: TextOverflow.ellipsis,
                  style: TextStyle(color: color,
                      fontWeight: FontWeight.w700, fontSize: 11),
                ),
              ),
            ]),
          ),
        ),
      ),
    );
  }
}
