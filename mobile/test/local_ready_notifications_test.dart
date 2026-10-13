import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

/// Guards the offline ready-for-pickup system notification wiring. These paths
/// must keep working without any cloud/FCM round trip: the waiter phone derives
/// READY state from the LAN sync snapshot, then mirrors it to the OS tray.
void main() {
  test('ready pickup mirrors to a local OS notification with sound', () {
    final service = File(
      'lib/core/notifications/restaurant_local_notifications.dart',
    ).readAsStringSync();
    expect(service, contains('restaurant_ready_pickup'));
    expect(service, contains('requestNotificationsPermission'));
    expect(service, contains('playSound: true'));
    expect(service, contains('presentSound: true'));
    expect(service, contains('Future<void> showReadyPickup'));

    // Detection + de-duplication lives in the shared notifier so the visible
    // screen and the background long-poll loop cannot double-alert a dish.
    final notifier = File(
      'lib/features/tables/ready_pickup_notifier.dart',
    ).readAsStringSync();
    expect(notifier, contains('showReadyPickup(freshAlerts)'));
    expect(notifier, contains('newlyReady'));

    final home = File(
      'lib/features/tables/table_home_screen.dart',
    ).readAsStringSync();
    expect(home, contains('readyNotifier.refresh()'));

    final app = File('lib/app/waiter_app.dart').readAsStringSync();
    expect(app, contains('localNotifications.initialize()'));

    final dependencies = File('lib/app/dependencies.dart').readAsStringSync();
    expect(dependencies, contains('RestaurantLocalNotifications'));
    expect(dependencies, contains('ReadyPickupNotifier'));

    // Android builds need desugaring for flutter_local_notifications.
    final configure = File(
      'tool/configure_android.dart',
    ).readAsStringSync();
    expect(configure, contains('isCoreLibraryDesugaringEnabled'));
    expect(configure, contains('desugar_jdk_libs'));
  });

  test('ready pickup keeps arriving over the LAN with the screen off', () {
    // The pull endpoint gains a bounded wait so the desktop can hold the
    // request open until a READY change appears.
    final client = File(
      'lib/core/api/mobile_api_client.dart',
    ).readAsStringSync();
    expect(client, contains("'wait'"));

    final engine = File('lib/sync/sync_engine.dart').readAsStringSync();
    expect(engine, contains('pullOnce'));
    expect(engine, contains('wait: longPoll ? wait : null'));

    final coordinator = File(
      'lib/sync/sync_coordinator.dart',
    ).readAsStringSync();
    expect(coordinator, contains('pullOnce(wait: _watchWait)'));

    final keeper = File(
      'lib/core/notifications/lan_push_service.dart',
    ).readAsStringSync();
    expect(keeper, contains('restaurant/lan_service'));

    final app = File('lib/app/waiter_app.dart').readAsStringSync();
    expect(app, contains('lanPush.start()'));

    // The generated Android project must declare the foreground service, its
    // data-sync type and the wake lock so Dart keeps polling with the screen
    // off. There is no cloud/FCM dependency in this path.
    final configure = File(
      'tool/configure_android.dart',
    ).readAsStringSync();
    expect(configure, contains('android.permission.FOREGROUND_SERVICE'));
    expect(configure, contains('android.permission.FOREGROUND_SERVICE_DATA_SYNC'));
    expect(configure, contains('android.permission.WAKE_LOCK'));
    expect(configure, contains('foregroundServiceType="dataSync"'));
    expect(configure, contains('class RestaurantLanService'));
    expect(configure, contains('startForegroundService'));
    expect(configure, contains('setMethodCallHandler'));
  });
}
