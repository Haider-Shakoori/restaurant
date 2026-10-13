import 'dart:async';
import 'dart:ui';

import 'package:flutter/material.dart';

import '../core/models/session_credentials.dart';
import '../features/setup/setup_screen.dart';
import '../features/tables/table_home_screen.dart';
import 'app_strings.dart';
import 'app_theme.dart';
import 'dependencies.dart';

class WaiterApp extends StatefulWidget {
  const WaiterApp({
    required this.dependencies,
    super.key,
  });

  final AppDependencies dependencies;

  @override
  State<WaiterApp> createState() => _WaiterAppState();
}

class _WaiterAppState extends State<WaiterApp> {
  late Future<SessionCredentials?> _sessionFuture;

  @override
  void initState() {
    super.initState();
    _sessionFuture = _loadSession();
  }

  Future<SessionCredentials?> _loadSession() async {
    final session = await widget.dependencies.credentials.readSession();

    if (session != null) {
      await widget.dependencies.pushNotifications.initialize();
      await widget.dependencies.localNotifications.initialize();
      unawaited(widget.dependencies.pushNotifications.registerAfterLogin());
      widget.dependencies.syncCoordinator.start();
      unawaited(widget.dependencies.syncCoordinator.syncNow());
      // Keep the process alive on Android so LAN ready-for-pickup alerts keep
      // arriving when the phone screen is off. Only meaningful when a local
      // (restaurant network) base URL is configured.
      if ((session.localBaseUrl ?? '').isNotEmpty) {
        unawaited(widget.dependencies.lanPush.start());
      }
    }

    return session;
  }

  void _reloadSession() {
    setState(() {
      _sessionFuture = _loadSession();
    });
  }

  @override
  void dispose() {
    unawaited(widget.dependencies.syncCoordinator.dispose());
    unawaited(widget.dependencies.pushNotifications.dispose());
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final platformLocale = PlatformDispatcher.instance.locale;
    final languageCode = switch (platformLocale.languageCode) {
      'fa' => 'fa',
      'ps' => 'ps',
      _ => 'en',
    };
    final locale = Locale(languageCode);
    final strings = AppStrings(locale);

    return MaterialApp(
      debugShowCheckedModeBanner: false,
      title: strings.appName,
      theme: buildRestaurantTheme(),
      builder: (context, child) {
        return Directionality(
          textDirection: strings.isRtl ? TextDirection.rtl : TextDirection.ltr,
          child: child ?? const SizedBox.shrink(),
        );
      },
      home: FutureBuilder<SessionCredentials?>(
        future: _sessionFuture,
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) {
            return const Scaffold(
              body: Center(child: CircularProgressIndicator()),
            );
          }

          if (snapshot.data == null) {
            return SetupScreen(
              dependencies: widget.dependencies,
              strings: strings,
              onConnected: _reloadSession,
            );
          }

          return TableHomeScreen(
            dependencies: widget.dependencies,
            strings: strings,
            onLoggedOut: _reloadSession,
          );
        },
      ),
    );
  }
}
