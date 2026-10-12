import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';

import '../../features/tables/ready_pickup_alerts.dart';

/// System-tray ready-for-pickup reminders for the waiter app.
///
/// The app already shows an in-app READY banner and plays a short sound as soon
/// as a LAN/cloud sync reveals prepared food. This layer mirrors the same event
/// as an operating-system notification (with sound) so a waiter who briefly
/// switched to another app still sees it in the notification tray. It is
/// produced entirely on the device: no internet round trip and no FCM needed.
///
/// Every call is best-effort. A device without notification permission, an
/// unsigned build or an unsupported platform keep working with the in-app
/// banner only.
class RestaurantLocalNotifications {
  RestaurantLocalNotifications();

  static const _channelId = 'restaurant_ready_pickup';
  static const _channelName = 'Ready for pickup';
  static const _channelDescription =
      'Alerts the waiter when the kitchen marks a dish ready for pickup.';

  final FlutterLocalNotificationsPlugin _plugin =
      FlutterLocalNotificationsPlugin();

  /// Local order id of the notification the waiter last tapped. The table home
  /// screen consumes it exactly like the FCM tap channel.
  final ValueNotifier<String?> tappedOrderId = ValueNotifier<String?>(null);

  bool _initialized = false;
  int _nextId = 0;

  bool get _supported => !kIsWeb && (Platform.isAndroid || Platform.isIOS);

  Future<void> initialize() async {
    if (_initialized || !_supported) return;
    _initialized = true;
    try {
      await _plugin.initialize(
        settings: const InitializationSettings(
          android: AndroidInitializationSettings('@mipmap/ic_launcher'),
          iOS: DarwinInitializationSettings(),
        ),
        onDidReceiveNotificationResponse: _onNotificationResponse,
      );

      final android = _plugin.resolvePlatformSpecificImplementation<
          AndroidFlutterLocalNotificationsPlugin>();
      if (android != null) {
        await android.createNotificationChannel(
          const AndroidNotificationChannel(
            _channelId,
            _channelName,
            description: _channelDescription,
            importance: Importance.max,
            playSound: true,
          ),
        );
        await android.requestNotificationsPermission();
      } else {
        await _plugin
            .resolvePlatformSpecificImplementation<
                IOSFlutterLocalNotificationsPlugin>()
            ?.requestPermissions(alert: true, badge: true, sound: true);
      }
    } on Object {
      // A locked-down or unsigned device must never block waiter ordering.
      _initialized = false;
    }
  }

  /// Mirrors the in-app ready banner as a system notification with sound.
  Future<void> showReadyPickup(List<ReadyPickupAlert> alerts) async {
    if (alerts.isEmpty || !_supported) return;
    await initialize();
    if (!_initialized) return;

    final references = alerts.map((alert) => alert.reference).toSet().join(', ');
    final items = alerts.fold<int>(0, (sum, alert) => sum + alert.itemCount);
    try {
      await _plugin.show(
        id: _nextId++,
        title: 'READY FOR PICKUP',
        body: '$items item(s) ready · $references',
        notificationDetails: NotificationDetails(
          android: AndroidNotificationDetails(
            _channelId,
            _channelName,
            channelDescription: _channelDescription,
            importance: Importance.max,
            priority: Priority.high,
            playSound: true,
            ticker: 'Ready for pickup',
          ),
          iOS: const DarwinNotificationDetails(presentSound: true),
        ),
        payload: alerts.first.orderId,
      );
    } on Object {
      // The in-app banner already surfaced the event.
    }
  }

  void _onNotificationResponse(NotificationResponse response) {
    final payload = response.payload;
    if (payload != null && payload.isNotEmpty) {
      tappedOrderId.value = payload;
    }
  }

  void clearTappedOrder() => tappedOrderId.value = null;
}
