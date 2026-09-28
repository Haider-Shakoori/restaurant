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

  if (!content.contains('android.permission.INTERNET')) {
    content = content.replaceFirst(
      '<manifest',
      '<manifest',
    );
    final close = content.indexOf('>');

    if (close >= 0) {
      content = content.substring(0, close + 1) +
          '\n    <uses-permission android:name="android.permission.INTERNET"/>' +
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
