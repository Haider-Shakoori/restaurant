import '../core/api/mobile_api_client.dart';
import '../core/security/offline_lease_verifier.dart';
import '../core/security/secure_credential_store.dart';
import '../core/session/session_service.dart';
import '../data/local/local_database.dart';
import '../features/orders/offline_order_repository.dart';
import '../sync/sync_coordinator.dart';
import '../sync/sync_engine.dart';

class AppDependencies {
  AppDependencies._({
    required this.database,
    required this.credentials,
    required this.api,
    required this.leaseVerifier,
    required this.session,
    required this.syncEngine,
    required this.syncCoordinator,
    required this.orders,
  });

  final LocalDatabase database;
  final CredentialStore credentials;
  final MobileApiClient api;
  final OfflineLeaseVerifier leaseVerifier;
  final SessionService session;
  final SyncEngine syncEngine;
  final SyncCoordinator syncCoordinator;
  final OfflineOrderRepository orders;

  static Future<AppDependencies> create() async {
    final database = await LocalDatabase.open();
    final credentials = SecureCredentialStore();
    final api = MobileApiClient();
    final leaseVerifier = OfflineLeaseVerifier();
    final session = SessionService(
      api: api,
      credentials: credentials,
      leaseVerifier: leaseVerifier,
      database: database,
    );
    final syncEngine = SyncEngine(
      api: api,
      store: database,
      credentials: credentials,
      leaseVerifier: leaseVerifier,
    );
    final syncCoordinator = SyncCoordinator(engine: syncEngine);
    final orders = OfflineOrderRepository(
      database: database,
      credentials: credentials,
      leaseVerifier: leaseVerifier,
    );

    return AppDependencies._(
      database: database,
      credentials: credentials,
      api: api,
      leaseVerifier: leaseVerifier,
      session: session,
      syncEngine: syncEngine,
      syncCoordinator: syncCoordinator,
      orders: orders,
    );
  }
}
