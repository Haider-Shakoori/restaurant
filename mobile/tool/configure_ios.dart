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

  if (!content.contains('<key>NSLocalNetworkUsageDescription</key>')) {
    const entry = '''
\t<key>NSLocalNetworkUsageDescription</key>
\t<string>Connect directly to BusinessOS Restaurant Desktop over the restaurant Wi-Fi or LAN.</string>
''';
    content = content.replaceFirst('</dict>', '$entry</dict>');
  }

  if (!content.contains('<key>NSAllowsLocalNetworking</key>')) {
    const entry = '''
\t<key>NSAppTransportSecurity</key>
\t<dict>
\t\t<key>NSAllowsLocalNetworking</key>
\t\t<true/>
\t</dict>
''';
    content = content.replaceFirst('</dict>', '$entry</dict>');
  }

  plist.writeAsStringSync(content);
}
