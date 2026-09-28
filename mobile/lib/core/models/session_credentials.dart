class SessionCredentials {
  const SessionCredentials({
    required this.baseUrl,
    required this.accessToken,
    required this.deviceId,
    required this.deviceSecret,
    required this.deviceUid,
    required this.publicKey,
    required this.lease,
  });

  final String baseUrl;
  final String accessToken;
  final String deviceId;
  final String deviceSecret;
  final String deviceUid;
  final String publicKey;
  final Map<String, Object?> lease;

  SessionCredentials copyWith({
    String? accessToken,
    String? publicKey,
    Map<String, Object?>? lease,
  }) {
    return SessionCredentials(
      baseUrl: baseUrl,
      accessToken: accessToken ?? this.accessToken,
      deviceId: deviceId,
      deviceSecret: deviceSecret,
      deviceUid: deviceUid,
      publicKey: publicKey ?? this.publicKey,
      lease: lease ?? this.lease,
    );
  }
}
