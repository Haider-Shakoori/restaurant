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

  final manifestClose = content.indexOf('>');

  if (manifestClose >= 0 && !content.contains('android.permission.INTERNET')) {
    content = content.substring(0, manifestClose + 1) +
        '\n    <uses-permission android:name="android.permission.INTERNET"/>' +
        content.substring(manifestClose + 1);
  }

  if (!content.contains('android.permission.CAMERA')) {
    final close = content.indexOf('>');
    if (close >= 0) {
      content = content.substring(0, close + 1) +
          '\n    <uses-permission android:name="android.permission.CAMERA"/>' +
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

  _writeBusinessOsLauncherIcon();
}

void _writeBusinessOsLauncherIcon() {
  final drawable = Directory('android/app/src/main/res/drawable');
  final mipmapAny = Directory('android/app/src/main/res/mipmap-anydpi');
  final mipmapV26 = Directory('android/app/src/main/res/mipmap-anydpi-v26');
  final values = Directory('android/app/src/main/res/values');

  drawable.createSync(recursive: true);
  mipmapAny.createSync(recursive: true);
  mipmapV26.createSync(recursive: true);
  values.createSync(recursive: true);

  File('${values.path}/businessos_colors.xml').writeAsStringSync(
    '''<?xml version="1.0" encoding="utf-8"?>
<resources>
    <color name="businessos_icon_background">#071A2E</color>
</resources>
''',
  );

  File('${drawable.path}/businessos_restaurant_foreground.xml')
      .writeAsStringSync(
    '''<?xml version="1.0" encoding="utf-8"?>
<vector xmlns:android="http://schemas.android.com/apk/res/android"
    android:width="108dp"
    android:height="108dp"
    android:viewportWidth="108"
    android:viewportHeight="108">
    <path
        android:fillColor="#0B82F6"
        android:pathData="M54,14 A40,40 0,1 0,54 94 A40,40 0,1 0,54 14"/>
    <path
        android:fillColor="#37C6FF"
        android:pathData="M81,22 A6,6 0,1 0,81 34 A6,6 0,1 0,81 22 M27,78 A4.5,4.5 0,1 0,27 87 A4.5,4.5 0,1 0,27 78"/>
    <path
        android:fillColor="#FFFFFF"
        android:pathData="M35,26 L57,26 C72,26 79,33 79,44 C79,52 75,57 68,60 C77,63 82,70 82,79 C82,93 71,101 55,101 L35,101 Z M47,38 L47,55 L56,55 C64,55 68,52 68,46 C68,41 64,38 56,38 Z M47,66 L47,89 L57,89 C65,89 70,85 70,78 C70,70 65,66 56,66 Z"/>
</vector>
''',
  );

  final legacy = '''<?xml version="1.0" encoding="utf-8"?>
<vector xmlns:android="http://schemas.android.com/apk/res/android"
    android:width="48dp"
    android:height="48dp"
    android:viewportWidth="108"
    android:viewportHeight="108">
    <path android:fillColor="#071A2E" android:pathData="M54,4 A50,50 0,1 0,54 104 A50,50 0,1 0,54 4"/>
    <path android:fillColor="#0B82F6" android:pathData="M54,14 A40,40 0,1 0,54 94 A40,40 0,1 0,54 14"/>
    <path android:fillColor="#37C6FF" android:pathData="M81,22 A6,6 0,1 0,81 34 A6,6 0,1 0,81 22"/>
    <path android:fillColor="#FFFFFF" android:pathData="M35,26 L57,26 C72,26 79,33 79,44 C79,52 75,57 68,60 C77,63 82,70 82,79 C82,93 71,101 55,101 L35,101 Z M47,38 L47,55 L56,55 C64,55 68,52 68,46 C68,41 64,38 56,38 Z M47,66 L47,89 L57,89 C65,89 70,85 70,78 C70,70 65,66 56,66 Z"/>
</vector>
''';

  File('${mipmapAny.path}/ic_launcher.xml').writeAsStringSync(legacy);
  File('${mipmapAny.path}/ic_launcher_round.xml').writeAsStringSync(legacy);

  final adaptive = '''<?xml version="1.0" encoding="utf-8"?>
<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">
    <background android:drawable="@color/businessos_icon_background"/>
    <foreground android:drawable="@drawable/businessos_restaurant_foreground"/>
</adaptive-icon>
''';

  File('${mipmapV26.path}/ic_launcher.xml').writeAsStringSync(adaptive);
  File('${mipmapV26.path}/ic_launcher_round.xml').writeAsStringSync(adaptive);
}
