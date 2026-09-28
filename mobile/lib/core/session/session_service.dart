import '../api/mobile_api_client.dart';
import '../security/offline_lease_verifier.dart';
import '../security/secure_credential_store.dart';
import '../../data/local/local_database.dart';

class SessionService {
  SessionService({
    required MobileApiClient api,
    required CredentialStore credentials,
    required OfflineLeaseVerifier leaseVerifier,
    required LocalDatabase database,
  }) : _api = api,
       _credentials = credentials,
       _leaseVerifier = leaseVerifier,
       _database = database;

  final MobileApiClient _api;
  final CredentialStore _credentials;
  final OfflineLeaseVerifier _leaseVerifier;
  final LocalDatabase _database;

  Future<void> activateAndLogin({
    required String tenantUrl,
    required String licenseKey,
    required String email,
    required String password,
    String deviceName = 'BusinessOS Waiter',
  }) async {
    final baseUrl = _normalizeBaseUrl(tenantUrl);
    final deviceUid = await _credentials.deviceUid();
    final keyResponse = await _api.publicKey(baseUrl);
    final publicKey = keyResponse['public_key']!.toString();

    final activation = await _api.activate(
      baseUrl: baseUrl,
      licenseKey: licenseKey.trim(),
      deviceUid: deviceUid,
      deviceName: deviceName,
      appVersion: '1.0.0',
    );
    final device = Map<String, Object?>.from(
      activation['device']! as Map<Object?, Object?>,
    );
    final deviceId = device['id']!.toString();
    final deviceSecret = activation['device_secret']!.toString();
    final lease = Map<String, Object?>.from(
      activation['lease']! as Map<Object?, Object?>,
    );

    final verified = await _leaseVerifier.verify(
      signedLease: lease,
      publicKey: publicKey,
      expectedDeviceId: deviceId,
    );

    if (!verified.valid) {
      throw ApiException(
        code: 'invalid_offline_lease',
        message: 'The activation lease could not be verified: ' + verified.reason,
      );
    }

    await _credentials.saveActivation(
      baseUrl: baseUrl,
      deviceId: deviceId,
      deviceSecret: deviceSecret,
      deviceUid: deviceUid,
      publicKey: publicKey,
      lease: lease,
    );

    final login = await _api.login(
      baseUrl: baseUrl,
      email: email.trim(),
      password: password,
      deviceName: deviceName,
    );
    await _credentials.saveAccessToken(login['access_token']!.toString());

    final session = await _credentials.readSession();

    if (session == null) {
      throw const ApiException(
        code: 'session_persist_failed',
        message: 'The activated session could not be stored securely.',
      );
    }

    final bootstrap = await _api.syncBootstrap(session);
    await _database.applyBootstrap(bootstrap);
  }

  Future<void> logoutLocal() async {
    await _credentials.clearSession();
  }

  String _normalizeBaseUrl(String input) {
    var value = input.trim();

    if (!value.contains('://')) {
      value = 'https://' + value;
    }

    final uri = Uri.parse(value);
    final localDev = uri.host == 'localhost' ||
        uri.host == '127.0.0.1' ||
        uri.host == '10.0.2.2' ||
        uri.host.endsWith('.test');

    if (uri.scheme != 'https' && !localDev) {
      throw const ApiException(
        code: 'https_required',
        message: 'Restaurant server URL must use HTTPS.',
      );
    }

    return value.replaceFirst(RegExp(r'/+$'), '');
  }
}
