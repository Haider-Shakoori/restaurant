import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:http/http.dart' as http;

import '../connection/connection_resolver.dart';
import 'api_exception.dart';
import '../models/session_credentials.dart';

export 'api_exception.dart';

abstract interface class SyncApi {
  Future<Map<String, Object?>> refreshLease(SessionCredentials credentials);

  Future<Map<String, Object?>> heartbeat(SessionCredentials credentials);

  Future<Map<String, Object?>> syncBootstrap(SessionCredentials credentials);

  Future<Map<String, Object?>> push(
    SessionCredentials credentials,
    List<Map<String, Object?>> mutations,
  );

  Future<Map<String, Object?>> pull(
    SessionCredentials credentials, {
    required int cursor,
    int limit,
  });
}

class MobileApiClient implements SyncApi, ServerProbe {
  MobileApiClient({
    http.Client? client,
    this.timeout = const Duration(seconds: 15),
  }) : _client = client ?? http.Client();

  final http.Client _client;
  final Duration timeout;

  @override
  Future<ServerHealth> probe(String baseUrl) async {
    final response = await _request(
      'GET',
      _uri(baseUrl, '/api/v1/health'),
      timeoutOverride: const Duration(seconds: 3),
    );

    return ServerHealth(
      baseUrl: baseUrl,
      tenantId: response['tenant_id']?.toString() ?? '',
      service: response['service']?.toString() ?? '',
    );
  }

  Future<Map<String, Object?>> publicKey(String baseUrl) {
    return _request(
      'GET',
      _uri(baseUrl, '/api/v1/license/public-key'),
    );
  }

  Future<Map<String, Object?>> activate({
    required String baseUrl,
    required String licenseKey,
    required String deviceUid,
    required String deviceName,
    required String appVersion,
  }) {
    return _request(
      'POST',
      _uri(baseUrl, '/api/v1/license/activate'),
      body: <String, Object?>{
        'license_key': licenseKey,
        'device_uid': deviceUid,
        'device_name': deviceName,
        'platform': Platform.isIOS ? 'ios' : 'android',
        'app_version': appVersion,
      },
    );
  }

  Future<Map<String, Object?>> redeemPairing({
    required String baseUrl,
    required String pairingToken,
    required String deviceUid,
    required String deviceName,
    required String appVersion,
  }) {
    return _request(
      'POST',
      _uri(baseUrl, '/api/v1/pairing-tokens/redeem'),
      body: <String, Object?>{
        'pairing_token': pairingToken,
        'device_uid': deviceUid,
        'device_name': deviceName,
        'platform': Platform.isIOS ? 'ios' : 'android',
        'app_version': appVersion,
      },
    );
  }

  Future<Map<String, Object?>> login({
    required String baseUrl,
    required String email,
    required String password,
    required String deviceName,
  }) {
    return _request(
      'POST',
      _uri(baseUrl, '/api/v1/auth/login'),
      body: <String, Object?>{
        'email': email,
        'password': password,
        'device_name': deviceName,
      },
    );
  }

  @override
  Future<Map<String, Object?>> heartbeat(
    SessionCredentials credentials,
  ) {
    return _request(
      'POST',
      _uri(credentials.baseUrl, '/api/v1/local/heartbeat'),
      headers: _authHeaders(credentials),
      body: const <String, Object?>{},
    ).then(_data);
  }

  @override
  Future<Map<String, Object?>> refreshLease(
    SessionCredentials credentials,
  ) {
    return _request(
      'POST',
      _uri(credentials.baseUrl, '/api/v1/license/lease'),
      headers: _deviceHeaders(credentials),
      body: const <String, Object?>{'app_version': '1.0.0'},
    );
  }

  @override
  Future<Map<String, Object?>> syncBootstrap(
    SessionCredentials credentials,
  ) {
    return _request(
      'GET',
      _uri(credentials.baseUrl, '/api/v1/sync/bootstrap'),
      headers: _authHeaders(credentials),
    ).then(_data);
  }

