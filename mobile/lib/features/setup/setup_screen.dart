import 'package:flutter/material.dart';

import '../../app/app_strings.dart';
import '../../app/dependencies.dart';
import '../../core/api/mobile_api_client.dart';

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
  final _server = TextEditingController();
  final _license = TextEditingController();
  final _email = TextEditingController();
  final _password = TextEditingController();

  bool _working = false;
  String? _error;

  @override
  void dispose() {
    _server.dispose();
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
      await widget.dependencies.session.activateAndLogin(
        tenantUrl: _server.text,
        licenseKey: _license.text,
        email: _email.text,
        password: _password.text,
      );
      widget.dependencies.syncCoordinator.start();
      widget.onConnected();
    } on ApiException catch (error) {
      setState(() {
        _error = error.message;
      });
    } on Object catch (error) {
      setState(() {
        _error = error.toString();
      });
    } finally {
      if (mounted) {
        setState(() {
          _working = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final s = widget.strings;

    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 480),
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
                      TextField(
                        controller: _server,
                        keyboardType: TextInputType.url,
                        autocorrect: false,
                        decoration: InputDecoration(
                          labelText: s.restaurantUrl,
                          hintText: 'restaurant.businessos.af',
                          border: const OutlineInputBorder(),
                        ),
                      ),
                      const SizedBox(height: 12),
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
