import '../core/api/mobile_api_client.dart';
import '../core/connection/connection_resolver.dart';
import '../core/notifications/lan_push_service.dart';
import '../core/notifications/restaurant_local_notifications.dart';
import '../core/notifications/restaurant_push_notifications.dart';
import '../core/security/offline_lease_verifier.dart';
import '../core/security/secure_credential_store.dart';
import '../core/session/session_service.dart';
import '../data/local/local_database.dart';
import '../features/orders/offline_order_repository.dart';
import '../features/tables/ready_pickup_notifier.dart';
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
    required this.localNotifications,
    required this.readyNotifier,
    required this.lanPush,
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

  /// On-device ready-for-pickup reminders. Works over the restaurant LAN with
  /// no internet; backs up the in-app banner when the waiter is on another app.
  final RestaurantLocalNotifications localNotifications;

  /// Single source of truth for ready-for-pickup detection, shared by the
  /// visible table screen and the background LAN long-poll loop.
  final ReadyPickupNotifier readyNotifier;

  /// Android foreground service keeper. Keeps the process (and the sync loop)
  /// alive so LAN ready-for-pickup notifications survive the screen going off.
  final LanPushService lanPush;

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
    final pushNotifications = RestaurantPushNotifications(
      api: api,
      currentSession: credentials.readSession,
    );
    final localNotifications = RestaurantLocalNotifications();
    final readyNotifier = ReadyPickupNotifier(
      database: database,
      credentials: credentials,
      notifications: localNotifications,
    );
    final syncCoordinator = SyncCoordinator(
      engine: syncEngine,
      onChanges: () => readyNotifier.refresh(),
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
      localNotifications: localNotifications,
      readyNotifier: readyNotifier,
      lanPush: LanPushService(),
    );
  }
}
