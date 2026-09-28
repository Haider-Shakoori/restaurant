import 'package:businessos_restaurant_waiter/core/api/api_exception.dart';
import 'package:businessos_restaurant_waiter/core/connection/connection_mode.dart';
import 'package:businessos_restaurant_waiter/core/connection/connection_resolver.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('local mode accepts private LAN IP and defaults to HTTP', () async {
    final probe = _FakeProbe(
      <String, ServerHealth>{
        'http://192.168.1.10': const ServerHealth(
          baseUrl: 'http://192.168.1.10',
          tenantId: 'restaurant-a',
          service: 'BusinessOS Restaurant Tenant',
        ),
      },
    );
    final resolver = ConnectionResolver(probe: probe);

    final target = await resolver.resolve(
      mode: ConnectionMode.local,
      localUrl: '192.168.1.10',
    );

    expect(target.channel, ConnectionChannel.local);
    expect(target.baseUrl, 'http://192.168.1.10');
    expect(target.tenantId, 'restaurant-a');
  });

  test('cloud mode forces HTTPS', () async {
    final resolver = ConnectionResolver(probe: _FakeProbe(const {}));

    await expectLater(
      resolver.resolve(
        mode: ConnectionMode.cloud,
        cloudUrl: 'http://restaurant.example.com',
      ),
      throwsA(
        isA<ApiException>().having(
          (error) => error.code,
          'code',
          'https_required',
        ),
      ),
    );
  });

  test('plain HTTP public address is rejected in local mode', () async {
    final resolver = ConnectionResolver(probe: _FakeProbe(const {}));

    await expectLater(
      resolver.resolve(
        mode: ConnectionMode.local,
        localUrl: 'http://8.8.8.8',
      ),
      throwsA(
        isA<ApiException>().having(
          (error) => error.code,
          'code',
          'insecure_public_server',
        ),
      ),
    );
  });

  test('automatic mode prefers local when local health succeeds', () async {
    final probe = _FakeProbe(
      <String, ServerHealth>{
        'http://192.168.1.10': const ServerHealth(
          baseUrl: 'http://192.168.1.10',
          tenantId: 'restaurant-a',
          service: 'BusinessOS Restaurant Tenant',
        ),
        'https://restaurant.example.com': const ServerHealth(
          baseUrl: 'https://restaurant.example.com',
          tenantId: 'restaurant-a',
          service: 'BusinessOS Restaurant Tenant',
        ),
      },
    );
    final resolver = ConnectionResolver(probe: probe);

    final target = await resolver.resolve(
      mode: ConnectionMode.automatic,
      localUrl: '192.168.1.10',
      cloudUrl: 'restaurant.example.com',
    );

    expect(target.channel, ConnectionChannel.local);
    expect(probe.calls, <String>['http://192.168.1.10']);
  });

  test('automatic mode falls back to cloud when local is unreachable', () async {
    final probe = _FakeProbe(
      <String, ServerHealth>{
        'https://restaurant.example.com': const ServerHealth(
          baseUrl: 'https://restaurant.example.com',
          tenantId: 'restaurant-a',
          service: 'BusinessOS Restaurant Tenant',
        ),
      },
    );
    final resolver = ConnectionResolver(probe: probe);

    final target = await resolver.resolve(
      mode: ConnectionMode.automatic,
      localUrl: '192.168.1.10',
      cloudUrl: 'restaurant.example.com',
    );

    expect(target.channel, ConnectionChannel.cloud);
    expect(
      probe.calls,
      <String>[
        'http://192.168.1.10',
        'https://restaurant.example.com',
      ],
    );
  });
}

class _FakeProbe implements ServerProbe {
  _FakeProbe(this.responses);

  final Map<String, ServerHealth> responses;
  final List<String> calls = <String>[];

  @override
  Future<ServerHealth> probe(String baseUrl) async {
    calls.add(baseUrl);
    final response = responses[baseUrl];

    if (response == null) {
      throw const ApiException(
        code: 'offline',
        message: 'Not reachable.',
      );
    }

    return response;
  }
}
