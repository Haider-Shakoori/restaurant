import 'package:uuid/uuid.dart';

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
    String? tableId,
    required String branchId,
    String serviceType = 'dine_in',
    String? serviceReference,
    required int guestCount,
    String? notes,
  }) async {
    await _ensureOfflineOperationAllowed();

    final clientOrderId = _uuid.v4();
    await _database.createDraftOrder(
      clientOrderId: clientOrderId,
      mutationId: _uuid.v4(),
      tableId: tableId,
      branchId: branchId,
      serviceType: serviceType,
      serviceReference: serviceReference,
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
    int? seatNumber,
    int? courseNumber,
    String? courseName,
    bool holdForCourse = false,
    List<Map<String, Object?>> modifiers = const [],
    String? allergyInstructions,
    String? kitchenInstructions,
  }) async {
    await _ensureOfflineOperationAllowed();

    await _database.addDraftItem(
      localOrderId: localOrderId,
      clientLineId: _uuid.v4(),
      mutationId: _uuid.v4(),
      menuItem: menuItem,
      quantity: quantity,
      notes: notes,
      seatNumber: seatNumber,
      courseNumber: courseNumber,
      courseName: courseName,
      holdForCourse: holdForCourse,
      modifiers: modifiers,
      allergyInstructions: allergyInstructions,
      kitchenInstructions: kitchenInstructions,
    );
  }

  Future<void> fireCourse({
    required String localOrderId,
    required int courseNumber,
    String priority = 'normal',
  }) async {
    await _ensureOfflineOperationAllowed();

    await _database.fireCourse(
      localOrderId: localOrderId,
      courseNumber: courseNumber,
      mutationId: _uuid.v4(),
      priority: priority,
    );
  }

  Future<void> voidProduction({
    required String kitchenTicketItemId,
    required String reason,
  }) async {
    await _ensureOfflineOperationAllowed();

    await _database.enqueueKitchenItemOperation(
      mutationId: _uuid.v4(),
      operation: 'order.item.void',
      kitchenTicketItemId: kitchenTicketItemId,
      reason: reason,
    );
  }

  Future<void> refireProduction({
    required String kitchenTicketItemId,
    required String reason,
  }) async {
    await _ensureOfflineOperationAllowed();

    await _database.enqueueKitchenItemOperation(
      mutationId: _uuid.v4(),
      operation: 'order.item.refire',
      kitchenTicketItemId: kitchenTicketItemId,
      reason: reason,
    );
  }

  Future<void> recallProduction({
    required String kitchenTicketItemId,
    required String reason,
  }) async {
    await _ensureOfflineOperationAllowed();

    await _database.enqueueKitchenItemOperation(
      mutationId: _uuid.v4(),
      operation: 'order.item.recall',
      kitchenTicketItemId: kitchenTicketItemId,
      reason: reason,
    );
  }

  Future<void> transferTable({
    required String localOrderId,
    required String targetTableId,
  }) async {
    await _ensureOfflineOperationAllowed();

    await _database.transferOrderTable(
      localOrderId: localOrderId,
      targetTableId: targetTableId,
      mutationId: _uuid.v4(),
    );
  }

  Future<void> moveUnsentItem({
    required String sourceLocalOrderId,
    required String targetLocalOrderId,
    required String localLineId,
    required int quantity,
  }) async {
    await _ensureOfflineOperationAllowed();

    await _database.moveUnsentOrderItem(
      sourceLocalOrderId: sourceLocalOrderId,
      targetLocalOrderId: targetLocalOrderId,
      localLineId: localLineId,
      targetLocalLineId: _uuid.v4(),
      quantity: quantity,
      mutationId: _uuid.v4(),
    );
  }

  Future<void> mergeOrders({
    required String sourceLocalOrderId,
    required String targetLocalOrderId,
  }) async {
    await _ensureOfflineOperationAllowed();

    await _database.mergeOrders(
      sourceLocalOrderId: sourceLocalOrderId,
      targetLocalOrderId: targetLocalOrderId,
      mutationId: _uuid.v4(),
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
      expectedDeviceId: session.deviceId,
    );

    if (!verification.valid) {
      throw OfflineOperationDenied(verification.reason);
    }
  }
}
