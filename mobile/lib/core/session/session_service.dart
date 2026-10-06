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
    required String email,
    required String password,
    String pairingCode = '',
    String deviceName = 'BusinessOS Waiter',
  }) async {
    final target = await _connectionResolver.resolve(
      mode: connectionMode,
      localUrl: localUrl,
      cloudUrl: cloudUrl,
    );
    final baseUrl = target.baseUrl;
    final deviceUid = await _credentials.deviceUid();

    if (target.channel == ConnectionChannel.local) {
      final paired = await _api.pairLocal(
        baseUrl: baseUrl,
        pairingCode: pairingCode,
        deviceUid: deviceUid,
        deviceName: deviceName,
      );
      final device = Map<String, Object?>.from(
        paired['device']! as Map<Object?, Object?>,
      );
      final deviceId = device['id']!.toString();
      final deviceSecret = paired['device_secret']!.toString();
      final publicKey = paired['public_key']!.toString();
      final lease = Map<String, Object?>.from(
        paired['lease']! as Map<Object?, Object?>,
      );

      final verified = await _leaseVerifier.verify(
        signedLease: lease,
        publicKey: publicKey,
        expectedTenantId: target.tenantId,
      );

      if (!verified.valid) {
        throw ApiException(
          code: 'invalid_local_host_lease',
          message:
              'The desktop restaurant entitlement could not be verified: ' +
              verified.reason,
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
        activeChannel: target.channel,
        localBaseUrl: target.localBaseUrl,
        cloudBaseUrl: target.cloudBaseUrl,
        tenantId: target.tenantId,
      );
      await _credentials.saveAccessToken(
        paired['access_token']!.toString(),
      );

      final session = await _credentials.readSession();

      if (session == null) {
        throw const ApiException(
          code: 'session_persist_failed',
          message: 'The paired local session could not be stored securely.',
        );
      }

      final bootstrap = await _api.syncBootstrap(session);
      await _database.applyBootstrap(bootstrap);
      return target;
    }
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
      expectedTenantId: target.tenantId,
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
      activeChannel: target.channel,
      localBaseUrl: target.localBaseUrl,
      cloudBaseUrl: target.cloudBaseUrl,
      tenantId: target.tenantId,
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

    return target;
  }

  Future<void> logoutLocal() async {
    await _credentials.clearSession();
  }
}
