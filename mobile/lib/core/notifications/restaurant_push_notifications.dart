import 'dart:async';
import 'dart:io';

import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';

import '../api/mobile_api_client.dart';
import '../models/session_credentials.dart';

/// Firebase application identifiers are public client config, not service
/// credentials. Missing values leave FCM disabled; LAN/in-app reminders work.
class RestaurantPushConfiguration {
  const RestaurantPushConfiguration._();

  static const apiKey = String.fromEnvironment('RESTAURANT_FIREBASE_API_KEY');
  static const appIdAndroid =
      String.fromEnvironment('RESTAURANT_FIREBASE_ANDROID_APP_ID');
  static const appIdIos =
      String.fromEnvironment('RESTAURANT_FIREBASE_IOS_APP_ID');
  static const senderId =
      String.fromEnvironment('RESTAURANT_FIREBASE_SENDER_ID');
  static const projectId =
      String.fromEnvironment('RESTAURANT_FIREBASE_PROJECT_ID');
  static const iosBundleId = 'af.businessos.businessosRestaurantWaiter';

  static bool get available =>
      apiKey.isNotEmpty &&
      senderId.isNotEmpty &&
      projectId.isNotEmpty &&
      (Platform.isIOS ? appIdIos : appIdAndroid).isNotEmpty;

  static FirebaseOptions get options => FirebaseOptions(
    apiKey: apiKey,
    appId: Platform.isIOS ? appIdIos : appIdAndroid,
    messagingSenderId: senderId,
    projectId: projectId,
    iosBundleId: Platform.isIOS ? iosBundleId : null,
  );
}

@pragma('vm:entry-point')
Future<void> restaurantPushBackgroundHandler(RemoteMessage message) async {
  // Background notifications are rendered by the OS from the FCM
  // notification payload. Do not open a SQLite DB or replay an order mutation.
  if (!RestaurantPushConfiguration.available) return;
  await Firebase.initializeApp(options: RestaurantPushConfiguration.options);
}

class RestaurantPushNotifications {
  RestaurantPushNotifications({
    required MobileApiClient api,
    required Future<SessionCredentials?> Function() currentSession,
  }) : _api = api,
       _currentSession = currentSession;

  final MobileApiClient _api;
  final Future<SessionCredentials?> Function() _currentSession;

  StreamSubscription<String>? _refreshSubscription;
  StreamSubscription<RemoteMessage>? _openSubscription;
  StreamSubscription<RemoteMessage>? _foregroundSubscription;
  String? _boundDeviceId;
  String? _lastReportedToken;
  final ValueNotifier<String?> tappedOrderId = ValueNotifier(null);
  bool _initialized = false;

  Future<void> initialize() async {
    if (_initialized || !RestaurantPushConfiguration.available) return;
    _initialized = true;
    try {
      await Firebase.initializeApp(
        options: RestaurantPushConfiguration.options,
      );
      FirebaseMessaging.onBackgroundMessage(
        restaurantPushBackgroundHandler,
      );
      await FirebaseMessaging.instance.setForegroundNotificationPresentationOptions(
        alert: false,
        badge: false,
        sound: false,
      );
      _openSubscription = FirebaseMessaging.onMessageOpenedApp.listen(
        _onMessageOpened,
      );
      _foregroundSubscription = FirebaseMessaging.onMessage.listen((message) {
        // Keep foreground behavior consistent with the already implemented
        // synced KOT banner rather than displaying duplicate push alerts.
      });
      _refreshSubscription = FirebaseMessaging.instance.onTokenRefresh.listen(
        (token) => unawaited(_reportToken(token)),
      );
      final initial = await FirebaseMessaging.instance.getInitialMessage();
      if (initial != null) _onMessageOpened(initial);
    } on Object {
      // Missing Firebase/APNs configuration cannot block waiter POS login.
      _initialized = false;
    }
  }

  void _onMessageOpened(RemoteMessage message) {
    if (message.data['type'] != 'restaurant.kot.ready') return;
    final orderId = message.data['order_id'];
    if (orderId != null && orderId.isNotEmpty) {
      tappedOrderId.value = orderId;
    }
  }

  void clearTappedOrder() => tappedOrderId.value = null;

  Future<void> registerAfterLogin() async {
    if (!_initialized) return;
    final session = await _currentSession();
    if (session == null) return;
    try {
      final permission = await FirebaseMessaging.instance.requestPermission(
        alert: true,
        badge: true,
        sound: true,
      );
      if (permission.authorizationStatus == AuthorizationStatus.denied) return;

      // Apple requires APNs registration before getToken; it can take time
      // after a fresh install, so retry after the next app resume/login.
      if (Platform.isIOS &&
          await FirebaseMessaging.instance.getAPNSToken() == null) return;
      final token = await FirebaseMessaging.instance.getToken();
      if (token != null && token.isNotEmpty) await _reportToken(token);
    } on Object {
      // Foreground LAN ordering stays available when Google services are
      // blocked, mobile data is off or the app is unsigned.
    }
  }

  Future<void> _reportToken(String token) async {
    final session = await _currentSession();
    final cloud = session?.cloudBaseUrl;
    if (session == null || cloud == null || cloud.isEmpty) return;
    if (_boundDeviceId == session.deviceId && _lastReportedToken == token) {
      return;
    }
    try {
      await _api.registerPushDevice(
        session.copyWith(baseUrl: cloud),
        token: token,
        platform: Platform.isIOS ? 'ios' : 'android',
      );
      _boundDeviceId = session.deviceId;
      _lastReportedToken = token;
    } on Object {
      // Registration retries on next login/resume or token rotation.
    }
  }

  Future<void> signOut() async {
    final session = await _currentSession();
    final cloud = session?.cloudBaseUrl;
    if (session != null && cloud != null && cloud.isNotEmpty) {
      try {
        await _api.unregisterPushDevice(session.copyWith(baseUrl: cloud));
      } on Object {
        // deleteToken below revokes delivery even if the cloud is offline.
      }
    }
    if (_initialized) {
      try {
        await FirebaseMessaging.instance.deleteToken();
      } on Object {
        // Best effort; backend token registration is still device/user-scoped.
      }
    }
    _lastReportedToken = null;
    _boundDeviceId = null;
    clearTappedOrder();
  }

  Future<void> dispose() async {
    await _refreshSubscription?.cancel();
    await _openSubscription?.cancel();
    await _foregroundSubscription?.cancel();
    tappedOrderId.dispose();
  }
}
