import 'dart:async';

import 'package:flutter/material.dart';

import '../../app/app_strings.dart';
import '../../app/app_theme.dart';
import '../../app/dependencies.dart';
import '../../core/connection/connection_mode.dart';
import '../../core/api/api_exception.dart';
import '../../core/models/session_credentials.dart';
import '../conflicts/conflict_screen.dart';
import '../orders/menu_browser_screen.dart';
import '../orders/order_screen.dart';
import 'ready_pickup_alerts.dart';
import 'floor_board.dart';

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
    } on Object catch (error) {
      // A transient corrupt/locked SQLite snapshot should never crash
      // periodic UI refresh. Do not claim cached table states are live.
      if (mounted) {
        setState(() {
          _syncError = 'local_cache_error';
          _connectionStatus = 'sync_error';
        });
      }
      debugPrint('Restaurant floor refresh error: $error');
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
      // A real user refresh is authoritative. Background syncs only fetch
      // incrementally, which can leave old occupancy cached indefinitely.
      await widget.dependencies.syncEngine.syncNow(forceBootstrap: true);
    } on ApiException catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('Desktop sync failed (${error.code}). '
                'Check Wi-Fi and the desktop LAN address, then retry.'),
            duration: const Duration(seconds: 7),
          ),
        );
      }
    } on Object catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Desktop sync failed: $error')),
        );
      }
    } finally {
      if (mounted) {
        setState(() => _syncing = false);
        await _refresh();
      }
    }
  }

  Future<void> _connectToDesktop() async {
    final controller = TextEditingController(
      text: _session?.localBaseUrl ?? '',
    );
    final localUrl = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Connect to Restaurant Desktop'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'Your current Cloud view can differ from the desktop. '
              'Switch to the desktop LAN to see its current tables and KOTs. '
              'Your saved orders and conflicts will not be deleted.',
            ),
            const SizedBox(height: 12),
            TextField(
              controller: controller,
              keyboardType: TextInputType.url,
              autocorrect: false,
              decoration: const InputDecoration(
                labelText: 'Desktop LAN URL',
                hintText: 'http://192.168.1.11:8787',
                border: OutlineInputBorder(),
              ),
            ),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, controller.text.trim()),
            child: const Text('Connect via LAN'),
          ),
        ],
      ),
    );
    controller.dispose();
    if (localUrl == null || localUrl.isEmpty) return;

    if (!mounted) return;
    setState(() => _syncing = true);
    try {
      await widget.dependencies.session.connectToDesktop(localUrl);
      await widget.dependencies.syncEngine.syncNow(forceBootstrap: true);
      await _refresh();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Connected to Restaurant Desktop LAN. Tables refreshed.')),
        );
      }
    } on ApiException catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Desktop LAN connection failed: ${error.message}')),
        );
      }
    } on Object catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Desktop LAN connection failed: $error')),
        );
      }
    } finally {
      if (mounted) setState(() => _syncing = false);
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
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: const Text(
              'This table is not available or belongs to another waiter. '
              'Refresh the floor before creating a new order.',
            ),
            action: SnackBarAction(label: 'Refresh', onPressed: _sync),
          ),
        );
      }
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

  Future<void> _testReadySound() async {
    final result = await widget.dependencies.localNotifications.testReadySound();
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(result
            ? 'Test notification sent. Check the iPhone sound and notification settings.'
            : 'Notifications unavailable. Enable Restaurant alerts and sounds in iPhone Settings.'),
      ),
    );
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
              } else if (value == 'test_ready_sound') {
                unawaited(_testReadySound());
              }
            },
            itemBuilder: (context) => const [
              PopupMenuItem(
                value: 'test_ready_sound',
                child: ListTile(
                  leading: Icon(Icons.notifications_active_outlined),
                  title: Text('Test kitchen-ready sound'),
                  contentPadding: EdgeInsets.zero,
                ),
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
                          : _connectionStatus == 'offline' ||
                                _connectionStatus == 'sync_error'
                              ? Icons.cloud_off_outlined
                              : session.activeChannel == ConnectionChannel.local
                                  ? Icons.lan_outlined
                                  : Icons.cloud_outlined,
                      size: 18,
                    ),
                    label: Text(
                      _syncing
                          ? 'Syncing'
                          : _connectionStatus == 'offline' ||
                                _connectionStatus == 'sync_error'
                              ? 'Needs sync · cached floor'
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
            if (session != null &&
                session.activeChannel == ConnectionChannel.local) ...[
              const SizedBox(height: 12),
              OutlinedButton.icon(
                onPressed: _syncing ? null : _sync,
                icon: const Icon(Icons.refresh_outlined),
                label: const Text('Reconnect and refresh desktop tables'),
              ),
              if (_connectionStatus == 'offline' ||
                  _connectionStatus == 'sync_error' ||
                  (_syncError?.isNotEmpty ?? false)) ...[
                const SizedBox(height: 8),
                Text(
                  'Offline — table statuses below are cached, not confirmed '
                  'by Restaurant Desktop. The configured LAN server is '
                  '${session.localBaseUrl ?? session.baseUrl}. '
                  'Keep both devices on the same Wi-Fi and retry.',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ],
            ],
            if (session != null &&
                session.activeChannel == ConnectionChannel.cloud) ...[
              const SizedBox(height: 12),
              OutlinedButton.icon(
                onPressed: _syncing ? null : _connectToDesktop,
                icon: const Icon(Icons.lan_outlined),
                label: const Text('Connect to Desktop LAN'),
              ),
              const SizedBox(height: 6),
              const Text(
                'Cloud may show tables that differ from your local Restaurant Desktop.',
                style: TextStyle(fontSize: 12),
              ),
            ],
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
            FloorBoard(
              key: const Key('waiter-floor-board'),
              tables: _tables,
              orders: _orders,
              readyAlerts: _readyAlerts,
              offline: _connectionStatus == 'offline' ||
                  _connectionStatus == 'sync_error' ||
                  (_syncError?.isNotEmpty ?? false),
              onTableTap: (table) => unawaited(_openTable(table)),
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
