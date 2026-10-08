import 'package:businessos_restaurant_waiter/core/connection/connection_mode.dart';
import 'package:businessos_restaurant_waiter/core/models/session_credentials.dart';
import 'package:businessos_restaurant_waiter/features/orders/menu_image_request.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  const session = SessionCredentials(
    baseUrl: 'https://restaurant.example.test',
    localBaseUrl: 'http://192.168.1.25:4821',
    accessToken: 'sensitive-access-token',
    deviceId: 'paired-tablet-1',
    deviceSecret: 'sensitive-device-secret',
    deviceUid: 'tablet-uid',
    publicKey: 'test-public-key',
    lease: <String, Object?>{},
    activeChannel: ConnectionChannel.local,
  );

  test('relative menu image resolves to local server with paired headers', () {
    final request = resolveMenuImageRequest('/menu-images/item-a.jpg', session);
    expect(request, isNotNull);
    expect(request!.url, 'http://192.168.1.25:4821/menu-images/item-a.jpg');
    expect(request.headers['Authorization'], 'Bearer sensitive-access-token');
    expect(request.headers['X-Device-Id'], 'paired-tablet-1');
    expect(request.headers['X-Device-Secret'], 'sensitive-device-secret');
  });

  test('cloud-hosted menu images receive no local credentials', () {
    final request = resolveMenuImageRequest(
      'https://restaurant.example.test/media/menu-items/item-a',
      session,
    );
    expect(request, isNotNull);
    expect(request!.headers, isEmpty);
  });

  test('third-party and invalid relative paths never receive credentials', () {
    final external = resolveMenuImageRequest(
      'https://images.example.org/menu-images/item-a.jpg',
      session,
    );
    expect(external, isNotNull);
    expect(external!.headers, isEmpty);
    expect(resolveMenuImageRequest('/unrelated/path', session), isNull);
    expect(resolveMenuImageRequest('/menu-images/../private.txt', session), isNull);
  });

  test('unpaired client cannot resolve a protected LAN image', () {
    expect(resolveMenuImageRequest('/menu-images/item-a.webp', null), isNull);
  });
}