  @override
  Future<Map<String, Object?>> push(
    SessionCredentials credentials,
    List<Map<String, Object?>> mutations,
  ) {
    return _request(
      'POST',
      _uri(credentials.baseUrl, '/api/v1/sync/push'),
      headers: _authHeaders(credentials),
      body: <String, Object?>{
        'batch_id': DateTime.now().microsecondsSinceEpoch.toString(),
        'mutations': mutations,
      },
    ).then(_data);
  }

  @override
  Future<Map<String, Object?>> pull(
    SessionCredentials credentials, {
    required int cursor,
    int limit = 100,
  }) {
    final uri = _uri(credentials.baseUrl, '/api/v1/sync/pull').replace(
      queryParameters: <String, String>{
        'cursor': cursor.toString(),
        'limit': limit.toString(),
      },
    );

    return _request(
      'GET',
      uri,
      headers: _authHeaders(credentials),
    ).then(_data);
  }

  Map<String, String> _authHeaders(SessionCredentials credentials) {
    return <String, String>{
      ..._deviceHeaders(credentials),
      'Authorization': 'Bearer ' + credentials.accessToken,
    };
  }

  Map<String, String> _deviceHeaders(SessionCredentials credentials) {
    return <String, String>{
      'X-Device-Id': credentials.deviceId,
      'X-Device-Secret': credentials.deviceSecret,
      'X-App-Version': '1.0.0',
      'X-Terminal-Type': Platform.isIOS ? 'ios' : 'android',
      'X-Device-Name': 'BusinessOS Waiter',
    };
  }

  Future<Map<String, Object?>> _request(
    String method,
    Uri uri, {
    Map<String, String> headers = const <String, String>{},
    Map<String, Object?>? body,
    Duration? timeoutOverride,
  }) async {
    try {
      final request = http.Request(method, uri)
        ..headers.addAll(<String, String>{
          'Accept': 'application/json',
          'Content-Type': 'application/json',
          ...headers,
        });

      if (body != null) {
        request.body = jsonEncode(body);
      }

      final streamed = await _client
          .send(request)
          .timeout(timeoutOverride ?? timeout);
      final response = await http.Response.fromStream(streamed);
      final decoded = response.body.isEmpty
          ? <String, Object?>{}
          : Map<String, Object?>.from(
              jsonDecode(response.body) as Map<String, dynamic>,
            );

      if (response.statusCode >= 200 && response.statusCode < 300) {
        return decoded;
      }

      throw ApiException(
        code: _errorCode(response.statusCode, decoded),
        message: _errorMessage(decoded),
        statusCode: response.statusCode,
        payload: decoded,
      );
    } on ApiException {
      rethrow;
    } on TimeoutException catch (error) {
      throw ApiException(
        code: 'timeout',
        message: 'The server did not respond in time.',
        payload: error,
      );
    } on SocketException catch (error) {
      throw ApiException(
        code: 'offline',
        message: 'No usable network connection is available.',
        payload: error,
      );
    } on http.ClientException catch (error) {
      throw ApiException(
        code: 'network',
        message: 'The server could not be reached.',
        payload: error,
      );
    }
  }

  String _errorCode(int status, Map<String, Object?> payload) {
    if (status == 423) {
      return 'subscription_locked';
    }

    if (status == 401) {
      return 'unauthenticated';
    }

    if (status == 403) {
      return 'forbidden';
    }

    if (status == 422) {
      return 'validation';
    }

    return payload['code']?.toString() ?? 'http_$status';
  }

  String _errorMessage(Map<String, Object?> payload) {
    final message = payload['message'];

    if (message is String && message.isNotEmpty) {
      return message;
    }

    final errors = payload['errors'];

    if (errors is Map<Object?, Object?> && errors.isNotEmpty) {
      final first = errors.values.first;

      if (first is List<Object?> && first.isNotEmpty) {
        return first.first.toString();
      }
    }

    return 'The server rejected the request.';
  }

  Uri _uri(String baseUrl, String path) {
    final normalized = baseUrl.trim().replaceFirst(RegExp(r'/+$'), '');
    return Uri.parse('$normalized$path');
  }

  Map<String, Object?> _data(Map<String, Object?> response) {
    return Map<String, Object?>.from(
      response['data']! as Map<Object?, Object?>,
    );
  }
}
