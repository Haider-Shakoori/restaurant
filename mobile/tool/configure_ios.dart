import 'dart:io';

const _plistBuddy = '/usr/libexec/PlistBuddy';

void main() {
  final plist = File('ios/Runner/Info.plist');

  if (!plist.existsSync()) {
    stderr.writeln(
      'Info.plist not found. Run flutter create --platforms=ios first.',
    );
    exitCode = 1;
    return;
  }

  if (!Platform.isMacOS || !File(_plistBuddy).existsSync()) {
    stderr.writeln(
      'iOS plist configuration requires macOS PlistBuddy at $_plistBuddy.',
    );
    exitCode = 1;
    return;
  }

  _setString(
    plist,
    'NSCameraUsageDescription',
    'Scan the BusinessOS Restaurant Desktop pairing QR code.',
  );
  _setString(
    plist,
    'NSLocalNetworkUsageDescription',
    'Connect directly to BusinessOS Restaurant Desktop over the restaurant Wi-Fi or LAN.',
  );

  _ensureDictionary(plist, 'NSAppTransportSecurity');
  _setBool(
    plist,
    'NSAppTransportSecurity:NSAllowsLocalNetworking',
    true,
  );

  _verify(
    plist,
    'NSCameraUsageDescription',
    'Scan the BusinessOS Restaurant Desktop pairing QR code.',
  );
  _verify(
    plist,
    'NSLocalNetworkUsageDescription',
    'Connect directly to BusinessOS Restaurant Desktop over the restaurant Wi-Fi or LAN.',
  );
  _verify(
    plist,
    'NSAppTransportSecurity:NSAllowsLocalNetworking',
    'true',
  );
}

void _setString(File plist, String key, String value) {
  final escaped = value.replaceAll('"', r'\"');
  final setResult = _run(plist, 'Set :$key "$escaped"', allowFailure: true);
  if (setResult.exitCode == 0) {
    return;
  }

  final addResult = _run(plist, 'Add :$key string "$escaped"');
  if (addResult.exitCode != 0) {
    _fail('Could not add iOS plist key $key.', addResult);
  }
}

void _ensureDictionary(File plist, String key) {
  final printResult = _run(plist, 'Print :$key', allowFailure: true);
  if (printResult.exitCode == 0) {
    return;
  }

  final addResult = _run(plist, 'Add :$key dict');
  if (addResult.exitCode != 0) {
    _fail('Could not add iOS plist dictionary $key.', addResult);
  }
}

void _setBool(File plist, String key, bool value) {
  final literal = value ? 'true' : 'false';
  final setResult = _run(plist, 'Set :$key $literal', allowFailure: true);
  if (setResult.exitCode == 0) {
    return;
  }

  final addResult = _run(plist, 'Add :$key bool $literal');
  if (addResult.exitCode != 0) {
    _fail('Could not add iOS plist boolean $key.', addResult);
  }
}

void _verify(File plist, String key, String expected) {
  final result = _run(plist, 'Print :$key');
  if (result.exitCode != 0) {
    _fail('Required iOS plist key $key is missing.', result);
  }

  final actual = result.stdout.toString().trim();
  if (actual.toLowerCase() != expected.toLowerCase()) {
    stderr.writeln(
      'Unexpected value for iOS plist key $key. Expected "$expected", got "$actual".',
    );
    exit(1);
  }
}

ProcessResult _run(
  File plist,
  String command, {
  bool allowFailure = false,
}) {
  final result = Process.runSync(
    _plistBuddy,
    <String>['-c', command, plist.path],
  );

  if (!allowFailure && result.exitCode != 0) {
    _fail('PlistBuddy command failed: $command', result);
  }

  return result;
}

Never _fail(String message, ProcessResult result) {
  stderr.writeln(message);
  final stderrText = result.stderr.toString().trim();
  if (stderrText.isNotEmpty) {
    stderr.writeln(stderrText);
  }
  exit(1);
}
