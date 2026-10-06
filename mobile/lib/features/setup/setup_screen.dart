import 'package:flutter/material.dart';

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
  final _pairingCode = TextEditingController();
  final _email = TextEditingController();
  final _password = TextEditingController();

  ConnectionMode _mode = ConnectionMode.automatic;
  bool _working = false;
  String? _error;

  @override
  void dispose() {
    _localServer.dispose();
    _cloudServer.dispose();
    _license.dispose();
    _pairingCode.dispose();
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
      await widget.dependencies.session.activateAndLogin(
        connectionMode: _mode,
        localUrl: _localServer.text,
        cloudUrl: _cloudServer.text,
        licenseKey: _license.text,
        email: _email.text,
        password: _password.text,
        pairingCode: _pairingCode.text,
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
    final showPairing = _mode != ConnectionMode.cloud;
    final showCloudCredentials = _mode != ConnectionMode.local;

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
                      if (showPairing) ...[
                        const SizedBox(height: 12),
                        TextField(
                          controller: _pairingCode,
                          keyboardType: TextInputType.number,
                          autocorrect: false,
                          decoration: InputDecoration(
                            labelText: s.pairingCode,
                            helperText: s.pairingHint,
                            prefixIcon: const Icon(Icons.phonelink_lock),
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
                      if (showCloudCredentials) ...[
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
                        ],
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
                        label: Text(_mode == ConnectionMode.local ? s.pairDevice : s.connect),
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
