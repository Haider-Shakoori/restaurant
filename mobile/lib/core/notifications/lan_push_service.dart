import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';

/// Keeps the waiter app process alive on the restaurant LAN (Android) so the
/// background long-poll can raise ready-for-pickup notifications even when the
/// screen is off and the app is not in front. The native foreground service
/// does no networking itself: it simply holds the process (and a partial wake
/// lock) so the existing Dart sync loop keeps running, then shows a low-key
/// ongoing "connected to the kitchen" notification.
///
/// iOS has no general background socket, so there is no equivalent there; the
/// app only raises notifications while it is running in the foreground.
///
/// Every call is best-effort: an unsigned, locked-down or non-Android device
/// keeps working with the in-app banner only.
class LanPushService {
  LanPushService({MethodChannel? channel})
    : _channel = channel ?? const MethodChannel('restaurant/lan_service');

  final MethodChannel _channel;

  bool get supported => !kIsWeb && Platform.isAndroid;

  /// Starts the foreground service. Safe to call repeatedly.
  Future<void> start() async {
    if (!supported) return;
    try {
      await _channel.invokeMethod<void>('start');
    } on MissingPluginException {
      // The native service is not bundled in this build; in-app banner remains.
    } on PlatformException {
      // A restricted device refused the foreground service.
    }
  }

  /// Stops the foreground service, e.g. on sign-out.
  Future<void> stop() async {
    if (!supported) return;
    try {
      await _channel.invokeMethod<void>('stop');
    } on Object {
      // Best effort; the service is also stopped by the OS with the process.
    }
  }
}
