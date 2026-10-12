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

    final home = File('lib/features/tables/table_home_screen.dart')
        .readAsStringSync();
    expect(home, contains('showReadyPickup(freshAlerts)'));

    final app = File('lib/app/waiter_app.dart').readAsStringSync();
    expect(app, contains('localNotifications.initialize()'));

    final dependencies = File('lib/app/dependencies.dart').readAsStringSync();
    expect(dependencies, contains('RestaurantLocalNotifications'));

    // Android builds need desugaring for flutter_local_notifications.
    final configure =
        File('tool/configure_android.dart').readAsStringSync();
    expect(configure, contains('isCoreLibraryDesugaringEnabled'));
    expect(configure, contains('desugar_jdk_libs'));
  });
}
