import 'dart:io';

void main() {
  final plist = File('ios/Runner/Info.plist');

  if (!plist.existsSync()) {
    stderr.writeln(
      'Info.plist not found. Run flutter create --platforms=ios first.',
    );
    exitCode = 1;
    return;
  }

  var content = plist.readAsStringSync();

  if (!content.contains('<key>NSCameraUsageDescription</key>')) {
    const entry = '''
\t<key>NSCameraUsageDescription</key>
\t<string>Scan the BusinessOS Restaurant Desktop pairing QR code.</string>
''';
    content = content.replaceFirst('</dict>', '$entry</dict>');
  }

  plist.writeAsStringSync(content);
}
