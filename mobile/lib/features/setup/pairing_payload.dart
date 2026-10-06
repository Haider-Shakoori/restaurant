import 'dart:convert';

import '../../core/api/api_exception.dart';

class RestaurantPairingPayload {
  const RestaurantPairingPayload({
    required this.localUrl,
    required this.cloudUrl,
    required this.pairingToken,
    this.expiresAt,
  });

  final String? localUrl;
  final String cloudUrl;
  final String pairingToken;
  final DateTime? expiresAt;

  static RestaurantPairingPayload parse(String raw) {
    try {
      final decoded = jsonDecode(raw);

      if (decoded is! Map<String, dynamic>) {
        throw const ApiException(
          code: 'invalid_pairing_qr',
          message: 'This QR code is not a BusinessOS Restaurant pairing code.',
        );
      }

      if (decoded['type'] != 'businessos.restaurant.pair' ||
          decoded['version'] != 1) {
        throw const ApiException(
          code: 'invalid_pairing_qr',
          message: 'This QR code is not supported by this BusinessOS Restaurant app.',
        );
      }

      final cloudUrl = decoded['cloud_url']?.toString().trim() ?? '';
      final localUrl = decoded['local_url']?.toString().trim();
      final token = decoded['pairing_token']?.toString().trim() ?? '';

      if (cloudUrl.isEmpty || token.isEmpty) {
        throw const ApiException(
          code: 'invalid_pairing_qr',
          message: 'The pairing QR code is missing required connection details.',
        );
      }

      final expiresAt = DateTime.tryParse(
        decoded['expires_at']?.toString() ?? '',
      );

      if (expiresAt != null && expiresAt.isBefore(DateTime.now().toUtc())) {
        throw const ApiException(
          code: 'pairing_qr_expired',
          message: 'This pairing QR code has expired. Generate a new one on the desktop app.',
        );
      }

      return RestaurantPairingPayload(
        localUrl: localUrl == null || localUrl.isEmpty ? null : localUrl,
        cloudUrl: cloudUrl,
        pairingToken: token,
        expiresAt: expiresAt,
      );
    } on ApiException {
      rethrow;
    } on Object {
      throw const ApiException(
        code: 'invalid_pairing_qr',
        message: 'This QR code could not be read as a BusinessOS Restaurant pairing code.',
      );
    }
  }
}
