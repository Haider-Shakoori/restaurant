import 'dart:convert';

/// A visible ready-for-collection group per still-open waiter order.
/// The synchronized server/LAN kitchen state is authoritative.
class ReadyPickupAlert {
  const ReadyPickupAlert({
    required this.orderId,
    required this.reference,
    required this.itemCount,
    required this.keys,
  });

  final String orderId;
  final String reference;
  final int itemCount;
  final List<String> keys;
}

List<ReadyPickupAlert> readyPickupAlerts(
  List<Map<String, Object?>> orders,
  List<Map<String, Object?>> tables,
) {
  final tableNames = <String, String>{
    for (final table in tables)
      table['id'].toString():
          (table['name'] ?? table['code'] ?? 'Unknown table').toString(),
  };
  final ready = <ReadyPickupAlert>[];

  for (final order in orders) {
    final status = order['status']?.toString() ?? '';
    if (status == 'served' || status == 'billed' ||
        status == 'closed' || status == 'cancelled') {
      continue;
    }

    final id = order['local_order_id']?.toString();
    if (id == null || id.isEmpty) continue;

    final readyItems = <String>[];
    final rawTickets = order['kitchen_tickets_json']?.toString();
    if (rawTickets != null && rawTickets.isNotEmpty) {
      Object? decoded;
      try {
        decoded = jsonDecode(rawTickets);
      } on FormatException {
        decoded = null;
      }
      if (decoded is List) {
        for (final ticket in decoded) {
          if (ticket is! Map) continue;
          final ticketId = ticket['id']?.toString() ?? id;
          final lines = ticket['items'];
          if (lines is! List) continue;
          for (final line in lines) {
            if (line is! Map || (line['status'] ?? line['state']) != 'ready') {
              continue;
            }
            final lineId = line['id']?.toString();
            if (lineId != null && lineId.isNotEmpty) {
              readyItems.add('item:$lineId');
            } else {
              final fallback = line['order_item_id'] ??
                  line['source_order_item_id'] ?? line['item_name'];
              readyItems.add('ticket:$ticketId:${fallback ?? 'ready'}');
            }
          }
        }
      }
    }

    // Old LAN/cloud snapshots may omit item-level tickets.
    // Respect only the remotely established ready state.
    if (readyItems.isEmpty && status == 'ready') {
      readyItems.add('order:$id');
    }
    if (readyItems.isEmpty) continue;

    final tableId = order['table_id']?.toString();
    final service = order['service_type']?.toString() ?? 'dine_in';
    final ref = order['service_reference']?.toString();
    final label = service == 'dine_in'
        ? tableNames[tableId] ?? 'Dine-in order $id'
        : '${service.replaceAll('_', ' ').toUpperCase()}' +
            (ref == null || ref.isEmpty ? ' · $id' : ' · $ref');

    ready.add(ReadyPickupAlert(
      orderId: id,
      reference: label,
      itemCount: readyItems.length,
      keys: readyItems.toSet().toList(growable: false),
    ));
  }
  return ready;
}

/// De-duplicates alerts across sync refreshes and application relaunches.
class ReadyPickupTracker {
  ReadyPickupTracker({Iterable<String> seen = const []})
      : _seen = seen.toSet();

  final Set<String> _seen;

  List<ReadyPickupAlert> newlyReady(List<ReadyPickupAlert> active) {
    final result = active
        .where((alert) => alert.keys.any((key) => !_seen.contains(key)))
        .toList(growable: false);
    for (final alert in active) {
      _seen.addAll(alert.keys);
    }
    return result;
  }

  String encode() => jsonEncode(_seen.toList(growable: false));

  static ReadyPickupTracker decode(String? raw) {
    if (raw == null || raw.isEmpty) return ReadyPickupTracker();
    try {
      final keys = jsonDecode(raw);
      if (keys is List) {
        return ReadyPickupTracker(seen: keys.whereType<String>());
      }
    } on FormatException {
      // Damaged alert history must not block restaurant ordering.
    }
    return ReadyPickupTracker();
  }
}
