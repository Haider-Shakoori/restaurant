/// Derives kitchen progress from each round's item-level status. KOT rounds
/// are retained as audit history after serving, not re-submitted.
class KotRoundProgress {
  const KotRoundProgress(this.label, {required this.isHistory});

  final String label;
  final bool isHistory;
}

KotRoundProgress kotRoundProgress(
  Map<String, Object?> round, {
  String? orderStatus,
}) {
  if (const <String>{'served', 'billed', 'closed', 'cancelled'}
      .contains(orderStatus)) {
    return const KotRoundProgress('Completed', isHistory: true);
  }

  final states = <String>[];
  final rawTickets = round['tickets'];
  if (rawTickets is List) {
    for (final raw in rawTickets) {
      if (raw is! Map) continue;
      final items = raw['items'];
      if (items is List && items.isNotEmpty) {
        for (final item in items) {
          if (item is Map) {
            states.add((item['status'] ?? item['state'] ?? 'queued')
                .toString().toLowerCase());
          }
        }
      } else {
        states.add((raw['status'] ?? 'queued').toString().toLowerCase());
      }
    }
  }

  if (states.isEmpty) {
    return const KotRoundProgress('Sent to kitchen', isHistory: false);
  }

  final terminal = const <String>{
    'served', 'completed', 'delivered', 'voided', 'cancelled'
  };
  if (states.every(terminal.contains)) {
    return const KotRoundProgress('Completed', isHistory: true);
  }
  final ready = states.where((s) => s == 'ready').length;
  if (ready == states.length) {
    return const KotRoundProgress('Ready for pickup', isHistory: false);
  }
  if (ready > 0) {
    return const KotRoundProgress('Partially ready', isHistory: false);
  }
  if (states.any((s) => s == 'preparing' || s == 'in_progress' ||
      s == 'cooking')) {
    return const KotRoundProgress('Preparing', isHistory: false);
  }
  return const KotRoundProgress('Queued', isHistory: false);
}
