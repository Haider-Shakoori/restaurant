import 'dart:io';

void main() {
  final manifest = File('android/app/src/main/AndroidManifest.xml');

  if (!manifest.existsSync()) {
    stderr.writeln(
      'AndroidManifest.xml not found. Run flutter create --platforms=android first.',
    );
    exitCode = 1;
    return;
  }

  var content = manifest.readAsStringSync();

  final permissions = <String>[
    'android.permission.INTERNET',
    'android.permission.CAMERA',
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
}
