import 'package:flutter/material.dart';
import '../../data/local/local_database.dart';

/// Explicitly discards only phone-only orders, never server orders.
class LocalOrderRecoveryScreen extends StatefulWidget {
  const LocalOrderRecoveryScreen({
    required this.database,
    required this.refreshFromDesktop,
    super.key,
  });
  final LocalDatabase database;
  final Future<void> Function() refreshFromDesktop;

  @override
  State<LocalOrderRecoveryScreen> createState() => _RecoveryState();
}

class _RecoveryState extends State<LocalOrderRecoveryScreen> {
  late Future<List<Map<String, Object?>>> _orders;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _reload();
  }

  void _reload() {
    if (mounted) {
      setState(() => _orders = widget.database.activeOrders());
    }
  }

  void _message(String value) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(value), duration: const Duration(seconds: 6)),
    );
  }

  Future<void> _sync() async {
    if (_busy) return;
    setState(() => _busy = true);
    try {
      await widget.refreshFromDesktop();
      _message('Table statuses and orders refreshed from desktop.');
    } on Object catch (error) {
      _message('Sync failed; cached data shown: $error');
    } finally {
      if (mounted) setState(() => _busy = false);
      _reload();
    }
  }

  Future<bool> _confirm(int count) async {
    final controller = TextEditingController();
    try {
      return await showDialog<bool>(
        context: context,
        builder: (dialogContext) => StatefulBuilder(
          builder: (dialogContext, refresh) => AlertDialog(
            title: Text('Discard $count phone-only orders?'),
            content: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Text(
                  'This permanently removes these unsent orders and their '
                  'pending/conflicted uploads from the phone. Desktop orders '
                  'will NOT be removed. Kitchen staff may never receive the '
                  'discarded orders. Do not proceed if they are still needed.',
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: controller,
                  onChanged: (_) => refresh(() {}),
                  decoration: const InputDecoration(
                    labelText: 'Type DELETE to confirm',
                    border: OutlineInputBorder(),
                  ),
                ),
              ],
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(dialogContext, false),
                child: const Text('Keep orders'),
              ),
              FilledButton(
                onPressed: controller.text == 'DELETE'
                    ? () => Navigator.pop(dialogContext, true)
                    : null,
                child: const Text('Discard locally'),
              ),
            ],
          ),
        ),
      ) ?? false;
    } finally {
      controller.dispose();
    }
  }

  Future<void> _discard(List<String> ids) async {
    if (_busy || ids.isEmpty || !await _confirm(ids.length)) return;
    if (!mounted) return;
    setState(() => _busy = true);
    try {
      await widget.database.discardLocalOrders(ids);
      _message('Discarded phone-only orders.');
      try {
        await widget.refreshFromDesktop();
      } on Object catch (error) {
        _message('Local cleanup completed; desktop refresh failed: $error');
      }
    } on Object catch (error) {
      _message('Nothing discarded: $error');
    } finally {
      if (mounted) setState(() => _busy = false);
      _reload();
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Local order recovery'),
      actions: [
        IconButton(
          tooltip: 'Refresh from desktop',
          icon: const Icon(Icons.sync),
          onPressed: _busy ? null : _sync,
        ),
      ],
    ),
    body: FutureBuilder<List<Map<String, Object?>>>(
      future: _orders,
      builder: (context, snapshot) {
        if (!snapshot.hasData) {
          return const Center(child: CircularProgressIndicator());
        }
        final orders = snapshot.data!;
        final phoneOnly = orders.where((o) => o['server_id'] == null).toList();
        return ListView(
          padding: const EdgeInsets.all(16),
          children: [
            const Text(
              'Refresh first. Only phone-only orders can be discarded here. '
              'Server-owned orders must be closed or cancelled on desktop.',
            ),
            const SizedBox(height: 12),
            FilledButton.icon(
              onPressed: _busy ? null : _sync,
              icon: const Icon(Icons.sync),
              label: const Text('Match orders and tables with desktop'),
            ),
            if (phoneOnly.isNotEmpty)
              OutlinedButton.icon(
                onPressed: _busy ? null : () => _discard(
                  phoneOnly.map((o) => o['local_order_id']!.toString()).toList(),
                ),
                icon: const Icon(Icons.delete_sweep_outlined),
                label: Text('Discard all ${phoneOnly.length} phone-only orders'),
              ),
            const SizedBox(height: 12),
            if (orders.isEmpty)
              const Text('No active orders cached on this phone.'),
            ...orders.map((order) {
              final localOnly = order['server_id'] == null;
              final id = order['local_order_id']!.toString();
              final shortId = id.length > 8 ? id.substring(0, 8) : id;
              final number = (order['display_number'] as num?)?.toInt() ?? 0;
              return Card(
                child: ListTile(
                  leading: Icon(localOnly
                      ? Icons.phonelink_erase
                      : Icons.dns_outlined),
                  title: Text(number > 0 ? 'Order #$number' : 'Order $shortId'),
                  subtitle: Text(
                    '${order['status']} · ${localOnly ? 'Phone only / unsent' : 'Desktop confirmed'}',
                  ),
                  trailing: localOnly
                      ? IconButton(
                          tooltip: 'Discard this phone-only order',
                          icon: const Icon(Icons.delete_outline),
                          onPressed: _busy ? null : () => _discard([id]),
                        )
                      : const Icon(Icons.lock_outline),
                ),
              );
            }),
          ],
        );
      },
    ),
  );
}
