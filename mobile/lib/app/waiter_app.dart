import 'dart:async';
import 'dart:ui';

import 'package:flutter/material.dart';

import '../core/models/session_credentials.dart';
import '../features/setup/setup_screen.dart';
import '../features/tables/table_home_screen.dart';
import 'app_strings.dart';
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
      unawaited(widget.dependencies.pushNotifications.registerAfterLogin());
      widget.dependencies.syncCoordinator.start();
      unawaited(widget.dependencies.syncCoordinator.syncNow());
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
      theme: ThemeData(
        useMaterial3: true,
        colorScheme: ColorScheme.fromSeed(
          seedColor: const Color(0xFFD7A84E),
          primary: const Color(0xFF171B20),
          onPrimary: Colors.white,
          secondary: const Color(0xFFEFC76D),
          onSecondary: const Color(0xFF171B20),
        ),
        scaffoldBackgroundColor: const Color(0xFFF3F5F7),
        appBarTheme: const AppBarTheme(
          backgroundColor: Color(0xFF171B20),
          foregroundColor: Colors.white,
          centerTitle: false,
          titleTextStyle: TextStyle(
            fontSize: 19,
            fontWeight: FontWeight.w800,
            color: Colors.white,
          ),
        ),
        filledButtonTheme: FilledButtonThemeData(
          style: FilledButton.styleFrom(
            backgroundColor: const Color(0xFF171B20),
            foregroundColor: Colors.white,
            disabledBackgroundColor: const Color(0xFFE2E5EA),
            disabledForegroundColor: const Color(0xFF6D7684),
            minimumSize: const Size(0, 46),
            padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(12),
            ),
            textStyle: const TextStyle(fontWeight: FontWeight.w800),
          ),
        ),
        outlinedButtonTheme: OutlinedButtonThemeData(
          style: OutlinedButton.styleFrom(
            foregroundColor: const Color(0xFF171B20),
            minimumSize: const Size(0, 44),
            side: const BorderSide(color: Color(0xFFD2D7DE)),
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(12),
            ),
          ),
        ),
        inputDecorationTheme: InputDecorationTheme(
          filled: true,
          fillColor: Colors.white,
          border: OutlineInputBorder(
            borderRadius: BorderRadius.circular(12),
            borderSide: const BorderSide(color: Color(0xFFD8DDE5)),
          ),
        ),
        cardTheme: CardThemeData(
          color: Colors.white,
          elevation: 2,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(16),
            side: const BorderSide(color: Color(0xFFE6E8EB)),
          ),
        ),
      ),
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
