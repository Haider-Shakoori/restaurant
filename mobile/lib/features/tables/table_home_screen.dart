import 'package:flutter/material.dart';

import '../../app/app_strings.dart';
import '../../app/dependencies.dart';
import '../conflicts/conflict_screen.dart';
import '../orders/order_screen.dart';

class TableHomeScreen extends StatefulWidget {
  const TableHomeScreen({
    required this.dependencies,
    required this.strings,
    required this.onLoggedOut,
    super.key,
  });

  final AppDependencies dependencies;
  final AppStrings strings;
  final VoidCallback onLoggedOut;

  @override
  State<TableHomeScreen> createState() => _TableHomeScreenState();
}

class _TableHomeScreenState extends State<TableHomeScreen> {
  List<Map<String, Object?>> _tables = const [];
  List<Map<String, Object?>> _orders = const [];
  int _pending = 0;
  int _conflicts = 0;
  String? _syncError;
  bool _syncing = false;

  @override
  void initState() {
    super.initState();
    _refresh();
  }

  Future<void> _refresh() async {
    final db = widget.dependencies.database;
    final values = await Future.wait<Object?>([
      db.tables(),
      db.activeOrders(),
      db.pendingCount(),
      db.conflictCount(),
      db.systemState('last_sync_error'),
    ]);

    if (!mounted) {
      return;
    }

    setState(() {
      _tables = List<Map<String, Object?>>.from(
        values[0]! as List<Map<String, Object?>>,
      );
      _orders = List<Map<String, Object?>>.from(
        values[1]! as List<Map<String, Object?>>,
      );
      _pending = values[2]! as int;
      _conflicts = values[3]! as int;
      _syncError = values[4] as String?;
    });
  }

  Future<void> _sync() async {
    if (_syncing) {
      return;
    }

    setState(() => _syncing = true);

    try {
      await widget.dependencies.syncCoordinator.syncNow();
    } finally {
      if (mounted) {
        setState(() => _syncing = false);
        await _refresh();
      }
    }
  }

  Future<void> _openTable(Map<String, Object?> table) async {
    final existing = _orders.where(
      (order) => order['table_id']?.toString() == table['id']?.toString(),
    );

    if (existing.isNotEmpty) {
      await _showOrder(existing.first['local_order_id']!.toString());
      return;
    }

    if (table['status'] != 'available') {
      return;
    }

    final guests = await showDialog<int>(
      context: context,
      builder: (context) => _GuestDialog(strings: widget.strings),
    );

    if (guests == null) {
      return;
    }

    try {
      final orderId = await widget.dependencies.orders.createOrder(
        tableId: table['id']!.toString(),
        guestCount: guests,
      );
      await _showOrder(orderId);
    } on Object catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(error.toString())),
        );
      }
    }
  }

  Future<void> _showOrder(String orderId) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (context) => OrderScreen(
          dependencies: widget.dependencies,
          strings: widget.strings,
          localOrderId: orderId,
        ),
      ),
    );
    await _refresh();
  }

  Future<void> _logout() async {
    await widget.dependencies.session.logoutLocal();
    widget.onLoggedOut();
  }

  @override
  Widget build(BuildContext context) {
    final s = widget.strings;

    return Scaffold(
      appBar: AppBar(
        title: Text(s.tables),
        actions: [
          IconButton(
            tooltip: s.sync,
            onPressed: _syncing ? null : _sync,
            icon: _syncing
                ? const SizedBox.square(
                    dimension: 20,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Icon(Icons.sync),
          ),
          PopupMenuButton<String>(
            onSelected: (value) {
              if (value == 'logout') {
                _logout();
              }
            },
            itemBuilder: (context) => const [
              PopupMenuItem(
                value: 'logout',
                child: Text('Sign out'),
              ),
            ],
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: _sync,
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                Chip(
                  avatar: const Icon(Icons.cloud_upload_outlined, size: 18),
                  label: Text(s.offlineQueued + ': $_pending'),
                ),
                ActionChip(
                  avatar: const Icon(Icons.warning_amber, size: 18),
                  label: Text(s.conflicts + ': $_conflicts'),
                  onPressed: _conflicts == 0
                      ? null
                      : () async {
                          await Navigator.of(context).push<void>(
                            MaterialPageRoute(
                              builder: (context) => ConflictScreen(
                                database: widget.dependencies.database,
                                strings: s,
                              ),
                            ),
                          );
                          await _refresh();
                        },
                ),
                if (_syncError != null && _syncError!.isNotEmpty)
                  Chip(
                    avatar: const Icon(Icons.cloud_off, size: 18),
                    label: Text(_syncError!),
                  ),
              ],
            ),
            const SizedBox(height: 16),
            GridView.builder(
              physics: const NeverScrollableScrollPhysics(),
              shrinkWrap: true,
              itemCount: _tables.length,
              gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
                maxCrossAxisExtent: 220,
                childAspectRatio: 1.45,
                crossAxisSpacing: 12,
                mainAxisSpacing: 12,
              ),
              itemBuilder: (context, index) {
                final table = _tables[index];
                final status = table['status']?.toString() ?? 'unknown';
                final occupied = status == 'occupied';

                return Card(
                  clipBehavior: Clip.antiAlias,
                  child: InkWell(
                    onTap: () => _openTable(table),
                    child: Padding(
                      padding: const EdgeInsets.all(16),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        mainAxisAlignment: MainAxisAlignment.spaceBetween,
                        children: [
                          Row(
                            children: [
                              Icon(
                                occupied
                                    ? Icons.table_restaurant
                                    : Icons.event_seat_outlined,
                              ),
                              const Spacer(),
                              Text(
                                status,
                                style: Theme.of(context).textTheme.labelMedium,
                              ),
                            ],
                          ),
                          Text(
                            table['name']?.toString() ??
                                table['code']!.toString(),
                            style: Theme.of(context).textTheme.titleMedium,
                          ),
                          Text(
                            table['area_name']?.toString() ?? '',
                            style: Theme.of(context).textTheme.bodySmall,
                          ),
                        ],
                      ),
                    ),
                  ),
                );
              },
            ),
          ],
        ),
      ),
    );
  }
}

class _GuestDialog extends StatefulWidget {
  const _GuestDialog({required this.strings});

  final AppStrings strings;

  @override
  State<_GuestDialog> createState() => _GuestDialogState();
}

class _GuestDialogState extends State<_GuestDialog> {
  int guests = 1;

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: Text(widget.strings.guests),
      content: Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          IconButton(
            onPressed: guests > 1 ? () => setState(() => guests--) : null,
            icon: const Icon(Icons.remove_circle_outline),
          ),
          Text('$guests', style: Theme.of(context).textTheme.headlineSmall),
          IconButton(
            onPressed: guests < 100 ? () => setState(() => guests++) : null,
            icon: const Icon(Icons.add_circle_outline),
          ),
        ],
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(context),
          child: const Text('Cancel'),
        ),
        FilledButton(
          onPressed: () => Navigator.pop(context, guests),
          child: Text(widget.strings.createOrder),
        ),
      ],
    );
  }
}
