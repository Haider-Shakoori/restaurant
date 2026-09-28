import '../connection/connection_mode.dart';

class SessionCredentials {
  const SessionCredentials({
    required this.baseUrl,
    required this.accessToken,
    required this.deviceId,
    required this.deviceSecret,
    required this.deviceUid,
    required this.publicKey,
    required this.lease,
    this.connectionMode = ConnectionMode.cloud,
    this.activeChannel = ConnectionChannel.cloud,
    this.localBaseUrl,
    this.cloudBaseUrl,
    this.tenantId,
  });

  final String baseUrl;
  final String accessToken;
  final String deviceId;
  final String deviceSecret;
  final String deviceUid;
  final String publicKey;
  final Map<String, Object?> lease;
  final ConnectionMode connectionMode;
  final ConnectionChannel activeChannel;
  final String? localBaseUrl;
  final String? cloudBaseUrl;
  final String? tenantId;

  SessionCredentials copyWith({
    String? baseUrl,
    String? accessToken,
    String? publicKey,
    Map<String, Object?>? lease,
    ConnectionMode? connectionMode,
    ConnectionChannel? activeChannel,
    String? localBaseUrl,
    String? cloudBaseUrl,
    String? tenantId,
  }) {
    return SessionCredentials(
      baseUrl: baseUrl ?? this.baseUrl,
      accessToken: accessToken ?? this.accessToken,
      deviceId: deviceId,
      deviceSecret: deviceSecret,
      deviceUid: deviceUid,
      publicKey: publicKey ?? this.publicKey,
      lease: lease ?? this.lease,
      connectionMode: connectionMode ?? this.connectionMode,
      activeChannel: activeChannel ?? this.activeChannel,
      localBaseUrl: localBaseUrl ?? this.localBaseUrl,
      cloudBaseUrl: cloudBaseUrl ?? this.cloudBaseUrl,
      tenantId: tenantId ?? this.tenantId,
    );
  }
}
