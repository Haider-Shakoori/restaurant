import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../app/app_strings.dart';
import '../../app/dependencies.dart';
import '../../core/connection/connection_mode.dart';
import '../../core/models/session_credentials.dart';
import '../conflicts/conflict_screen.dart';
import '../orders/menu_browser_screen.dart';
import '../orders/order_screen.dart';
import 'ready_pickup_alerts.dart';

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
  List<Map<String, Object?>> _branches = const [];
  List<Map<String, Object?>> _tables = const [];
  List<Map<String, Object?>> _orders = const [];
  int _pending = 0;
  int _conflicts = 0;
  String? _syncError;
  String? _connectionStatus;
  SessionCredentials? _session;
  bool _syncing = false;
  Timer? _readyRefreshTimer;
  bool _refreshInProgress = false;
  ReadyPickupTracker? _alertTracker;
  List<ReadyPickupAlert> _readyAlerts = const [];

  @override
  void initState() {
    super.initState();
    _refresh();
    // SyncCoordinator pulls LAN/cloud changes every 30 seconds; read the
    // committed SQLite snapshots more frequently to surface new READY events
    // promptly without starting another network connection per refresh.
    _readyRefreshTimer = Timer.periodic(
      const Duration(seconds: 6),
      (_) => unawaited(_refresh()),
    );
  }

  @override
  void dispose() {
    _readyRefreshTimer?.cancel();
    super.dispose();
  }

  Future<void> _refresh() async {
    if (_refreshInProgress) return;
    _refreshInProgress = true;
    try {
    final db = widget.dependencies.database;
    final values = await Future.wait<Object?>([
      db.branches(),
      db.tables(),
      db.activeOrders(),
      db.pendingCount(),
      db.conflictCount(),
      db.systemState('last_sync_error'),
      db.systemState('active_connection'),
      widget.dependencies.credentials.readSession(),
    ]);

    if (!mounted) return;

    final nextOrders = List<Map<String, Object?>>.from(
      values[2]! as List<Map<String, Object?>>,
    );
    final nextTables = List<Map<String, Object?>>.from(
      values[1]! as List<Map<String, Object?>>,
    );
    final alerts = readyPickupAlerts(nextOrders, nextTables);
    final session = values[7] as SessionCredentials?;
    // Never share an alert's acknowledged state across tenant installations.
    final alertScope = session?.tenantId ?? session?.deviceId ?? 'signed-out';
    final setting = 'ready_pickup_seen_$alertScope';
    _alertTracker ??= ReadyPickupTracker.decode(
      await db.systemState(setting),
    );
    final freshAlerts = _alertTracker!.newlyReady(alerts);
    if (freshAlerts.isNotEmpty) {
      await db.setSystemState(setting, _alertTracker!.encode());
    }

    if (!mounted) return;
    setState(() {
      _readyAlerts = alerts;
      _branches = List<Map<String, Object?>>.from(
        values[0]! as List<Map<String, Object?>>,
      );
      _tables = List<Map<String, Object?>>.from(
        values[1]! as List<Map<String, Object?>>,
      );
      _orders = List<Map<String, Object?>>.from(
        values[2]! as List<Map<String, Object?>>,
      );
      _pending = values[3]! as int;
      _conflicts = values[4]! as int;
      _syncError = values[5] as String?;
      _connectionStatus = values[6] as String?;
      _session = session;
    });

    if (freshAlerts.isNotEmpty && session != null) {
      unawaited(SystemSound.play(SystemSoundType.alert));
      unawaited(HapticFeedback.heavyImpact());
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          duration: const Duration(seconds: 8),
          content: Text(
            'READY FOR PICKUP: ${freshAlerts.map((a) => a.reference).join(', ')}',
          ),
          action: SnackBarAction(
            label: 'View',
            onPressed: () => _showOrder(freshAlerts.first.orderId),
          ),
        ),
      );
    }
    } finally {
      _refreshInProgress = false;
    }
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

  Future<void> _openMenu() async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (context) => MenuBrowserScreen(
          dependencies: widget.dependencies,
          onStartServiceOrder: _createServiceOrder,
        ),
      ),
    );
    if (mounted) await _refresh();
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
        branchId: table['branch_id']!.toString(),
        serviceType: 'dine_in',
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

  Future<void> _createServiceOrder(String serviceType) async {
    if (_branches.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('No active restaurant branch is available.')),
      );
      return;
    }

    final details = await showDialog<_ServiceOrderDetails>(
      context: context,
      builder: (context) => _ServiceOrderDialog(
        serviceType: serviceType,
        branches: _branches,
      ),
    );

    if (details == null) {
      return;
    }

    try {
      final orderId = await widget.dependencies.orders.createOrder(
        branchId: details.branchId,
        serviceType: serviceType,
        serviceReference: details.reference,
        guestCount: details.guests,
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
    _alertTracker = null;
    _readyAlerts = const [];
    await widget.dependencies.session.logoutLocal();
    widget.onLoggedOut();
  }

  @override
  Widget build(BuildContext context) {
    final s = widget.strings;
    final session = _session;

    return Scaffold(
      appBar: AppBar(
        title: Text(s.tables),
        actions: [
          IconButton(
            tooltip: '${_readyAlerts.length} orders ready for pickup',
            onPressed: _readyAlerts.isEmpty
                ? null
                : () => _showOrder(_readyAlerts.first.orderId),
            icon: Badge(
              isLabelVisible: _readyAlerts.isNotEmpty,
              label: Text('${_readyAlerts.length}'),
              child: const Icon(Icons.notifications_active_outlined),
            ),
          ),
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
                if (session != null)
                  Chip(
                    avatar: Icon(
                      _syncing
                          ? Icons.sync
                          : _connectionStatus == 'offline'
                              ? Icons.cloud_off_outlined
                              : session.activeChannel == ConnectionChannel.local
                                  ? Icons.lan_outlined
                                  : Icons.cloud_outlined,
                      size: 18,
                    ),
                    label: Text(
                      _syncing
                          ? 'Syncing'
                          : _connectionStatus == 'offline'
                              ? 'Offline · queued locally'
                              : (session.connectionMode == ConnectionMode.automatic
                                      ? s.automatic + ' · '
                                      : '') +
                                  (session.activeChannel == ConnectionChannel.local
                                      ? s.connectedLocal
                                      : s.connectedCloud),
                    ),
                  ),
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
            if (_readyAlerts.isNotEmpty) ...[
              const SizedBox(height: 14),
              Card(
                color: const Color(0xFFFFF7E1),
                margin: EdgeInsets.zero,
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(14),
                  side: const BorderSide(color: Color(0xFFEBC46A), width: 1.5),
                ),
                child: Padding(
                  padding: const EdgeInsets.all(14),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      const Row(children: [
                        Icon(Icons.notifications_active_rounded,
                            color: Color(0xFF916312)),
                        SizedBox(width: 8),
                        Expanded(child: Text('READY FOR PICKUP',
                            style: TextStyle(fontWeight: FontWeight.w900,
                                color: Color(0xFF46330F)))),
                      ]),
                      const SizedBox(height: 7),
                      const Text('Collect prepared food, deliver to the guest, '
                          'then mark the complete order Served in POS.',
                          style: TextStyle(color: Color(0xFF554521))),
                      for (final alert in _readyAlerts)
                        ListTile(
                          dense: true,
                          contentPadding: EdgeInsets.zero,
                          title: Text(alert.reference, style:
                              const TextStyle(fontWeight: FontWeight.w800)),
                          subtitle: Text('${alert.itemCount} ready item(s)'),
                          trailing: const Icon(Icons.chevron_right_rounded),
                          onTap: () => _showOrder(alert.orderId),
                        ),
                    ],
                  ),
                ),
              ),
            ],
            const SizedBox(height: 16),
            Card(
              margin: EdgeInsets.zero,
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Row(children: [
                  const Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('Restaurant menu', style: TextStyle(
                            fontSize: 18, fontWeight: FontWeight.w800)),
                        SizedBox(height: 4),
                        Text('Browse food photos, categories and prices.'),
                      ],
                    ),
                  ),
                  FilledButton.icon(
                    onPressed: _openMenu,
                    icon: const Icon(Icons.restaurant_menu_rounded),
                    label: const Text('Browse menu'),
                  ),
                ]),
              ),
            ),
            const SizedBox(height: 14),
            Wrap(
              spacing: 10,
              runSpacing: 10,
              children: [
                FilledButton.icon(
                  onPressed: () => _createServiceOrder('takeaway'),
                  icon: const Icon(Icons.shopping_bag_outlined),
                  label: const Text('Takeaway'),
                ),
                FilledButton.tonalIcon(
                  onPressed: () => _createServiceOrder('delivery'),
                  icon: const Icon(Icons.delivery_dining_outlined),
                  label: const Text('Delivery'),
                ),
                FilledButton.tonalIcon(
                  onPressed: () => _createServiceOrder('counter'),
                  icon: const Icon(Icons.point_of_sale_outlined),
                  label: const Text('Counter'),
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


class _ServiceOrderDetails {
  const _ServiceOrderDetails({
    required this.branchId,
    required this.guests,
    this.reference,
  });

  final String branchId;
  final int guests;
  final String? reference;
}

class _ServiceOrderDialog extends StatefulWidget {
  const _ServiceOrderDialog({
    required this.serviceType,
    required this.branches,
  });

  final String serviceType;
  final List<Map<String, Object?>> branches;

  @override
  State<_ServiceOrderDialog> createState() => _ServiceOrderDialogState();
}

class _ServiceOrderDialogState extends State<_ServiceOrderDialog> {
  late String branchId;
  final reference = TextEditingController();
  int guests = 1;

  @override
  void initState() {
    super.initState();
    branchId = widget.branches.first['id']!.toString();
  }

  @override
  void dispose() {
    reference.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final title = widget.serviceType[0].toUpperCase() +
        widget.serviceType.substring(1);

    return AlertDialog(
      title: Text('New $title order'),
      content: SizedBox(
        width: 420,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            DropdownButtonFormField<String>(
              initialValue: branchId,
              decoration: const InputDecoration(labelText: 'Branch'),
              items: widget.branches
                  .map(
                    (branch) => DropdownMenuItem<String>(
                      value: branch['id']!.toString(),
                      child: Text(branch['name']!.toString()),
                    ),
                  )
                  .toList(),
              onChanged: (value) {
                if (value != null) {
                  setState(() => branchId = value);
                }
              },
            ),
            const SizedBox(height: 12),
            TextField(
              controller: reference,
              decoration: InputDecoration(
                labelText: widget.serviceType == 'delivery'
                    ? 'Delivery / customer reference'
                    : 'Order reference (optional)',
              ),
            ),
            const SizedBox(height: 12),
            Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                IconButton(
                  onPressed:
                      guests > 1 ? () => setState(() => guests--) : null,
                  icon: const Icon(Icons.remove_circle_outline),
                ),
                Text('$guests guest(s)'),
                IconButton(
                  onPressed:
                      guests < 100 ? () => setState(() => guests++) : null,
                  icon: const Icon(Icons.add_circle_outline),
                ),
              ],
            ),
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('Cancel'),
        ),
        FilledButton(
          onPressed: () {
            final cleaned = reference.text.trim();
            Navigator.of(context).pop(
              _ServiceOrderDetails(
                branchId: branchId,
                guests: guests,
                reference: cleaned.isEmpty ? null : cleaned,
              ),
            );
          },
          child: const Text('Create order'),
        ),
      ],
    );
  }
}
