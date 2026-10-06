import 'package:businessos_restaurant_waiter/core/api/api_exception.dart';
import 'package:businessos_restaurant_waiter/features/setup/pairing_payload.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('parses a valid BusinessOS Restaurant pairing payload', () {
    final payload = RestaurantPairingPayload.parse(
      '{"type":"businessos.restaurant.pair","version":1,'
      '"local_url":"http://192.168.1.15:8787",'
      '"cloud_url":"https://restaurant.businessos.af",'
      '"pairing_token":"abcdefghijklmnopqrstuvwxyz1234567890",'
      '"expires_at":"2099-01-01T00:00:00Z"}',
    );

    expect(payload.localUrl, 'http://192.168.1.15:8787');
    expect(payload.cloudUrl, 'https://restaurant.businessos.af');
    expect(payload.pairingToken, 'abcdefghijklmnopqrstuvwxyz1234567890');
  });

  test('rejects unrelated QR codes', () {
    expect(
      () => RestaurantPairingPayload.parse('https://businessos.af'),
      throwsA(
        isA<ApiException>().having(
          (error) => error.code,
          'code',
          'invalid_pairing_qr',
        ),
      ),
    );
  });

  test('rejects expired pairing codes', () {
    expect(
      () => RestaurantPairingPayload.parse(
        '{"type":"businessos.restaurant.pair","version":1,'
        '"cloud_url":"https://restaurant.businessos.af",'
        '"pairing_token":"abcdefghijklmnopqrstuvwxyz1234567890",'
        '"expires_at":"2020-01-01T00:00:00Z"}',
      ),
      throwsA(
        isA<ApiException>().having(
          (error) => error.code,
          'code',
          'pairing_qr_expired',
        ),
      ),
    );
  });
}
