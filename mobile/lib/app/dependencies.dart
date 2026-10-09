import '../core/api/mobile_api_client.dart';
import '../core/connection/connection_resolver.dart';
import '../core/notifications/restaurant_push_notifications.dart';
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
    required this.connectionResolver,
    required this.leaseVerifier,
    required this.session,
    required this.syncEngine,
    required this.syncCoordinator,
    required this.orders,
    required this.pushNotifications,
  });

  final LocalDatabase database;
  final CredentialStore credentials;
  final MobileApiClient api;
  final ConnectionResolver connectionResolver;
  final OfflineLeaseVerifier leaseVerifier;
  final SessionService session;
  final SyncEngine syncEngine;
  final SyncCoordinator syncCoordinator;
  final OfflineOrderRepository orders;
  final RestaurantPushNotifications pushNotifications;

  static Future<AppDependencies> create() async {
    final database = await LocalDatabase.open();
    final credentials = SecureCredentialStore();
    final api = MobileApiClient();
    final connectionResolver = ConnectionResolver(probe: api);
    final leaseVerifier = OfflineLeaseVerifier();
    final session = SessionService(
      api: api,
      connectionResolver: connectionResolver,
      credentials: credentials,
      leaseVerifier: leaseVerifier,
      database: database,
    );
    final syncEngine = SyncEngine(
      api: api,
      store: database,
      credentials: credentials,
      leaseVerifier: leaseVerifier,
      connectionResolver: connectionResolver,
    );
    final syncCoordinator = SyncCoordinator(engine: syncEngine);
    final pushNotifications = RestaurantPushNotifications(
      api: api,
      currentSession: credentials.readSession,
    );
    final orders = OfflineOrderRepository(
      database: database,
      credentials: credentials,
      leaseVerifier: leaseVerifier,
    );

    return AppDependencies._(
      database: database,
      credentials: credentials,
      api: api,
      connectionResolver: connectionResolver,
      leaseVerifier: leaseVerifier,
      session: session,
      syncEngine: syncEngine,
      syncCoordinator: syncCoordinator,
      orders: orders,
      pushNotifications: pushNotifications,
    );
  }
}
