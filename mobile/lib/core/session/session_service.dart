import '../../data/local/local_database.dart';
import '../api/mobile_api_client.dart';
import '../connection/connection_mode.dart';
import '../connection/connection_resolver.dart';
import '../security/offline_lease_verifier.dart';
import '../security/secure_credential_store.dart';

class SessionService {
  SessionService({
    required MobileApiClient api,
    required ConnectionResolver connectionResolver,
    required CredentialStore credentials,
    required OfflineLeaseVerifier leaseVerifier,
    required LocalDatabase database,
  }) : _api = api,
       _connectionResolver = connectionResolver,
       _credentials = credentials,
       _leaseVerifier = leaseVerifier,
       _database = database;

  final MobileApiClient _api;
  final ConnectionResolver _connectionResolver;
  final CredentialStore _credentials;
  final OfflineLeaseVerifier _leaseVerifier;
  final LocalDatabase _database;

  Future<ConnectionTarget> activateAndLogin({
    required ConnectionMode connectionMode,
    String? localUrl,
    String? cloudUrl,
    required String licenseKey,
    String? pairingToken,
    required String email,
    required String password,
    String deviceName = 'BusinessOS Waiter',
  }) async {
    final target = await _connectionResolver.resolve(
      mode: connectionMode,
      localUrl: localUrl,
      cloudUrl: cloudUrl,
    );

    // First-time activation and sign-in are cloud-authoritative. Once the
    // signed device lease and user token exist, automatic mode can prefer
    // the Desktop LAN endpoint and fail back to cloud as connectivity changes.
    var activationTarget = target;
    if (connectionMode == ConnectionMode.automatic &&
        cloudUrl != null &&
        cloudUrl.trim().isNotEmpty) {
      activationTarget = await _connectionResolver.resolve(
        mode: ConnectionMode.cloud,
        localUrl: localUrl,
        cloudUrl: cloudUrl,
      );
    }

    final baseUrl = activationTarget.baseUrl;
    final deviceUid = await _credentials.deviceUid();
    final keyResponse = await _api.publicKey(baseUrl);
    final publicKey = keyResponse['public_key']!.toString();

    final normalizedPairingToken = pairingToken?.trim() ?? '';
    final activation = normalizedPairingToken.isNotEmpty
        ? await _api.redeemPairing(
            baseUrl: baseUrl,
            pairingToken: normalizedPairingToken,
            deviceUid: deviceUid,
            deviceName: deviceName,
            appVersion: '1.0.0',
          )
        : await _api.activate(
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
      expectedTenantId: activationTarget.tenantId,
    );

    if (!verified.valid) {
      throw ApiException(
        code: 'invalid_offline_lease',
        message:
            'The activation lease could not be verified: ' + verified.reason,
      );
    }

    await _credentials.saveActivation(
      baseUrl: baseUrl,
      deviceId: deviceId,
      deviceSecret: deviceSecret,
      deviceUid: deviceUid,
      publicKey: publicKey,
      lease: lease,
      connectionMode: target.mode,
      activeChannel: activationTarget.channel,
      localBaseUrl: target.localBaseUrl,
      cloudBaseUrl: target.cloudBaseUrl,
      tenantId: activationTarget.tenantId,
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

    return activationTarget;
  }

  Future<void> logoutLocal() async {
    await _credentials.clearSession();
  }
}
