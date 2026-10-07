import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:uuid/uuid.dart';

import '../connection/connection_mode.dart';
import '../models/session_credentials.dart';

abstract interface class CredentialStore {
  Future<SessionCredentials?> readSession();

  Future<String> deviceUid();

  Future<void> saveActivation({
    required String baseUrl,
    required String deviceId,
    required String deviceSecret,
    required String deviceUid,
    required String publicKey,
    required Map<String, Object?> lease,
    ConnectionMode connectionMode = ConnectionMode.cloud,
    ConnectionChannel activeChannel = ConnectionChannel.cloud,
    String? localBaseUrl,
    String? cloudBaseUrl,
    String? tenantId,
  });

  Future<void> saveAccessToken(String token);

  Future<void> saveLease(Map<String, Object?> lease);

  Future<void> saveActiveConnection({
    required String baseUrl,
    required ConnectionChannel activeChannel,
  });

  Future<void> clearSession();
}

class SecureCredentialStore implements CredentialStore {
  SecureCredentialStore({
    FlutterSecureStorage? storage,
    Uuid? uuid,
  }) : _storage = storage ?? const FlutterSecureStorage(),
       _uuid = uuid ?? const Uuid();

  static const _baseUrlKey = 'tenant_base_url';
  static const _tokenKey = 'access_token';
  static const _deviceIdKey = 'device_id';
  static const _deviceSecretKey = 'device_secret';
  static const _deviceUidKey = 'device_uid';
  static const _publicKeyKey = 'lease_public_key';
  static const _leaseKey = 'offline_lease';
  static const _connectionModeKey = 'connection_mode';
  static const _activeChannelKey = 'active_connection_channel';
  static const _localBaseUrlKey = 'local_base_url';
  static const _cloudBaseUrlKey = 'cloud_base_url';
  static const _tenantIdKey = 'connection_tenant_id';

  final FlutterSecureStorage _storage;
  final Uuid _uuid;

  @override
  Future<SessionCredentials?> readSession() async {
    final requiredValues = await Future.wait<String?>([
      _storage.read(key: _baseUrlKey),
      _storage.read(key: _tokenKey),
      _storage.read(key: _deviceIdKey),
      _storage.read(key: _deviceSecretKey),
      _storage.read(key: _deviceUidKey),
      _storage.read(key: _publicKeyKey),
      _storage.read(key: _leaseKey),
    ]);

    if (requiredValues.any((value) => value == null || value.isEmpty)) {
      return null;
    }

    final optionalValues = await Future.wait<String?>([
      _storage.read(key: _connectionModeKey),
      _storage.read(key: _activeChannelKey),
      _storage.read(key: _localBaseUrlKey),
      _storage.read(key: _cloudBaseUrlKey),
      _storage.read(key: _tenantIdKey),
    ]);

    final leaseValue =
        jsonDecode(requiredValues[6]!) as Map<String, dynamic>;

    return SessionCredentials(
      baseUrl: requiredValues[0]!,
      accessToken: requiredValues[1]!,
      deviceId: requiredValues[2]!,
      deviceSecret: requiredValues[3]!,
      deviceUid: requiredValues[4]!,
      publicKey: requiredValues[5]!,
      lease: Map<String, Object?>.from(leaseValue),
      connectionMode: ConnectionModeValue.parse(optionalValues[0]),
      activeChannel: ConnectionChannelValue.parse(optionalValues[1]),
      localBaseUrl: optionalValues[2],
      cloudBaseUrl: optionalValues[3],
      tenantId: optionalValues[4],
    );
  }

  @override
  Future<String> deviceUid() async {
    final existing = await _storage.read(key: _deviceUidKey);

    if (existing != null && existing.isNotEmpty) {
      return existing;
    }

    final created = _uuid.v4();
    await _storage.write(key: _deviceUidKey, value: created);

    return created;
  }

  @override
  Future<void> saveActivation({
    required String baseUrl,
    required String deviceId,
    required String deviceSecret,
    required String deviceUid,
    required String publicKey,
    required Map<String, Object?> lease,
    ConnectionMode connectionMode = ConnectionMode.cloud,
    ConnectionChannel activeChannel = ConnectionChannel.cloud,
    String? localBaseUrl,
    String? cloudBaseUrl,
    String? tenantId,
  }) async {
    await _storage.write(key: _baseUrlKey, value: baseUrl);
    await _storage.write(key: _deviceIdKey, value: deviceId);
    await _storage.write(key: _deviceSecretKey, value: deviceSecret);
    await _storage.write(key: _deviceUidKey, value: deviceUid);
    await _storage.write(key: _publicKeyKey, value: publicKey);
    await _storage.write(
      key: _connectionModeKey,
      value: connectionMode.storageValue,
    );
    await _storage.write(
      key: _activeChannelKey,
      value: activeChannel.storageValue,
    );
    await _writeOptional(_localBaseUrlKey, localBaseUrl);
    await _writeOptional(_cloudBaseUrlKey, cloudBaseUrl);
    await _writeOptional(_tenantIdKey, tenantId);
    await saveLease(lease);
  }

  @override
  Future<void> saveAccessToken(String token) {
    return _storage.write(key: _tokenKey, value: token);
  }

  @override
  Future<void> saveLease(Map<String, Object?> lease) {
    return _storage.write(key: _leaseKey, value: jsonEncode(lease));
  }

  @override
  Future<void> saveActiveConnection({
    required String baseUrl,
    required ConnectionChannel activeChannel,
  }) async {
    await _storage.write(key: _baseUrlKey, value: baseUrl);
    await _storage.write(
      key: _activeChannelKey,
      value: activeChannel.storageValue,
    );
  }

  @override
  Future<void> clearSession() async {
    for (final key in <String>[
      _baseUrlKey,
      _tokenKey,
      _deviceIdKey,
      _deviceSecretKey,
      _publicKeyKey,
      _leaseKey,
      _connectionModeKey,
      _activeChannelKey,
      _localBaseUrlKey,
      _cloudBaseUrlKey,
      _tenantIdKey,
    ]) {
      await _storage.delete(key: key);
    }
  }

  Future<void> _writeOptional(String key, String? value) async {
    if (value == null || value.isEmpty) {
      await _storage.delete(key: key);
      return;
    }

    await _storage.write(key: key, value: value);
  }
}
