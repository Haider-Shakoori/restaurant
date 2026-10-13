import 'dart:async';

import 'package:flutter/material.dart';

import '../../app/app_strings.dart';
import '../../app/app_theme.dart';
import '../../app/dependencies.dart';
import '../../core/connection/connection_mode.dart';
import '../../core/format/restaurant_formats.dart';
import '../../core/models/session_credentials.dart';
import '../conflicts/conflict_screen.dart';
import '../orders/menu_browser_screen.dart';
import '../orders/order_screen.dart';
import 'local_order_recovery_screen.dart';
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
  String? _lastSyncAt;
  SessionCredentials? _session;
  bool _syncing = false;
  Timer? _readyRefreshTimer;
  bool _refreshInProgress = false;
  List<ReadyPickupAlert> _readyAlerts = const [];

  @override
  void initState() {
    super.initState();
    _refresh();
    widget.dependencies.pushNotifications.tappedOrderId.addListener(
      _handlePushTap,
    );
    if (widget.dependencies.pushNotifications.tappedOrderId.value != null) {
      WidgetsBinding.instance.addPostFrameCallback((_) => _handlePushTap());
    }
    widget.dependencies.localNotifications.tappedOrderId.addListener(
      _handleLocalTap,
    );
    if (widget.dependencies.localNotifications.tappedOrderId.value != null) {
      WidgetsBinding.instance.addPostFrameCallback((_) => _handleLocalTap());
    }
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
    widget.dependencies.pushNotifications.tappedOrderId.removeListener(
      _handlePushTap,
    );
    widget.dependencies.localNotifications.tappedOrderId.removeListener(
      _handleLocalTap,
    );
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
      db.systemState('last_sync_at'),
    ]);

    if (!mounted) return;

    final session = values[7] as SessionCredentials?;
    // Shared notifier owns de-duplication + the OS notification so the screen
    // and the background LAN long-poll cannot double-alert the same dish.
    final alertRefresh = await widget.dependencies.readyNotifier.refresh();
    final alerts = alertRefresh.alerts;
    final freshAlerts = alertRefresh.freshAlerts;

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
      _lastSyncAt = values[8] as String?;
      _session = session;
    });

    if (freshAlerts.isNotEmpty) {
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
      // Force an authoritative snapshot, not merely an incremental cursor
      // pull. This clears obsolete server orders and corrects stale tables.
      await widget.dependencies.syncCoordinator.syncAndRefresh();
    } on Object catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('Sync failed; showing cached tables. $error'),
          ),
        );
      }
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
    ).toList();

    String? separatePartyLabel;
    if (existing.isNotEmpty || table['status'] == 'occupied') {
      final choice = await showModalBottomSheet<String>(
        context: context,
        showDragHandle: true,
        builder: (sheetContext) => SafeArea(
          child: ListView(
            shrinkWrap: true,
            children: [
              const ListTile(
                title: Text('Orders at this table'),
                subtitle: Text(
                  'Different parties have independent orders and bills.',
                ),
              ),
              ...existing.map((order) {
                final label = order['service_reference']?.toString();
                final number = (order['display_number'] as num?)?.toInt() ?? 0;
                final title = number > 0 ? 'Order #$number' : 'Phone order';
                return ListTile(
                  leading: const Icon(Icons.receipt_long_outlined),
                  title: Text(label == null || label.isEmpty
                      ? title : '$title · $label'),
                  subtitle: Text(order['status']?.toString() ?? ''),
                  onTap: () => Navigator.pop(
                    sheetContext,
                    'order:${order['local_order_id']}',
                  ),
                );
              }),
              ListTile(
                key: const Key('create-separate-party'),
                leading: const Icon(Icons.group_add_outlined),
                title: const Text('New separate party'),
                subtitle: const Text('Own kitchen order, payment and receipt'),
                onTap: () => Navigator.pop(sheetContext, 'new'),
              ),
            ],
          ),
        ),
      );
      if (choice == null) return;
      if (choice.startsWith('order:')) {
        await _showOrder(choice.substring(6));
        return;
      }
      separatePartyLabel = await _askPartyLabel();
      if (separatePartyLabel == null) return;
    } else if (table['status'] != 'available') {
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
        serviceReference: separatePartyLabel,
        separateParty: separatePartyLabel != null,
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

  Future<String?> _askPartyLabel() async {
    final controller = TextEditingController();
    try {
      return await showDialog<String>(
        context: context,
        builder: (dialogContext) => StatefulBuilder(
          builder: (dialogContext, refresh) => AlertDialog(
            title: const Text('Separate guest party'),
            content: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Text(
                  'Label the guests so kitchen staff and the cashier can '
                  'identify their order. Example: Party B, Window side.',
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: controller,
                  maxLength: 80,
                  autofocus: true,
                  decoration: const InputDecoration(
                    labelText: 'Party / seat group',
                    border: OutlineInputBorder(),
                  ),
                  onChanged: (_) => refresh(() {}),
                ),
              ],
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(dialogContext),
                child: const Text('Cancel'),
              ),
              FilledButton(
                onPressed: controller.text.trim().isEmpty
                    ? null
                    : () => Navigator.pop(dialogContext, controller.text.trim()),
                child: const Text('Continue'),
              ),
            ],
          ),
        ),
      );
    } finally {
      controller.dispose();
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

  void _handlePushTap() {
    final serverId = widget.dependencies.pushNotifications.tappedOrderId.value;
    if (serverId == null || !mounted) return;
    widget.dependencies.pushNotifications.clearTappedOrder();
    unawaited(_openPushedOrder(serverId));
  }

  void _handleLocalTap() {
    // Local ready reminders carry the app's own local_order_id, so the order is
    // already in SQLite and can be opened without another sync round trip.
    final orderId = widget.dependencies.localNotifications.tappedOrderId.value;
    if (orderId == null || !mounted) return;
    widget.dependencies.localNotifications.clearTappedOrder();
    unawaited(_showOrder(orderId));
  }

  Future<void> _openPushedOrder(String serverId) async {
    // FCM carries a cloud order ID; local screens navigate by local_order_id.
    // Wait for the next authenticated pull before opening if necessary.
    var match = _orders.where(
      (order) => order['server_id']?.toString() == serverId,
    );
    if (match.isEmpty) {
      await widget.dependencies.syncCoordinator.syncNow();
      await _refresh();
      if (!mounted) return;
      match = _orders.where((order) =>
          order['server_id']?.toString() == serverId);
    }
    if (match.isNotEmpty) {
      await _showOrder(match.first['local_order_id']!.toString());
    } else if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Order is not synced yet. Reconnect and pull to view it.'),
        ),
      );
    }
  }

  Future<void> _recoverOrders() async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (context) => LocalOrderRecoveryScreen(
          database: widget.dependencies.database,
          refreshFromDesktop: widget.dependencies.syncCoordinator.syncAndRefresh,
        ),
      ),
    );
    if (mounted) await _refresh();
  }

  Future<void> _logout() async {
    await widget.dependencies.pushNotifications.signOut();
    unawaited(widget.dependencies.lanPush.stop());
    widget.dependencies.readyNotifier.reset();
    _readyAlerts = const [];
    await widget.dependencies.session.logoutLocal();
    widget.onLoggedOut();
  }

  @override
  Widget build(BuildContext context) {
    final s = widget.strings;
    final session = _session;
    final lastSync = DateTime.tryParse(_lastSyncAt ?? '')?.toLocal();
    final syncHealthy = (_syncError == null || _syncError!.isEmpty) &&
        _connectionStatus != 'offline' &&
        lastSync != null &&
        DateTime.now().difference(lastSync).inSeconds < 45;

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
              } else if (value == 'recover_orders') {
                _recoverOrders();
              }
            },
            itemBuilder: (context) => const [
              PopupMenuItem(
                value: 'recover_orders',
                child: Text('Recover / clear stuck orders'),
              ),
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
                          : !syncHealthy
                              ? Icons.cloud_off_outlined
                              : session.activeChannel == ConnectionChannel.local
                                  ? Icons.lan_outlined
                                  : Icons.cloud_outlined,
                      size: 18,
                    ),
                    label: Text(
                      _syncing
                          ? 'Syncing'
                          : !syncHealthy
                              ? 'Not synchronized · cached data'
                              : (session.connectionMode == ConnectionMode.automatic
                                      ? s.automatic + ' · '
                                      : '') +
                                  (session.activeChannel == ConnectionChannel.local
                                      ? s.connectedLocal
                                      : s.connectedCloud),
                    ),
                  ),
                if (session != null)
                  Chip(
                    avatar: const Icon(Icons.dns_outlined, size: 18),
                    label: Text(
                      'Server: ${Uri.tryParse(session.baseUrl)?.authority ?? 'unknown'}',
                    ),
                  ),
                Chip(
                  avatar: const Icon(Icons.schedule_outlined, size: 18),
                  label: Text(
                    lastSync == null
                        ? 'Last confirmed sync: Never'
                        : 'Last confirmed sync: $lastSync',
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
                color: RestaurantColors.successSoft,
                margin: EdgeInsets.zero,
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(14),
                  side: const BorderSide(
                      color: RestaurantColors.success, width: 1.5),
                ),
                child: Padding(
                  padding: const EdgeInsets.all(14),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      const Row(children: [
                        Icon(Icons.notifications_active_rounded,
                            color: RestaurantColors.success),
                        SizedBox(width: 8),
                        Expanded(child: Text('READY FOR PICKUP',
                            style: TextStyle(fontWeight: FontWeight.w900,
                                color: RestaurantColors.success))),
                      ]),
                      const SizedBox(height: 7),
                      const Text('Collect prepared food, deliver to the guest, '
                          'then mark the complete order Served in POS.',
                          style: TextStyle(color: RestaurantColors.textSecondary)),
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
                                formatTableStatus(status),
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
