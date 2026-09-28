import 'dart:convert';

import 'package:businessos_restaurant_waiter/core/security/offline_lease_verifier.dart';
import 'package:cryptography/cryptography.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('verifies canonical Ed25519 lease and rejects expired lease', () async {
    final algorithm = Ed25519();
    final keyPair = await algorithm.newKeyPair();
    final publicKey = await keyPair.extractPublicKey();
    final now = DateTime.utc(2026, 9, 28, 5);

    final payload = <String, Object?>{
      'schema_version': 1,
      'lease_id': 'LEASE-001',
      'key_id': 'test-key',
      'tenant_id': 'restaurant-a',
      'device_id': 'DEVICE-001',
      'features': <String, Object?>{
        'offline': true,
        'max_devices': '5',
      },
      'issued_at': now.subtract(const Duration(hours: 1)).toIso8601String(),
      'offline_valid_until': now.add(const Duration(days: 2)).toIso8601String(),
      'subscription_ends_at': now.add(const Duration(days: 10)).toIso8601String(),
    };

    final canonical = jsonEncode(_canonicalize(payload));
    final signature = await algorithm.sign(
      utf8.encode(canonical),
      keyPair: keyPair,
    );

    final signedLease = <String, Object?>{
      'payload': payload,
      'signature': base64UrlEncode(signature.bytes).replaceAll('=', ''),
      'algorithm': 'Ed25519',
      'key_id': 'test-key',
    };
    final publicKeyEncoded = base64UrlEncode(publicKey.bytes).replaceAll('=', '');
    final verifier = OfflineLeaseVerifier();

    final valid = await verifier.verify(
      signedLease: signedLease,
      publicKey: publicKeyEncoded,
      expectedDeviceId: 'DEVICE-001',
      expectedTenantId: 'restaurant-a',
      now: now,
    );

    expect(valid.valid, isTrue);
    expect(valid.reason, 'valid');

    final expired = await verifier.verify(
      signedLease: signedLease,
      publicKey: publicKeyEncoded,
      expectedDeviceId: 'DEVICE-001',
      now: now.add(const Duration(days: 3)),
    );

    expect(expired.valid, isFalse);
    expect(expired.reason, 'expired');
  });
}

Object? _canonicalize(Object? value) {
  if (value is List<Object?>) {
    return value.map(_canonicalize).toList(growable: false);
  }

  if (value is Map<Object?, Object?>) {
    final keys = value.keys.map((key) => key.toString()).toList()..sort();
    return <String, Object?>{
      for (final key in keys) key: _canonicalize(value[key]),
    };
  }

  return value;
}
