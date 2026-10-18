import 'package:businessos_restaurant_waiter/features/orders/kot_round_status.dart';
import 'package:flutter_test/flutter_test.dart';

Map<String, Object?> round(List<String> states) => <String, Object?>{
  'sequence': 2,
  'kot_number': 'KOT-0003',
  'tickets': <Object?>[
    <String, Object?>{
      'id': 'ticket-1',
      'items': <Object?>[
        for (final state in states)
          <String, Object?>{'status': state},
      ],
    },
  ],
};

void main() {
  test('unsent-empty tickets remain sent rather than disappearing', () {
    final p = kotRoundProgress(<String, Object?>{
      'sequence': 1,
      'tickets': <Object?>[],
    });
    expect(p.isHistory, false);
    expect(p.label, 'Sent to kitchen');
  });

  test('kitchen progress distinguishes queued, cooking, and partial ready', () {
    expect(kotRoundProgress(round(['queued'])).label, 'Queued');
    expect(kotRoundProgress(round(['preparing'])).label, 'Preparing');
    expect(kotRoundProgress(round(['ready','preparing'])).label,
        'Partially ready');
    expect(kotRoundProgress(round(['ready','ready'])).label,
        'Ready for pickup');
  });

  test('serving archives finished rounds without deleting their history', () {
    final served = kotRoundProgress(round(['ready','ready']),
        orderStatus: 'served');
    expect(served.isHistory, true);
    expect(served.label, 'Completed');
    final complete = kotRoundProgress(round(['served','voided']));
    expect(complete.isHistory, true);
    expect(complete.label, 'Completed');
  });

  test('ready items are not classed as completed before serving', () {
    final ready = kotRoundProgress(round(['ready']));
    expect(ready.isHistory, false);
  });
}
