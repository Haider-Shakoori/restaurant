import '../api/mobile_api_client.dart';
import 'connection_mode.dart';

class ServerHealth {
  const ServerHealth({
    required this.baseUrl,
    required this.tenantId,
    required this.service,
  });

  final String baseUrl;
  final String tenantId;
  final String service;
}

class ConnectionTarget {
  const ConnectionTarget({
    required this.mode,
    required this.channel,
    required this.baseUrl,
    required this.localBaseUrl,
    required this.cloudBaseUrl,
    required this.tenantId,
  });

  final ConnectionMode mode;
  final ConnectionChannel channel;
  final String baseUrl;
  final String? localBaseUrl;
  final String? cloudBaseUrl;
  final String tenantId;
}

abstract interface class ServerProbe {
  Future<ServerHealth> probe(String baseUrl);
}

class ConnectionResolver {
  ConnectionResolver({required ServerProbe probe}) : _probe = probe;

  final ServerProbe _probe;

  Future<ConnectionTarget> resolve({
    required ConnectionMode mode,
    String? localUrl,
    String? cloudUrl,
  }) async {
    final local = _optionalLocal(localUrl);
    final cloud = _optionalCloud(cloudUrl);

    switch (mode) {
      case ConnectionMode.local:
        if (local == null) {
          throw const ApiException(
            code: 'local_server_required',
            message: 'Enter the local restaurant server IP or hostname.',
          );
        }

        final health = await _requireReachable(local, ConnectionChannel.local);
        return ConnectionTarget(
          mode: mode,
          channel: ConnectionChannel.local,
          baseUrl: local,
          localBaseUrl: local,
          cloudBaseUrl: cloud,
          tenantId: health.tenantId,
        );

      case ConnectionMode.cloud:
        if (cloud == null) {
          throw const ApiException(
            code: 'cloud_server_required',
            message: 'Enter the cloud restaurant server address.',
          );
        }

        final health = await _requireReachable(cloud, ConnectionChannel.cloud);
        return ConnectionTarget(
          mode: mode,
          channel: ConnectionChannel.cloud,
          baseUrl: cloud,
          localBaseUrl: local,
          cloudBaseUrl: cloud,
          tenantId: health.tenantId,
        );

      case ConnectionMode.automatic:
        if (local == null && cloud == null) {
          throw const ApiException(
            code: 'server_required',
            message: 'Enter at least one local or cloud restaurant server.',
          );
        }

        if (local != null) {
          final localHealth = await _tryProbe(local);

          if (localHealth != null) {
            return ConnectionTarget(
              mode: mode,
              channel: ConnectionChannel.local,
              baseUrl: local,
              localBaseUrl: local,
              cloudBaseUrl: cloud,
              tenantId: localHealth.tenantId,
            );
          }
        }

        if (cloud != null) {
          final cloudHealth = await _tryProbe(cloud);

          if (cloudHealth != null) {
            return ConnectionTarget(
              mode: mode,
              channel: ConnectionChannel.cloud,
              baseUrl: cloud,
              localBaseUrl: local,
              cloudBaseUrl: cloud,
              tenantId: cloudHealth.tenantId,
            );
          }
        }

        throw const ApiException(
          code: 'server_unreachable',
          message: 'Neither the local nor cloud restaurant server is reachable.',
        );
    }
  }

  String normalizeLocal(String input) {
    var value = input.trim();

    if (value.isEmpty) {
      throw const ApiException(
        code: 'local_server_required',
        message: 'Enter the local restaurant server IP or hostname.',
      );
    }

    if (!value.contains('://')) {
      value = 'http://' + value;
    }

    final uri = Uri.tryParse(value);

    if (uri == null || uri.host.isEmpty || !uri.hasScheme) {
      throw const ApiException(
        code: 'invalid_local_server',
        message: 'The local server address is invalid.',
      );
    }

    if (uri.scheme != 'http' && uri.scheme != 'https') {
      throw const ApiException(
        code: 'invalid_local_scheme',
        message: 'Local server must use HTTP or HTTPS.',
      );
    }

    if (uri.scheme == 'http' && !_isPrivateOrLocalHost(uri.host)) {
      throw const ApiException(
        code: 'insecure_public_server',
        message: 'Plain HTTP is allowed only for private/local network servers.',
      );
    }

    return value.replaceFirst(RegExp(r'/+$'), '');
  }

  String normalizeCloud(String input) {
    var value = input.trim();

    if (value.isEmpty) {
      throw const ApiException(
        code: 'cloud_server_required',
        message: 'Enter the cloud restaurant server address.',
      );
    }

    if (!value.contains('://')) {
      value = 'https://' + value;
    }

    final uri = Uri.tryParse(value);

    if (uri == null || uri.host.isEmpty || !uri.hasScheme) {
      throw const ApiException(
        code: 'invalid_cloud_server',
        message: 'The cloud server address is invalid.',
      );
    }

    if (uri.scheme != 'https') {
      throw const ApiException(
        code: 'https_required',
        message: 'Cloud restaurant servers must use HTTPS.',
      );
    }

    return value.replaceFirst(RegExp(r'/+$'), '');
  }

  Future<ServerHealth> _requireReachable(
    String baseUrl,
    ConnectionChannel channel,
  ) async {
    final health = await _tryProbe(baseUrl);

    if (health == null) {
      throw ApiException(
        code: channel == ConnectionChannel.local
            ? 'local_server_unreachable'
            : 'cloud_server_unreachable',
        message: channel == ConnectionChannel.local
            ? 'The local restaurant server is not reachable on this network.'
            : 'The cloud restaurant server is not reachable.',
      );
    }

    return health;
  }

  Future<ServerHealth?> _tryProbe(String baseUrl) async {
    try {
      final health = await _probe.probe(baseUrl);

      if (!health.service.startsWith('BusinessOS Restaurant')) {
        return null;
      }

      if (health.tenantId.isEmpty) {
        return null;
      }

      return health;
    } on ApiException {
      return null;
    }
  }

  String? _optionalLocal(String? value) {
    final trimmed = value?.trim() ?? '';
    return trimmed.isEmpty ? null : normalizeLocal(trimmed);
  }

  String? _optionalCloud(String? value) {
    final trimmed = value?.trim() ?? '';
    return trimmed.isEmpty ? null : normalizeCloud(trimmed);
  }

  bool _isPrivateOrLocalHost(String host) {
    final normalized = host.toLowerCase();

    if (normalized == 'localhost' ||
        normalized == '::1' ||
        normalized == '10.0.2.2' ||
        normalized.endsWith('.local') ||
        normalized.endsWith('.test')) {
      return true;
    }

    final ipv4 = normalized.split('.');

    if (ipv4.length == 4) {
      final octets = ipv4.map(int.tryParse).toList();

      if (octets.every((value) => value != null)) {
        final first = octets[0]!;
        final second = octets[1]!;

        if (first == 10 || first == 127 || first == 169 && second == 254) {
          return true;
        }

        if (first == 192 && second == 168) {
          return true;
        }

        if (first == 172 && second >= 16 && second <= 31) {
          return true;
        }
      }
    }

    return normalized.startsWith('fc') ||
        normalized.startsWith('fd') ||
        normalized.startsWith('fe8') ||
        normalized.startsWith('fe9') ||
        normalized.startsWith('fea') ||
        normalized.startsWith('feb');
  }
}
