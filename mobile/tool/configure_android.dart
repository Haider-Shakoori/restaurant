import 'dart:io';

void main() {
  final manifestOk = _configureManifest();
  final gradleOk = _configureGradle();
  final lanServiceOk = _configureLanServiceKotlin();

  if (!manifestOk || !gradleOk || !lanServiceOk) {
    exitCode = 1;
  }
}

bool _configureManifest() {
  final manifest = File('android/app/src/main/AndroidManifest.xml');

  if (!manifest.existsSync()) {
    stderr.writeln(
      'AndroidManifest.xml not found. Run flutter create --platforms=android first.',
    );
    return false;
  }

  var content = manifest.readAsStringSync();

  final permissions = <String>[
    'android.permission.INTERNET',
    'android.permission.CAMERA',
    'android.permission.POST_NOTIFICATIONS',
    // The ready-for-pickup keeper runs as a foreground service so LAN
    // notifications survive the screen turning off; a partial wake lock keeps
    // the long-poll request cycling while the CPU would otherwise sleep.
    'android.permission.FOREGROUND_SERVICE',
    'android.permission.FOREGROUND_SERVICE_DATA_SYNC',
    'android.permission.WAKE_LOCK',
  ];

  for (final permission in permissions) {
    if (content.contains(permission)) {
      continue;
    }

    final close = content.indexOf('>');
    if (close >= 0) {
      content = content.substring(0, close + 1) +
          '\n    <uses-permission android:name="$permission"/>' +
          content.substring(close + 1);
    }
  }

  if (!content.contains('android:usesCleartextTraffic=')) {
    content = content.replaceFirst(
      '<application',
      '<application android:usesCleartextTraffic="true"',
    );
  }

  // Declare the foreground service that keeps LAN ready-for-pickup alerts
  // arriving while the app is backgrounded. `dataSync` matches Android 14's
  // required foreground service type; `stopWithTask=false` lets it outlive a
  // swipe from Recents.
  if (!content.contains('android:name=".RestaurantLanService"')) {
    const service = '''        <service
            android:name=".RestaurantLanService"
            android:exported="false"
            android:foregroundServiceType="dataSync"
            android:stopWithTask="false" />
''';
    final applicationClose = content.lastIndexOf('</application>');
    if (applicationClose >= 0) {
      content = content.substring(0, applicationClose) +
          service +
          content.substring(applicationClose);
    } else {
      stderr.writeln('</application> not found in AndroidManifest.xml.');
      return false;
    }
  }

  manifest.writeAsStringSync(content);
  return true;
}

/// Enables core library desugaring, which `flutter_local_notifications`
/// requires for its Android notifications on older API levels. Flutter 3.47
/// generates a Kotlin DSL (`build.gradle.kts`) app module, but this also keeps
/// the Groovy variant working.
bool _configureGradle() {
  final kts = File('android/app/build.gradle.kts');
  final groovy = File('android/app/build.gradle');
  final file = kts.existsSync()
      ? kts
      : groovy.existsSync()
          ? groovy
          : null;

  if (file == null) {
    stderr.writeln(
      'Android app Gradle file not found. Run flutter create --platforms=android first.',
    );
    return false;
  }

  final isKts = file.path.endsWith('.kts');
  var content = file.readAsStringSync();
  final original = content;

  // 1. Turn desugaring on inside the existing compileOptions block.
  final flag = isKts
      ? 'isCoreLibraryDesugaringEnabled = true'
      : 'coreLibraryDesugaringEnabled true';
  if (!content.contains('isCoreLibraryDesugaringEnabled') &&
      !content.contains('coreLibraryDesugaringEnabled')) {
    final marker = RegExp(r'compileOptions\s*\{');
    final match = marker.firstMatch(content);
    if (match != null) {
      content = content.replaceRange(
        match.end,
        match.end,
        '\n        $flag',
      );
    } else {
      stderr.writeln('compileOptions block not found in ${file.path}.');
      return false;
    }
  }

  // 2. Add the desugaring library to the dependency graph.
  if (!content.contains('desugar_jdk_libs')) {
    final dependency = isKts
        ? '    coreLibraryDesugaring("com.android.tools:desugar_jdk_libs:2.1.4")'
        : "    coreLibraryDesugaring 'com.android.tools:desugar_jdk_libs:2.1.4'";
    content = '${content.trimRight()}\n\ndependencies {\n$dependency\n}\n';
  }

  if (content != original) {
    file.writeAsStringSync(content);
  }
  return true;
}

