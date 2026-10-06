import 'package:flutter/material.dart';

import '../../app/app_strings.dart';
import '../../app/dependencies.dart';
import '../../core/api/mobile_api_client.dart';
import '../../core/connection/connection_mode.dart';
import 'pairing_payload.dart';
import 'pairing_scanner_screen.dart';

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

  @override
  void dispose() {
    _localServer.dispose();
    _cloudServer.dispose();
    _license.dispose();
    _email.dispose();
    _password.dispose();
    super.dispose();
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
      if (_pairingToken != null) {
        await widget.dependencies.session.pairAndLogin(
          localUrl: _localServer.text.trim().isEmpty ? null : _localServer.text,
          cloudUrl: _cloudServer.text,
          pairingToken: _pairingToken!,
          email: _email.text,
          password: _password.text,
        );
      } else {
        await widget.dependencies.session.activateAndLogin(
          connectionMode: _mode,
          localUrl: _localServer.text,
          cloudUrl: _cloudServer.text,
          licenseKey: _license.text,
          email: _email.text,
          password: _password.text,
        );
      }
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

  Future<void> _scanPairingCode() async {
    final payload = await Navigator.of(context).push<RestaurantPairingPayload>(
      MaterialPageRoute<RestaurantPairingPayload>(
        builder: (_) => const PairingScannerScreen(),
      ),
    );

    if (payload == null || !mounted) {
      return;
    }

    setState(() {
      _pairingToken = payload.pairingToken;
      _mode = ConnectionMode.automatic;
      _localServer.text = payload.localUrl ?? '';
      _cloudServer.text = payload.cloudUrl;
      _license.clear();
      _error = null;
    });
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
                      Center(
                        child: Container(
                          width: 72,
                          height: 72,
                          decoration: const BoxDecoration(
                            color: Color(0xFF071A2E),
                            shape: BoxShape.circle,
                          ),
                          child: Stack(
                            alignment: Alignment.center,
                            children: [
                              Container(
                                width: 56,
                                height: 56,
                                decoration: const BoxDecoration(
                                  color: Color(0xFF0B82F6),
                                  shape: BoxShape.circle,
                                ),
                              ),
                              const Text(
                                'B',
                                style: TextStyle(
                                  color: Colors.white,
                                  fontSize: 34,
                                  fontWeight: FontWeight.w900,
                                  height: 1,
                                ),
                              ),
                              const Positioned(
                                right: 11,
                                top: 11,
                                child: CircleAvatar(
                                  radius: 5,
                                  backgroundColor: Color(0xFF37C6FF),
                                ),
                              ),
                            ],
                          ),
                        ),
                      ),
                      const SizedBox(height: 16),
                      Text(
                        s.setupTitle,
                        textAlign: TextAlign.center,
                        style: Theme.of(context).textTheme.headlineSmall,
                      ),
                      FilledButton.tonalIcon(
                        onPressed: _working ? null : _scanPairingCode,
                        icon: const Icon(Icons.qr_code_scanner_rounded),
                        label: const Text('Scan desktop QR code'),
                      ),
                      const SizedBox(height: 10),
                      Text(
                        _pairingToken == null
                            ? 'Or enter the restaurant connection details manually below.'
                            : 'Desktop pairing code loaded. Enter your staff sign-in details to finish connecting.',
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
                      if (_pairingToken == null) ...[
                        const SizedBox(height: 20),
                        TextField(
                          controller: _license,
                        autocorrect: false,
                        textCapitalization: TextCapitalization.characters,
                        decoration: InputDecoration(
                          labelText: s.licenseKey,
                          border: const OutlineInputBorder(),
                        ),
                      ),
                      ] else ...[
                        const SizedBox(height: 16),
                        Row(
                          children: [
                            const Icon(Icons.verified_rounded, size: 20),
                            const SizedBox(width: 8),
                            const Expanded(
                              child: Text('Secure one-time pairing token ready'),
                            ),
                            TextButton(
                              onPressed: _working
                                  ? null
                                  : () => setState(() => _pairingToken = null),
                              child: const Text('Use manual setup'),
                            ),
                          ],
                        ),
                      ],
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
