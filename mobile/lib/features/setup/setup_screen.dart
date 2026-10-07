import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

import '../../app/app_strings.dart';
import '../../app/dependencies.dart';
import '../../core/api/mobile_api_client.dart';
import '../../core/connection/connection_mode.dart';

class SetupScreen extends StatefulWidget {
  const SetupScreen({
    required this.dependencies,
    required this.strings,
    required this.onConnected,
    super.key,
  });

  final AppDependencies dependencies;
  final AppStrings strings;
  final VoidCallback onConnected;

  @override
  State<SetupScreen> createState() => _SetupScreenState();
}

class _SetupScreenState extends State<SetupScreen> {
  final _localServer = TextEditingController();
  final _cloudServer = TextEditingController();
  final _license = TextEditingController();
  final _email = TextEditingController();
  final _password = TextEditingController();

  ConnectionMode _mode = ConnectionMode.automatic;
  bool _working = false;
  String? _error;
  String? _pairingToken;
  String? _pairingExpiresAt;

  @override
  void dispose() {
    _localServer.dispose();
    _cloudServer.dispose();
    _license.dispose();
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _scanQr() async {
    final payload = await Navigator.of(context).push<String>(
      MaterialPageRoute(builder: (_) => const _PairingQrScanner()),
    );
    if (payload == null || payload.isEmpty) return;
    try {
      final data = Map<String, dynamic>.from(jsonDecode(payload) as Map);
      if (data['type'] != 'businessos.restaurant.pairing.v1') {
        throw const FormatException('Unsupported Restaurant pairing QR.');
      }
      setState(() {
        _mode = ConnectionMode.automatic;
        _localServer.text = data['local_url']?.toString() ?? '';
        _cloudServer.text = data['cloud_url']?.toString() ?? '';
        _license.text = data['license_key']?.toString() ?? '';
        _pairingToken = data['pairing_token']?.toString();
        _pairingExpiresAt = data['pairing_expires_at']?.toString();
        _error = null;
      });
    } catch (_) {
      setState(() => _error = 'This QR code is not a valid BusinessOS Restaurant pairing code.');
    }
  }

  Future<void> _connect() async {
    if (_working) {
      return;
    }

    setState(() {
      _working = true;
      _error = null;
    });

    try {
      await widget.dependencies.session.activateAndLogin(
        connectionMode: _mode,
        localUrl: _localServer.text,
        cloudUrl: _cloudServer.text,
        licenseKey: _license.text,
        pairingToken: _pairingToken,
        email: _email.text,
        password: _password.text,
      );
      widget.dependencies.syncCoordinator.start();
      widget.onConnected();
    } on ApiException catch (error) {
      if (mounted) {
        setState(() => _error = error.message);
      }
    } on Object catch (error) {
      if (mounted) {
        setState(() => _error = error.toString());
      }
    } finally {
      if (mounted) {
        setState(() => _working = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final s = widget.strings;
    final showLocal = _mode != ConnectionMode.cloud;
    final showCloud = _mode != ConnectionMode.local;

    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 520),
              child: Card(
                child: Padding(
                  padding: const EdgeInsets.all(24),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      const Icon(Icons.restaurant_menu, size: 52),
                      const SizedBox(height: 16),
                      Text(
                        s.setupTitle,
                        textAlign: TextAlign.center,
                        style: Theme.of(context).textTheme.headlineSmall,
                      ),
                      const SizedBox(height: 16),
                      FilledButton.tonalIcon(
                        onPressed: _working ? null : _scanQr,
                        icon: const Icon(Icons.qr_code_scanner_rounded),
                        label: const Text('Scan Desktop QR'),
                      ),
                      const SizedBox(height: 8),
                      Text(
                        'Scan the QR shown in Restaurant Desktop Settings, or enter the connection details below.',
                        textAlign: TextAlign.center,
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                      const SizedBox(height: 24),
                      Text(
                        s.connectionMode,
                        style: Theme.of(context).textTheme.titleSmall,
                      ),
                      const SizedBox(height: 8),
                      SegmentedButton<ConnectionMode>(
                        segments: [
                          ButtonSegment(
                            value: ConnectionMode.local,
                            icon: const Icon(Icons.lan_outlined),
                            label: Text(s.local),
                          ),
                          ButtonSegment(
                            value: ConnectionMode.cloud,
                            icon: const Icon(Icons.cloud_outlined),
                            label: Text(s.cloud),
                          ),
                          ButtonSegment(
                            value: ConnectionMode.automatic,
                            icon: const Icon(Icons.alt_route),
                            label: Text(s.automatic),
                          ),
                        ],
                        selected: <ConnectionMode>{_mode},
                        onSelectionChanged: _working
                            ? null
                            : (selection) {
                                setState(() {
                                  _mode = selection.first;
                                  _error = null;
                                });
                              },
                      ),
                      if (_mode == ConnectionMode.automatic) ...[
                        const SizedBox(height: 8),
                        Text(
                          s.automaticHint,
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                      ],
                      if (showLocal) ...[
                        const SizedBox(height: 20),
                        TextField(
                          controller: _localServer,
                          keyboardType: TextInputType.url,
                          autocorrect: false,
                          decoration: InputDecoration(
                            labelText: s.localServer,
                            hintText: '192.168.1.10',
                            prefixIcon: const Icon(Icons.lan_outlined),
                            border: const OutlineInputBorder(),
                          ),
                        ),
                      ],
                      if (showCloud) ...[
                        const SizedBox(height: 12),
                        TextField(
                          controller: _cloudServer,
                          keyboardType: TextInputType.url,
                          autocorrect: false,
                          decoration: InputDecoration(
                            labelText: s.cloudServer,
                            hintText: 'restaurant.businessos.af',
                            prefixIcon: const Icon(Icons.cloud_outlined),
                            border: const OutlineInputBorder(),
                          ),
                        ),
                      ],
                      const SizedBox(height: 20),
                      if (_pairingToken != null && _pairingToken!.isNotEmpty)
                        Card(
                          child: ListTile(
                            leading: const Icon(Icons.verified_user_outlined),
                            title: const Text('Desktop pairing ready'),
                            subtitle: Text(
                              _pairingExpiresAt == null
                                  ? 'This one-time code will activate this waiter device. Sign in below with the waiter account.'
                                  : 'One-time activation approved by Desktop. Expires: $_pairingExpiresAt',
                            ),
                            trailing: IconButton(
                              tooltip: 'Use license key instead',
                              icon: const Icon(Icons.close),
                              onPressed: _working
                                  ? null
                                  : () => setState(() {
                                      _pairingToken = null;
                                      _pairingExpiresAt = null;
                                    }),
                            ),
                          ),
                        )
                      else
                        TextField(
                          controller: _license,
                          autocorrect: false,
                          textCapitalization: TextCapitalization.characters,
                          decoration: InputDecoration(
                            labelText: s.licenseKey,
                            border: const OutlineInputBorder(),
                          ),
                        ),
                      const SizedBox(height: 12),
                      TextField(
                        controller: _email,
                        keyboardType: TextInputType.emailAddress,
                        autocorrect: false,
                        decoration: InputDecoration(
                          labelText: s.email,
                          border: const OutlineInputBorder(),
                        ),
                      ),
                      const SizedBox(height: 12),
                      TextField(
                        controller: _password,
                        obscureText: true,
                        decoration: InputDecoration(
                          labelText: s.password,
                          border: const OutlineInputBorder(),
                        ),
                      ),
                      if (_error != null) ...[
                        const SizedBox(height: 12),
                        Text(
                          _error!,
                          style: TextStyle(
                            color: Theme.of(context).colorScheme.error,
                          ),
                        ),
                      ],
                      const SizedBox(height: 20),
                      FilledButton.icon(
                        onPressed: _working ? null : _connect,
                        icon: _working
                            ? const SizedBox.square(
                                dimension: 18,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              )
                            : const Icon(Icons.lock_open),
                        label: Text(s.connect),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}


class _PairingQrScanner extends StatefulWidget {
  const _PairingQrScanner();

  @override
  State<_PairingQrScanner> createState() => _PairingQrScannerState();
}

class _PairingQrScannerState extends State<_PairingQrScanner> {
  bool _handled = false;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Scan Restaurant Desktop QR')),
      body: Stack(
        fit: StackFit.expand,
        children: [
          MobileScanner(
            onDetect: (capture) {
              if (_handled) return;
              String? value;
              for (final barcode in capture.barcodes) {
                final raw = barcode.rawValue;
                if (raw != null && raw.isNotEmpty) {
                  value = raw;
                  break;
                }
              }
              if (value == null) return;
              _handled = true;
              Navigator.of(context).pop(value);
            },
          ),
          IgnorePointer(
            child: Center(
              child: Container(
                width: 250,
                height: 250,
                decoration: BoxDecoration(
                  border: Border.all(
                    color: Theme.of(context).colorScheme.primary,
                    width: 3,
                  ),
                  borderRadius: BorderRadius.circular(24),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