/// Writes the two Kotlin sources that make up the Android LAN push keeper: a
/// foreground service plus a MethodChannel handler registered on MainActivity.
/// Flutter generates the `android/` folder in CI, so these sources are produced
/// here rather than committed. `MainActivity.kt` is replaced wholesale, which is
/// safe because `flutter create` only ever generates a trivial subclass.
bool _configureLanServiceKotlin() {
  final kotlinRoot = Directory('android/app/src/main/kotlin');
  if (!kotlinRoot.existsSync()) {
    stderr.writeln(
      'Kotlin source directory not found. Run flutter create --platforms=android first.',
    );
    return false;
  }

  File? mainActivity;
  for (final entity in kotlinRoot.listSync(recursive: true)) {
    if (entity is File && entity.path.endsWith('MainActivity.kt')) {
      mainActivity = entity;
      break;
    }
  }

  if (mainActivity == null) {
    stderr.writeln(
      'MainActivity.kt not found; cannot add the LAN push service.',
    );
    return false;
  }

  final source = mainActivity.readAsStringSync();
  final package = RegExp(
    r'package\s+([A-Za-z0-9_.]+)',
  ).firstMatch(source)?.group(1);
  if (package == null || package.isEmpty) {
    stderr.writeln('MainActivity.kt has no package declaration.');
    return false;
  }

  File(
    '${mainActivity.parent.path}/RestaurantLanService.kt',
  ).writeAsStringSync(_lanServiceSource(package));
  mainActivity.writeAsStringSync(_mainActivitySource(package));
  return true;
}

String _mainActivitySource(String package) => '''
package $package

import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

class MainActivity : FlutterActivity() {
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "restaurant/lan_service")
            .setMethodCallHandler { call, result ->
                when (call.method) {
                    "start" -> {
                        RestaurantLanService.start(applicationContext)
                        result.success(null)
                    }
                    "stop" -> {
                        RestaurantLanService.stop(applicationContext)
                        result.success(null)
                    }
                    else -> result.notImplemented()
                }
            }
    }
}
''';

String _lanServiceSource(String package) => '''
package $package

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.os.PowerManager

/// Keeps the waiter app process alive on the restaurant LAN so the background
/// long-poll can raise ready-for-pickup notifications even when the phone
/// screen is off. It deliberately does no networking itself: the Dart isolate
/// keeps running while this foreground service holds the process up. There is
/// no cloud/FCM dependency, so it works with no internet at all.
class RestaurantLanService : Service() {
    private var wakeLock: PowerManager.WakeLock? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        startForegroundNotification()
        acquireWakeLock()
        return START_NOT_STICKY
    }

    override fun onDestroy() {
        releaseWakeLock()
        super.onDestroy()
    }

    private fun startForegroundNotification() {
        val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                CHANNEL_ID,
                "Kitchen connection",
                NotificationManager.IMPORTANCE_LOW,
            ).apply {
                description = "Keeps ready-for-pickup alerts working on the restaurant Wi-Fi."
                setShowBadge(false)
            }
            manager.createNotificationChannel(channel)
        }

        val builder = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            Notification.Builder(this, CHANNEL_ID)
        } else {
            @Suppress("DEPRECATION")
            Notification.Builder(this)
        }

        val icon = if (applicationInfo.icon != 0) {
            applicationInfo.icon
        } else {
            android.R.drawable.ic_dialog_info
        }

        val notification: Notification = builder
            .setContentTitle("Restaurant")
            .setContentText("Connected to the kitchen")
            .setSmallIcon(icon)
            .setOngoing(true)
            .setCategory(Notification.CATEGORY_SERVICE)
            .build()

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            startForeground(
                NOTIFICATION_ID,
                notification,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC,
            )
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
    }

    private fun acquireWakeLock() {
        if (wakeLock != null) return
        val power = getSystemService(Context.POWER_SERVICE) as PowerManager
        wakeLock = power.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, WAKE_LOCK_TAG).apply {
            setReferenceCounted(false)
            acquire()
        }
    }

    private fun releaseWakeLock() {
        wakeLock?.let { if (it.isHeld) it.release() }
        wakeLock = null
    }

    companion object {
        private const val CHANNEL_ID = "restaurant_lan_service"
        private const val NOTIFICATION_ID = 4701
        private const val WAKE_LOCK_TAG = "businessos:restaurant_lan"

        fun start(context: Context) {
            val intent = Intent(context, RestaurantLanService::class.java)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                context.startForegroundService(intent)
            } else {
                context.startService(intent)
            }
        }

        fun stop(context: Context) {
            context.stopService(Intent(context, RestaurantLanService::class.java))
        }
    }
}
''';
