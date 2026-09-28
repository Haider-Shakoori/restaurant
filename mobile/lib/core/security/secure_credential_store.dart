import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:uuid/uuid.dart';

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
  });

  Future<void> saveAccessToken(String token);

  Future<void> saveLease(Map<String, Object?> lease);

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

  final FlutterSecureStorage _storage;
  final Uuid _uuid;

  @override
  Future<SessionCredentials?> readSession() async {
    final values = await Future.wait<String?>([
      _storage.read(key: _baseUrlKey),
      _storage.read(key: _tokenKey),
      _storage.read(key: _deviceIdKey),
      _storage.read(key: _deviceSecretKey),
      _storage.read(key: _deviceUidKey),
      _storage.read(key: _publicKeyKey),
      _storage.read(key: _leaseKey),
    ]);

    if (values.any((value) => value == null || value.isEmpty)) {
      return null;
    }

    final leaseValue = jsonDecode(values[6]!) as Map<String, dynamic>;

    return SessionCredentials(
      baseUrl: values[0]!,
      accessToken: values[1]!,
      deviceId: values[2]!,
      deviceSecret: values[3]!,
      deviceUid: values[4]!,
      publicKey: values[5]!,
      lease: Map<String, Object?>.from(leaseValue),
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
  }) async {
    await _storage.write(key: _baseUrlKey, value: baseUrl);
    await _storage.write(key: _deviceIdKey, value: deviceId);
    await _storage.write(key: _deviceSecretKey, value: deviceSecret);
    await _storage.write(key: _deviceUidKey, value: deviceUid);
    await _storage.write(key: _publicKeyKey, value: publicKey);
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
  Future<void> clearSession() async {
    await _storage.delete(key: _baseUrlKey);
    await _storage.delete(key: _tokenKey);
    await _storage.delete(key: _deviceIdKey);
    await _storage.delete(key: _deviceSecretKey);
    await _storage.delete(key: _publicKeyKey);
    await _storage.delete(key: _leaseKey);
  }
}
