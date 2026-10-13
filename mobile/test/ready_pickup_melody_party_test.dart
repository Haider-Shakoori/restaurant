import 'dart:typed_data';
import 'package:businessos_restaurant_waiter/features/tables/ready_pickup_chime.dart';
import 'package:businessos_restaurant_waiter/features/tables/ready_pickup_alerts.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('pickup chime is a locally generated, non-silent 4-note PCM wave', () {
    final sound = generateReadyPickupWave();
    final header = ByteData.sublistView(sound);
    expect(String.fromCharCodes(sound.sublist(0, 4)), 'RIFF');
    expect(String.fromCharCodes(sound.sublist(8, 12)), 'WAVE');
    expect(header.getUint16(20, Endian.little), 1);
    expect(header.getUint32(24, Endian.little), 22050);
    expect(sound.length, greaterThan(32000));
    expect(sound.skip(44).where((x) => x != 0).length, greaterThan(2000));
  });

  test('pickup alert includes order and separate-party label', () {
    final result = readyPickupAlerts([
      <String, Object?>{
        'local_order_id': 'order-b',
        'status': 'ready',
        'table_id': 't1',
        'service_type': 'dine_in',
        'service_reference': 'Party B',
        'display_number': 18,
      },
    ], [
      <String, Object?>{'id': 't1', 'name': 'Table 4'}
    ]);
    expect(result, hasLength(1));
    expect(result.single.reference, contains('Party B'));
    expect(result.single.reference, contains('Table 4'));
    expect(result.single.reference, contains('#18'));
    final seen = ReadyPickupTracker();
    expect(seen.newlyReady(result), hasLength(1));
    expect(seen.newlyReady(result), isEmpty);
  });
}
