import 'dart:convert';
import 'dart:typed_data';

import 'package:cryptography/cryptography.dart';

class LeaseVerificationResult {
  const LeaseVerificationResult({
    required this.valid,
    required this.reason,
    this.expiresAt,
  });

  final bool valid;
  final String reason;
  final DateTime? expiresAt;
}

abstract interface class LeaseValidator {
  Future<LeaseVerificationResult> verify({
    required Map<String, Object?> signedLease,
    required String publicKey,
    String? expectedDeviceId,
    String? expectedTenantId,
    DateTime? now,
  });
}

class OfflineLeaseVerifier implements LeaseValidator {
  OfflineLeaseVerifier({Ed25519? algorithm})
    : _algorithm = algorithm ?? Ed25519();

  final Ed25519 _algorithm;

  Future<LeaseVerificationResult> verify({
    required Map<String, Object?> signedLease,
    required String publicKey,
    String? expectedDeviceId,
    String? expectedTenantId,
    DateTime? now,
  }) async {
    try {
      final payload = Map<String, Object?>.from(
        signedLease['payload']! as Map<Object?, Object?>,
      );
      final signatureEncoded = signedLease['signature']! as String;
      final algorithmName = signedLease['algorithm']! as String;

      if (algorithmName != 'Ed25519') {
        return const LeaseVerificationResult(
          valid: false,
          reason: 'unsupported_algorithm',
        );
      }

      if (expectedDeviceId != null &&
          payload['device_id']?.toString() != expectedDeviceId) {
        return const LeaseVerificationResult(
          valid: false,
          reason: 'device_mismatch',
        );
      }

      if (expectedTenantId != null &&
          payload['tenant_id']?.toString() != expectedTenantId) {
        return const LeaseVerificationResult(
          valid: false,
          reason: 'tenant_mismatch',
        );
      }

      final expiresAt = DateTime.parse(
        payload['offline_valid_until']! as String,
      ).toUtc();
      final subscriptionEndsAt = DateTime.parse(
        payload['subscription_ends_at']! as String,
      ).toUtc();
      final effectiveNow = (now ?? DateTime.now()).toUtc();

      if (!expiresAt.isAfter(effectiveNow) ||
          !subscriptionEndsAt.isAfter(effectiveNow)) {
        return LeaseVerificationResult(
          valid: false,
          reason: 'expired',
          expiresAt: expiresAt,
        );
      }

      final publicKeyBytes = _decodeBase64Url(publicKey);
      final signatureBytes = _decodeBase64Url(signatureEncoded);
      final message = utf8.encode(jsonEncode(_canonicalize(payload)));
      final verified = await _algorithm.verify(
        message,
        signature: Signature(
          signatureBytes,
          publicKey: SimplePublicKey(
            publicKeyBytes,
            type: KeyPairType.ed25519,
          ),
        ),
      );

      return LeaseVerificationResult(
        valid: verified,
        reason: verified ? 'valid' : 'invalid_signature',
        expiresAt: expiresAt,
      );
    } on Object {
      return const LeaseVerificationResult(
        valid: false,
        reason: 'malformed_lease',
      );
    }
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

  Uint8List _decodeBase64Url(String value) {
    final normalized = base64Url.normalize(value);
    return Uint8List.fromList(base64Url.decode(normalized));
  }
}
