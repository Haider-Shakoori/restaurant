import 'dart:io';

void main() {
  final manifestOk = _configureManifest();
  final gradleOk = _configureGradle();

  if (!manifestOk || !gradleOk) {
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
