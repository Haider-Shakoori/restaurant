import 'package:uuid/uuid.dart';

import '../../core/connection/connection_mode.dart';
import '../../core/security/offline_lease_verifier.dart';
import '../../core/security/secure_credential_store.dart';
import '../../data/local/local_database.dart';

class OfflineOperationDenied implements Exception {
  const OfflineOperationDenied(this.reason);

  final String reason;

  @override
  String toString() => 'OfflineOperationDenied($reason)';
}

class OfflineOrderRepository {
  OfflineOrderRepository({
    required LocalDatabase database,
    required CredentialStore credentials,
    required LeaseValidator leaseVerifier,
    Uuid? uuid,
  }) : _database = database,
       _credentials = credentials,
       _leaseVerifier = leaseVerifier,
       _uuid = uuid ?? const Uuid();

  final LocalDatabase _database;
  final CredentialStore _credentials;
  final LeaseValidator _leaseVerifier;
  final Uuid _uuid;

  Future<String> createOrder({
    required String tableId,
    required int guestCount,
    String? notes,
  }) async {
    await _ensureOfflineOperationAllowed();

    final clientOrderId = _uuid.v4();
    await _database.createDraftOrder(
      clientOrderId: clientOrderId,
      mutationId: _uuid.v4(),
      tableId: tableId,
      guestCount: guestCount,
      notes: notes,
    );

    return clientOrderId;
  }

  Future<void> addItem({
    required String localOrderId,
    required Map<String, Object?> menuItem,
    int quantity = 1,
    String? notes,
  }) async {
    await _ensureOfflineOperationAllowed();

    await _database.addDraftItem(
      localOrderId: localOrderId,
      clientLineId: _uuid.v4(),
      mutationId: _uuid.v4(),
      menuItem: menuItem,
      quantity: quantity,
      notes: notes,
    );
  }

  Future<void> submit(String localOrderId) async {
    await _ensureOfflineOperationAllowed();

    await _database.submitDraftOrder(
      localOrderId: localOrderId,
      mutationId: _uuid.v4(),
    );
  }

  Future<void> _ensureOfflineOperationAllowed() async {
    if (await _database.systemState('server_locked') == '1') {
      throw const OfflineOperationDenied('subscription_locked');
    }

    final session = await _credentials.readSession();

    if (session == null) {
      throw const OfflineOperationDenied('not_configured');
    }

    final verification = await _leaseVerifier.verify(
      signedLease: session.lease,
      publicKey: session.publicKey,
      expectedDeviceId: session.activeChannel == ConnectionChannel.local
          ? null
          : session.deviceId,
    );

    if (!verification.valid) {
      throw OfflineOperationDenied(verification.reason);
    }
  }
}
