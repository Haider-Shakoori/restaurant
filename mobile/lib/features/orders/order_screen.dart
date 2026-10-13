import 'dart:convert';

import 'package:flutter/material.dart';

import '../../app/app_strings.dart';
import '../../app/app_theme.dart';
import '../../app/dependencies.dart';
import '../../core/format/restaurant_formats.dart';
import '../../core/models/session_credentials.dart';
import 'menu_category_top_bar.dart';
import 'menu_item_photo.dart';

/// Mirrors the desktop order label: the short `#n` staff number leads, followed
/// by the service reference (table / takeaway ref) when one is present.
String _orderLabel(Map<String, Object?> order, {required String fallback}) {
  final reference = order['service_reference']?.toString().trim() ?? '';
  final body = reference.isNotEmpty ? reference : fallback;
  final number = formatOrderNumber(order['display_number']);
  if (number.isEmpty) {
    return body;
  }
  return body.isEmpty ? number : '$number · $body';
}

class OrderScreen extends StatefulWidget {
  const OrderScreen({
    required this.dependencies,
    required this.strings,
    required this.localOrderId,
    super.key,
  });

  final AppDependencies dependencies;
  final AppStrings strings;
  final String localOrderId;

  @override
  State<OrderScreen> createState() => _OrderScreenState();
}

class _OrderScreenState extends State<OrderScreen> {
  Map<String, Object?>? _order;
  SessionCredentials? _imageSession;
  List<Map<String, Object?>> _items = const [];
  List<Map<String, Object?>> _categories = const [];
  Map<String, List<Map<String, Object?>>> _menu = const {};
  Map<String, Object?> _restaurantSettings = const {};
  String? _selectedCategoryId;
  String _query = '';
  bool _working = false;
  bool _hasUnsent = false;
  int _rightTab = 0;
  int _phoneTab = 0;

  @override
  void initState() {
    super.initState();
    _refresh();
  }

  Future<void> _refresh() async {
    final db = widget.dependencies.database;
    final imageSession = await widget.dependencies.credentials.readSession();
    final order = await db.order(widget.localOrderId);
    final items = await db.orderItems(widget.localOrderId);
    final categories = await db.menuCategories();
    final hasUnsent = await db.hasUnsentItems(widget.localOrderId);
    final restaurantSettings = await db.restaurantSettings();
    final menu = <String, List<Map<String, Object?>>>{};

    for (final category in categories) {
      final id = category['id']!.toString();
      menu[id] = await db.menuItems(id);
    }

    if (!mounted) {
      return;
    }

    setState(() {
      _order = order;
      _imageSession = imageSession;
      _items = items;
      _categories = categories;
      _menu = menu;
      _hasUnsent = hasUnsent;
      _restaurantSettings = restaurantSettings;
      _selectedCategoryId ??= categories.isEmpty
          ? null
          : categories.first['id']!.toString();
    });
  }

  Future<void> _addItem(Map<String, Object?> item) async {
    if (_working) return;

    final rawGroups = item['modifier_groups_json']?.toString();
    final groups = rawGroups == null || rawGroups.isEmpty
        ? const <Object?>[]
        : (jsonDecode(rawGroups) as List<Object?>);

    final selected = await showDialog<_OrderItemOptions>(
      context: context,
      builder: (context) => _ItemOptionsDialog(
        itemName: item['name']!.toString(),
        groups: groups,
        coursesEnabled: _restaurantSettings['courses_enabled'] == true,
      ),
    );

    if (selected == null) return;
    final options = selected;

    setState(() => _working = true);

    try {
      await widget.dependencies.orders.addItem(
        localOrderId: widget.localOrderId,
        menuItem: item,
        notes: options.notes,
        seatNumber: options.seatNumber,
        courseNumber: options.courseNumber,
        courseName: options.courseName,
        holdForCourse: options.holdForCourse,
        modifiers: options.modifiers,
        allergyInstructions: options.allergyInstructions,
        kitchenInstructions: options.kitchenInstructions,
      );
      await _refresh();
    } on Object catch (error) {
      _showError(error);
    } finally {
      if (mounted) setState(() => _working = false);
    }
  }

  Future<void> _fireCourse(int courseNumber) async {
    if (_working) return;
    setState(() => _working = true);

    try {
      await widget.dependencies.orders.fireCourse(
        localOrderId: widget.localOrderId,
        courseNumber: courseNumber,
      );
      await widget.dependencies.syncCoordinator.syncNow();
      await _refresh();

      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Course $courseNumber sent to Kitchen.')),
        );
      }
    } on Object catch (error) {
      _showError(error);
    } finally {
      if (mounted) setState(() => _working = false);
    }
  }

  Future<String?> _askReason(String title) async {
    final controller = TextEditingController();

    final reason = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(title),
        content: TextField(
          controller: controller,
          autofocus: true,
          maxLines: 3,
          decoration: const InputDecoration(
            labelText: 'Reason',
            hintText: 'Enter the operational reason',
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () {
              final value = controller.text.trim();
              if (value.isNotEmpty) {
                Navigator.of(context).pop(value);
              }
            },
            child: const Text('Continue'),
          ),
        ],
      ),
    );

    controller.dispose();
    return reason;
  }

  Future<void> _runProductionAction(
    String action,
    Map<String, Object?> item,
  ) async {
    final id = item['id']?.toString();
    if (id == null || id.isEmpty || _working) return;

    final reason = await _askReason(
      switch (action) {
        'void' => 'Void kitchen item',
        'refire' => 'Re-fire kitchen item',
        _ => 'Recall ready item',
      },
    );

    if (reason == null) return;

    setState(() => _working = true);

    try {
      switch (action) {
        case 'void':
          await widget.dependencies.orders.voidProduction(
            kitchenTicketItemId: id,
            reason: reason,
          );
          break;
        case 'refire':
          await widget.dependencies.orders.refireProduction(
            kitchenTicketItemId: id,
            reason: reason,
          );
          break;
        case 'recall':
          await widget.dependencies.orders.recallProduction(
            kitchenTicketItemId: id,
            reason: reason,
          );
          break;
      }

      await widget.dependencies.syncCoordinator.syncNow();
      await _refresh();
    } on Object catch (error) {
      _showError(error);
    } finally {
      if (mounted) setState(() => _working = false);
    }
  }

  Future<void> _showKotRound(Map<String, Object?> round) async {
    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (context) {
        final tickets = round['tickets'] as List<Object?>? ?? const [];

        return SafeArea(
          child: FractionallySizedBox(
            heightFactor: 0.78,
            child: ListView(
              padding: const EdgeInsets.fromLTRB(16, 0, 16, 24),
              children: [
                Text(
                  (round['kot_number']?.toString() ?? 'KOT') +
                      ' · Round ' +
                      ((round['sequence'] as num?)?.toInt() ?? 0).toString(),
                  style: Theme.of(context).textTheme.titleLarge?.copyWith(
                    fontWeight: FontWeight.w900,
                  ),
                ),
                const SizedBox(height: 12),
                ...tickets.whereType<Map<Object?, Object?>>().expand((rawTicket) {
                  final ticket = Map<String, Object?>.from(rawTicket);
                  final station = ticket['station'] is Map<Object?, Object?>
                      ? Map<String, Object?>.from(
                          ticket['station']! as Map<Object?, Object?>,
                        )
                      : const <String, Object?>{};
                  final items =
                      ticket['items'] as List<Object?>? ?? const <Object?>[];

                  return <Widget>[
                    Padding(
                      padding: const EdgeInsets.only(top: 8, bottom: 6),
                      child: Text(
                        station['name']?.toString() ?? 'Kitchen',
                        style: const TextStyle(fontWeight: FontWeight.w800),
                      ),
                    ),
                    ...items.whereType<Map<Object?, Object?>>().map((rawItem) {
                      final item = Map<String, Object?>.from(rawItem);
                      final status = item['status']?.toString() ?? 'unknown';

                      return Card(
                        child: Padding(
                          padding: const EdgeInsets.all(12),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Row(
                                children: [
                                  Expanded(
                                    child: Text(
                                      item['item_name']?.toString() ?? 'Item',
                                      style: const TextStyle(
                                        fontWeight: FontWeight.w800,
                                      ),
                                    ),
                                  ),
                                  Text(
                                    'x' + (item['quantity'] ?? 1).toString(),
                                  ),
                                  const SizedBox(width: 8),
                                  Chip(label: Text(formatStatus(status))),
                                ],
                              ),
                              if (item['production_reason'] != null)
                                Text(
                                  item['production_reason'].toString(),
                                  style: const TextStyle(fontSize: 12),
                                ),
                              const SizedBox(height: 8),
                              Wrap(
                                spacing: 8,
                                runSpacing: 8,
                                children: [
                                  if (!const {'voided', 'cancelled', 'completed'}
                                      .contains(status))
                                    OutlinedButton(
                                      onPressed: _working
                                          ? null
                                          : () {
                                              Navigator.of(context).pop();
                                              _runProductionAction('void', item);
                                            },
                                      child: const Text('Void'),
                                    ),
                                  if (status == 'ready')
                                    OutlinedButton(
                                      onPressed: _working
                                          ? null
                                          : () {
                                              Navigator.of(context).pop();
                                              _runProductionAction(
                                                'recall',
                                                item,
                                              );
                                            },
                                      child: const Text('Recall'),
                                    ),
                                  if (const {'ready', 'completed', 'voided'}
                                      .contains(status))
                                    FilledButton.tonal(
                                      onPressed: _working
                                          ? null
                                          : () {
                                              Navigator.of(context).pop();
                                              _runProductionAction(
                                                'refire',
                                                item,
                                              );
                                            },
                                      child: const Text('Re-fire'),
                                    ),
                                ],
                              ),
                            ],
                          ),
                        ),
                      );
                    }),
                  ];
                }),
              ],
            ),
          ),
        );
      },
    );
  }

  Future<void> _transferTable() async {
    if (_working || _order?['service_type']?.toString() != 'dine_in') return;

    final tables = await widget.dependencies.database.tables();
    final currentTableId = _order?['table_id']?.toString();
    final available = tables
        .where(
          (table) =>
              table['id']?.toString() != currentTableId &&
              table['status']?.toString() == 'available' &&
              table['branch_id']?.toString() == _order?['branch_id']?.toString(),
        )
        .toList(growable: false);

    if (!mounted) return;

    if (available.isEmpty) {
      _showError('No available table is available in this branch.');
      return;
    }

    final targetId = await showDialog<String>(
      context: context,
      builder: (context) {
        var selected = available.first['id']!.toString();

        return StatefulBuilder(
          builder: (context, setDialogState) => AlertDialog(
            title: const Text('Transfer table'),
            content: DropdownButtonFormField<String>(
              initialValue: selected,
              decoration: const InputDecoration(labelText: 'Target table'),
              items: available
                  .map(
                    (table) => DropdownMenuItem<String>(
                      value: table['id']!.toString(),
                      child: Text(
                        (table['area_name']?.toString() ?? '') +
                            ' · ' +
                            (table['name']?.toString() ??
                                table['code']!.toString()),
                      ),
                    ),
                  )
                  .toList(),
              onChanged: (value) {
                if (value != null) {
                  setDialogState(() => selected = value);
                }
              },
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(context).pop(),
                child: const Text('Cancel'),
              ),
              FilledButton(
                onPressed: () => Navigator.of(context).pop(selected),
                child: const Text('Transfer'),
              ),
            ],
          ),
        );
      },
    );

    if (targetId == null) return;

    setState(() => _working = true);
    try {
      await widget.dependencies.orders.transferTable(
        localOrderId: widget.localOrderId,
        targetTableId: targetId,
      );
      await widget.dependencies.syncCoordinator.syncNow();
      await _refresh();
    } on Object catch (error) {
      _showError(error);
    } finally {
      if (mounted) setState(() => _working = false);
    }
  }

  Future<void> _moveItem() async {
    if (_working) return;

    final unsent = _items
        .where((item) {
          final quantity = (item['quantity'] as num?)?.toInt() ?? 0;
          final dispatched =
              (item['dispatched_quantity'] as num?)?.toInt() ?? 0;
          return quantity > dispatched;
        })
        .toList(growable: false);

    final orders = await widget.dependencies.database.activeOrders();
    final targets = orders
        .where(
          (order) =>
              order['local_order_id']?.toString() != widget.localOrderId &&
              order['branch_id']?.toString() == _order?['branch_id']?.toString(),
        )
        .toList(growable: false);

    if (!mounted) return;

    if (unsent.isEmpty || targets.isEmpty) {
      _showError('An unsent item and another open order are required.');
      return;
    }

    final result = await showDialog<_MoveItemChoice>(
      context: context,
      builder: (context) => _MoveItemDialog(
        items: unsent,
        targets: targets,
      ),
    );

    if (result == null) return;

    setState(() => _working = true);
    try {
      await widget.dependencies.orders.moveUnsentItem(
        sourceLocalOrderId: widget.localOrderId,
        targetLocalOrderId: result.targetOrderId,
        localLineId: result.localLineId,
        quantity: result.quantity,
      );
      await widget.dependencies.syncCoordinator.syncNow();
      await _refresh();
    } on Object catch (error) {
      _showError(error);
    } finally {
      if (mounted) setState(() => _working = false);
    }
  }

  Future<void> _mergeOrder() async {
    if (_working) return;

    final hasDispatched = _items.any(
      (item) => ((item['dispatched_quantity'] as num?)?.toInt() ?? 0) > 0,
    );

    if (hasDispatched) {
      _showError(
        'This order already has kitchen production. Move only unsent items instead.',
      );
      return;
    }

    final orders = await widget.dependencies.database.activeOrders();
    final targets = orders
        .where(
          (order) =>
              order['local_order_id']?.toString() != widget.localOrderId &&
              order['branch_id']?.toString() == _order?['branch_id']?.toString(),
        )
        .toList(growable: false);

    if (!mounted) return;

    if (targets.isEmpty) {
      _showError('No compatible open target order is available.');
      return;
    }

    final targetId = await showDialog<String>(
      context: context,
      builder: (context) {
        var selected = targets.first['local_order_id']!.toString();

        return StatefulBuilder(
          builder: (context, setDialogState) => AlertDialog(
            title: const Text('Merge order'),
            content: DropdownButtonFormField<String>(
              initialValue: selected,
              decoration: const InputDecoration(labelText: 'Target order'),
              items: targets
                  .map(
                    (order) => DropdownMenuItem<String>(
                      value: order['local_order_id']!.toString(),
                      child: Text(
                        _orderLabel(
                          order,
                          fallback:
                              'Order ${order['local_order_id']!.toString().substring(0, 8)}',
                        ),
                      ),
                    ),
                  )
                  .toList(),
              onChanged: (value) {
                if (value != null) {
                  setDialogState(() => selected = value);
                }
              },
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(context).pop(),
                child: const Text('Cancel'),
              ),
              FilledButton(
                onPressed: () => Navigator.of(context).pop(selected),
                child: const Text('Merge'),
              ),
            ],
          ),
        );
      },
    );

    if (targetId == null) return;

    setState(() => _working = true);
    try {
      await widget.dependencies.orders.mergeOrders(
        sourceLocalOrderId: widget.localOrderId,
        targetLocalOrderId: targetId,
      );
      await widget.dependencies.syncCoordinator.syncNow();

      if (mounted) {
        Navigator.of(context).maybePop();
      }
    } on Object catch (error) {
      _showError(error);
    } finally {
      if (mounted) setState(() => _working = false);
    }
  }

  Future<void> _showOrderOperations() async {
    final action = await showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: Wrap(
          children: [
            if (_order?['service_type']?.toString() == 'dine_in')
              ListTile(
                leading: const Icon(Icons.swap_horiz_rounded),
                title: const Text('Transfer table'),
                onTap: () => Navigator.of(context).pop('transfer'),
              ),
            ListTile(
              leading: const Icon(Icons.call_split_rounded),
              title: const Text('Move / split unsent item'),
              subtitle: const Text(
                'Move only unsent quantity to another open order.',
              ),
              onTap: () => Navigator.of(context).pop('move'),
            ),
            ListTile(
              leading: const Icon(Icons.merge_type_rounded),
              title: const Text('Merge draft / unsent order'),
              subtitle: const Text(
                'Full merge is blocked after kitchen production starts.',
              ),
              onTap: () => Navigator.of(context).pop('merge'),
            ),
          ],
        ),
      ),
    );

    switch (action) {
      case 'transfer':
        await _transferTable();
      case 'move':
        await _moveItem();
      case 'merge':
        await _mergeOrder();
    }
  }

  Future<void> _submit() async {
    if (_working) return;
    setState(() => _working = true);

    try {
      // Sending creates durable offline mutations first. Do not tell staff
      // the kitchen received the order until the server acknowledges them.
      await widget.dependencies.orders.submit(widget.localOrderId);
      Object? syncFailure;
      try {
        await widget.dependencies.syncCoordinator.syncNowWithFeedback();
      } on Object catch (error) {
        syncFailure = error;
      }
      await _refresh();

      final current = await widget.dependencies.database.order(widget.localOrderId);
      final awaitingServer = current?['status']?.toString() ==
          'submitted_pending_sync';
      final conflicts = await widget.dependencies.database.conflictCount();

      if (mounted) {
        final message = syncFailure != null
            ? 'Saved on phone, NOT delivered to kitchen. Sync failed: $syncFailure'
            : conflicts > 0
                ? 'Not confirmed: $conflicts synchronization conflicts. Review conflicts on Tables.'
                : awaitingServer
                    ? 'Saved on phone, still queued. Check the desktop connection and retry sync.'
                    : 'Kitchen order confirmed by server.';
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(message), duration: const Duration(seconds: 7)),
        );
      }
    } on Object catch (error) {
      _showError(error);
    } finally {
      if (mounted) setState(() => _working = false);
    }
  }

  void _showError(Object error) {
    if (!mounted) return;
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(error.toString())));
  }

  // The waiter app sends the initial kitchen ticket only. Existing KOT
  // history remains visible, but creating additional KOT rounds is disabled.
  bool get _editable =>
      _kotRounds.isEmpty &&
      _order?['status']?.toString() == 'draft';

  List<int> get _heldCourses {
    final courses = _items
        .where((item) => item['course_state']?.toString() == 'held')
        .map((item) => (item['course_number'] as num?)?.toInt())
        .whereType<int>()
        .toSet()
        .toList()
      ..sort();

    return courses;
  }

  List<Map<String, Object?>> get _kotRounds {
    final raw = _order?['kot_rounds_json']?.toString();

    if (raw == null || raw.isEmpty) {
      return const [];
    }

    final decoded = jsonDecode(raw) as List<Object?>;

    return decoded
        .whereType<Map<Object?, Object?>>()
        .map((round) => Map<String, Object?>.from(round))
        .toList(growable: false);
  }

  bool get _allProductionReady {
    final rounds = _kotRounds;
    if (rounds.isEmpty) return false;

    final statuses = <String>[];

    for (final round in rounds) {
      for (final rawTicket in round['tickets'] as List<Object?>? ?? const []) {
        if (rawTicket is! Map<Object?, Object?>) continue;
        final ticket = Map<String, Object?>.from(rawTicket);
        for (final rawItem in ticket['items'] as List<Object?>? ?? const []) {
          if (rawItem is! Map<Object?, Object?>) continue;
          final item = Map<String, Object?>.from(rawItem);
          final status = item['status']?.toString();
          if (status == null || status == 'voided' || status == 'cancelled') {
            continue;
          }
          statuses.add(status);
        }
      }
    }

    return statuses.isNotEmpty &&
        statuses.every((status) => status == 'ready' || status == 'completed');
  }

  List<Map<String, Object?>> get _visibleItems {
    Iterable<Map<String, Object?>> result = _selectedCategoryId == null
        ? _menu.values.expand((items) => items)
        : (_menu[_selectedCategoryId] ?? const []);

    final query = _query.trim().toLowerCase();
    if (query.isEmpty) return result.toList(growable: false);

    return result
        .where(
          (item) =>
              item['name']!.toString().toLowerCase().contains(query) ||
              (item['description']?.toString().toLowerCase().contains(query) ??
                  false),
        )
        .toList(growable: false);
  }

  @override
  Widget build(BuildContext context) {
    final order = _order;
    if (order == null) {
      return const Scaffold(body: Center(child: CircularProgressIndicator()));
    }

    return Scaffold(
      backgroundColor: RestaurantColors.appBackground,
      body: SafeArea(
        child: LayoutBuilder(
          builder: (context, constraints) => Column(
            children: [
              _header(order),
              Expanded(
                child: constraints.maxWidth >= 900
                    ? _tabletBody(context, order)
                    : _phoneBody(context, order),
              ),
              _bottomNav(),
            ],
          ),
        ),
      ),
    );
  }

  Widget _header(Map<String, Object?> order) {
    // The full desktop-style status row does not fit narrow waiter phones.
    if (MediaQuery.sizeOf(context).width < 700) {
      return Container(
        height: 60,
        padding: const EdgeInsets.symmetric(horizontal: 8),
        decoration: const BoxDecoration(
          color: RestaurantColors.surface,
          border: Border(
            bottom: BorderSide(color: RestaurantColors.border),
          ),
        ),
        child: Row(
          children: [
            IconButton(
              tooltip: 'Back to tables',
              onPressed: () => Navigator.of(context).maybePop(),
              color: RestaurantColors.textPrimary,
              icon: const Icon(Icons.arrow_back_rounded),
            ),
            const SizedBox(width: 4),
            const Expanded(
              child: Text(
                'Restaurant POS',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                    color: RestaurantColors.textPrimary,
                    fontWeight: FontWeight.w800),
              ),
            ),
            Text(
              '${_kotRounds.length} KOT',
              style: const TextStyle(
                color: RestaurantColors.brandHover,
                fontSize: 12,
                fontWeight: FontWeight.w800,
              ),
            ),
            IconButton(
              tooltip: 'Order operations',
              onPressed: _working ? null : _showOrderOperations,
              color: RestaurantColors.textSecondary,
              icon: const Icon(Icons.more_vert_rounded),
            ),
          ],
        ),
      );
    }

    return Container(
      height: 68,
      padding: const EdgeInsets.symmetric(horizontal: 16),
      decoration: const BoxDecoration(
        color: RestaurantColors.surface,
        border: Border(
          bottom: BorderSide(color: RestaurantColors.border),
        ),
      ),
      child: Row(
        children: [
          IconButton(
            onPressed: () => Navigator.of(context).maybePop(),
            color: RestaurantColors.textPrimary,
            icon: const Icon(Icons.arrow_back_rounded),
          ),
          Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(
              border: Border.all(color: RestaurantColors.brand),
              borderRadius: BorderRadius.circular(11),
            ),
            child: const Icon(
              Icons.restaurant_rounded,
              color: RestaurantColors.brand,
            ),
          ),
          const SizedBox(width: 10),
          const Column(
            mainAxisAlignment: MainAxisAlignment.center,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'Business OS',
                style: TextStyle(
                  color: RestaurantColors.textPrimary,
                  fontWeight: FontWeight.w800,
                ),
              ),
              Text(
                'Restaurant',
                style: TextStyle(
                    color: RestaurantColors.brandHover, fontSize: 11),
              ),
            ],
          ),
          const SizedBox(width: 20),
          const Icon(Icons.circle, size: 9, color: RestaurantColors.success),
          const SizedBox(width: 6),
          const Text(
            'Connected',
            style:
                TextStyle(color: RestaurantColors.textSecondary, fontSize: 12),
          ),
          const Spacer(),
          if (_kotRounds.isNotEmpty) ...[
            Container(
              margin: const EdgeInsetsDirectional.only(end: 8),
              padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 7),
              decoration: BoxDecoration(
                color: RestaurantColors.brandSoft,
                borderRadius: BorderRadius.circular(RestaurantRadii.chip),
              ),
              child: Text(
                _kotRounds.length == 1
                    ? '1 KOT round'
                    : '${_kotRounds.length} KOT rounds',
                style: const TextStyle(
                  color: RestaurantColors.brandHover,
                  fontSize: 11,
                  fontWeight: FontWeight.w800,
                ),
              ),
            ),
            if (_allProductionReady)
              Container(
                margin: const EdgeInsetsDirectional.only(end: 8),
                padding: const EdgeInsets.symmetric(
                  horizontal: 10,
                  vertical: 7,
                ),
                decoration: BoxDecoration(
                  color: RestaurantColors.successSoft,
                  borderRadius: BorderRadius.circular(RestaurantRadii.chip),
                ),
                child: const Text(
                  'READY',
                  style: TextStyle(
                    color: RestaurantColors.success,
                    fontSize: 11,
                    fontWeight: FontWeight.w900,
                  ),
                ),
              ),
          ],
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 7),
            decoration: BoxDecoration(
              color: RestaurantColors.gridHeader,
              borderRadius: BorderRadius.circular(RestaurantRadii.chip),
            ),
            child: Text(
              formatStatus(order['status']),
              style: const TextStyle(
                color: RestaurantColors.textSecondary,
                fontSize: 11,
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
          const SizedBox(width: 6),
          IconButton(
            tooltip: 'Order operations',
            onPressed: _working ? null : _showOrderOperations,
            color: RestaurantColors.textSecondary,
            icon: const Icon(Icons.more_vert_rounded),
          ),
        ],
      ),
    );
  }

  Widget _tabletBody(BuildContext context, Map<String, Object?> order) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Expanded(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(12, 12, 8, 12),
            child: Column(
              children: [
                _categoryBar(),
                const SizedBox(height: 12),
                _searchBar(),
                const SizedBox(height: 12),
                Expanded(child: _menuGrid()),
              ],
            ),
          ),
        ),
        SizedBox(
          width: 340,
          child: Padding(
            padding: const EdgeInsets.fromLTRB(8, 12, 12, 12),
            child: _orderPanel(order),
          ),
        ),
      ],
    );
  }

  Widget _phoneBody(BuildContext context, Map<String, Object?> order) {
    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(12, 8, 12, 4),
          child: Row(
            children: [
              Expanded(
                child: ChoiceChip(
                  key: const Key('mobile-pos-menu-tab'),
                  label: const SizedBox(
                    width: double.infinity,
                    child: Center(child: Text('Menu')),
                  ),
                  selected: _phoneTab == 0,
                  onSelected: (_) => setState(() => _phoneTab = 0),
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: ChoiceChip(
                  key: const Key('mobile-pos-order-tab'),
                  label: SizedBox(
                    width: double.infinity,
                    child: Center(child: Text('Order (${_items.length})')),
                  ),
                  selected: _phoneTab == 1,
                  onSelected: (_) => setState(() => _phoneTab = 1),
                ),
              ),
            ],
          ),
        ),
        Expanded(
          child: _phoneTab == 0
              ? Column(
                  children: [
                    _categoryBar(),
                    Padding(
                      padding: const EdgeInsets.fromLTRB(12, 8, 12, 4),
                      child: _searchBar(),
                    ),
                    Expanded(
                      child: _menuGrid(phone: true),
                    ),
                  ],
                )
              : Padding(
                  padding: const EdgeInsets.all(12),
                  child: _orderPanel(order),
                ),
        ),
      ],
    );
  }

  Widget _categoryBar() {
    return MenuCategoryTopBar(
      categories: _categories,
      selectedCategoryId: _selectedCategoryId,
      onSelected: (id) => setState(() => _selectedCategoryId = id),
    );
  }

  Widget _searchBar() {
    return TextField(
      onChanged: (value) => setState(() => _query = value),
      decoration: InputDecoration(
        hintText: 'Search menu items...',
        prefixIcon: const Icon(Icons.search_rounded),
        filled: true,
        fillColor: RestaurantColors.surface,
        contentPadding: EdgeInsets.zero,
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(RestaurantRadii.input),
          borderSide: BorderSide.none,
        ),
      ),
    );
  }

  Widget _menuGrid({bool phone = false}) {
    final items = _visibleItems;

    if (items.isEmpty) {
      return const Center(
        child: Padding(
          padding: EdgeInsets.all(32),
          child: Text('No menu items found.'),
        ),
      );
    }

    return GridView.builder(
      // Both panes own their own viewport; the photo grid must scroll itself.
      shrinkWrap: false,
      itemCount: items.length,
      gridDelegate: SliverGridDelegateWithMaxCrossAxisExtent(
        maxCrossAxisExtent: phone ? 220 : 250,
        mainAxisExtent: phone ? 225 : 240,
        crossAxisSpacing: 12,
        mainAxisSpacing: 12,
      ),
      itemBuilder: (context, index) {
        final item = items[index];

        return Material(
          color: RestaurantColors.surface,
          clipBehavior: Clip.antiAlias,
          borderRadius: BorderRadius.circular(RestaurantRadii.card),
          child: InkWell(
            onTap: _editable && !_working ? () => _addItem(item) : null,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: SizedBox(
                    width: double.infinity,
                    child: MenuItemPhoto(
                      imageUrl: item['image_url']?.toString(),
                      credentials: _imageSession,
                    ),
                  ),
                ),
                Padding(
                  padding: const EdgeInsets.fromLTRB(12, 9, 8, 9),
                  child: Row(
                    children: [
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              item['name']!.toString(),
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: const TextStyle(
                                fontWeight: FontWeight.w800,
                              ),
                            ),
                            const SizedBox(height: 3),
                            Text(
                              formatMoney(item['price'], decimals: 0),
                              style: const TextStyle(
                                color: RestaurantColors.brandHover,
                                fontWeight: FontWeight.w700,
                              ),
                            ),
                          ],
                        ),
                      ),
                      IconButton.filled(
                        onPressed: _editable && !_working
                            ? () => _addItem(item)
                            : null,
                        style: IconButton.styleFrom(
                          backgroundColor: RestaurantColors.brand,
                          foregroundColor: RestaurantColors.onBrand,
                        ),
                        icon: const Icon(Icons.add_rounded),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        );
      },
    );
  }

  Widget _orderPanel(Map<String, Object?> order) {
    return Container(
      decoration: BoxDecoration(
        color: RestaurantColors.surface,
        borderRadius: BorderRadius.circular(RestaurantRadii.card),
        boxShadow: const [
          BoxShadow(
            blurRadius: 24,
            color: Color(0x12000000),
            offset: Offset(0, 8),
          ),
        ],
      ),
      clipBehavior: Clip.antiAlias,
      child: Column(
        children: [
          Container(
            padding: const EdgeInsets.all(16),
            color: RestaurantColors.brand,
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    _orderLabel(order, fallback: 'Current Order'),
                    style: const TextStyle(
                      color: RestaurantColors.onBrand,
                      fontWeight: FontWeight.w800,
                      fontSize: 16,
                    ),
                  ),
                ),
                Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 9,
                    vertical: 6,
                  ),
                  decoration: BoxDecoration(
                    color: RestaurantColors.error,
                    borderRadius: BorderRadius.circular(9),
                  ),
                  child: Text(
                    _serviceLabel(order),
                    style: const TextStyle(
                      color: Colors.white,
                      fontWeight: FontWeight.w700,
                      fontSize: 11,
                    ),
                  ),
                ),
              ],
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(12, 10, 12, 4),
            child: SegmentedButton<int>(
              key: const Key('mobile-pos-right-tabs'),
              segments: [
                ButtonSegment<int>(
                  value: 0,
                  label: Text('Items (${_items.length})'),
                  icon: const Icon(Icons.shopping_cart_outlined, size: 18),
                ),
                ButtonSegment<int>(
                  value: 1,
                  label: Text('KOT (${_kotRounds.length})'),
                  icon: const Icon(Icons.receipt_long_outlined, size: 18),
                ),
              ],
              selected: {_rightTab},
              onSelectionChanged: (values) =>
                  setState(() => _rightTab = values.first),
              showSelectedIcon: false,
            ),
          ),
          Expanded(
            child: _rightTab == 0
                ? _orderItemsScroll()
                : _kitchenRoundsScroll(),
          ),
          const Divider(height: 1),
          Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              children: [
                _totalRow(
                  'Subtotal',
                  formatMoney(order['subtotal']),
                ),
                const SizedBox(height: 8),
                _totalRow(
                  'Total',
                  formatMoney(order['total']),
                  emphasized: true,
                ),
                const SizedBox(height: 14),
                SizedBox(
                  width: double.infinity,
                  height: 50,
                  child: FilledButton.icon(
                    onPressed: _editable && _hasUnsent && !_working
                        ? _submit
                        : null,
                    style: FilledButton.styleFrom(
                      backgroundColor: RestaurantColors.brand,
                      foregroundColor: RestaurantColors.onBrand,
                      shape: RoundedRectangleBorder(
                        borderRadius:
                            BorderRadius.circular(RestaurantRadii.button),
                      ),
                    ),
                    icon: const Icon(Icons.send_rounded),
                    label: Text(
                      _working ? 'Sending...' : 'Send to Kitchen',
                      style: const TextStyle(fontWeight: FontWeight.w800),
                    ),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _orderItemsScroll() {
    if (_items.isEmpty) {
      return const Center(child: Text('Add menu items to start the order.'));
    }
    return ListView.separated(
      key: const Key('mobile-pos-order-items-scroll'),
      padding: const EdgeInsets.all(12),
      itemCount: _items.length,
      separatorBuilder: (context, index) => const Divider(height: 18),
      itemBuilder: (context, index) {
        final item = _items[index];
        final allergy = item['allergy_instructions']?.toString();
        return Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Icon(Icons.restaurant_menu_rounded, size: 25),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(item['item_name']!.toString(),
                      style: const TextStyle(fontWeight: FontWeight.w800)),
                  Text('x${item['quantity']} · ${formatMoney(item['line_total'])}',
                      style: const TextStyle(color: RestaurantColors.textSecondary)),
                  if (allergy != null && allergy.isNotEmpty)
                    Text('ALLERGY: $allergy',
                        style: const TextStyle(
                          color: Colors.red,
                          fontWeight: FontWeight.w800,
                          fontSize: 10,
                        )),
                ],
              ),
            ),
          ],
        );
      },
    );
  }

  Widget _kitchenRoundsScroll() {
    return ListView(
      key: const Key('mobile-pos-kot-scroll'),
      padding: const EdgeInsets.all(12),
      children: [
        if (_kotRounds.isEmpty)
          const Padding(
            padding: EdgeInsets.all(12),
            child: Text('No KOT rounds have been sent yet.'),
          ),
        ..._kotRounds.map((round) {
          final sequence = (round['sequence'] as num?)?.toInt() ?? 0;
          final number = round['kot_number']?.toString() ?? 'KOT';
          final priority = round['priority']?.toString() ?? 'normal';
          return ListTile(
            leading: Icon(
              priority == 'rush'
                  ? Icons.bolt_rounded
                  : Icons.receipt_long_rounded,
              color: priority == 'rush' ? Colors.red : null,
            ),
            title: Text('Round $sequence · $number'),
            trailing: const Icon(Icons.chevron_right_rounded),
            onTap: () => _showKotRound(round),
          );
        }),
        if (_restaurantSettings['courses_enabled'] == true &&
            _heldCourses.isNotEmpty) ...[
          const Divider(),
          const Text('Held courses',
              style: TextStyle(fontWeight: FontWeight.w800)),
          const SizedBox(height: 8),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: _heldCourses.map((course) => FilledButton.tonalIcon(
              onPressed: _working ? null : () => _fireCourse(course),
              icon: const Icon(Icons.play_arrow_rounded, size: 18),
              label: Text('Fire course $course'),
            )).toList(),
          ),
        ],
      ],
    );
  }

  Widget _totalRow(String label, String value, {bool emphasized = false}) {
    return Row(
      children: [
        Text(
          label,
          style: TextStyle(
            fontSize: emphasized ? 16 : 13,
            fontWeight: emphasized ? FontWeight.w900 : FontWeight.w600,
            color: emphasized
                ? RestaurantColors.textPrimary
                : RestaurantColors.textSecondary,
          ),
        ),
        const Spacer(),
        Text(
          value,
          style: TextStyle(
            fontSize: emphasized ? 16 : 13,
            fontWeight: emphasized ? FontWeight.w900 : FontWeight.w700,
            color: emphasized
                ? RestaurantColors.brandHover
                : RestaurantColors.textPrimary,
          ),
        ),
      ],
    );
  }

  Widget _bottomNav() {
    // Do not advertise dead tabs: available actions must be functional.
    return Container(
      height: 58,
      decoration: const BoxDecoration(
        color: RestaurantColors.surface,
        border: Border(top: BorderSide(color: RestaurantColors.border)),
      ),
      child: Row(children: [
        Expanded(child: TextButton.icon(
          onPressed: null,
          icon: const Icon(Icons.restaurant_menu_rounded,
              color: RestaurantColors.brand),
          label: const Text('Menu',
              style: TextStyle(color: RestaurantColors.brand)),
        )),
        Expanded(child: TextButton.icon(
          onPressed: () => Navigator.of(context).maybePop(),
          icon: const Icon(Icons.table_restaurant_rounded,
              color: RestaurantColors.textPrimary),
          label: const Text('Tables',
              style: TextStyle(color: RestaurantColors.textPrimary)),
        )),
      ]),
    );
  }

  String _serviceLabel(Map<String, Object?> order) {
    switch (order['service_type']?.toString()) {
      case 'takeaway':
        return 'Takeaway';
      case 'delivery':
        return 'Delivery';
      case 'counter':
        return 'Counter';
      default:
        return 'Dine In';
    }
  }
}

class _OrderItemOptions {
  const _OrderItemOptions({
    this.modifiers = const [],
    this.notes,
    this.seatNumber,
    this.courseNumber,
    this.courseName,
    this.holdForCourse = false,
    this.allergyInstructions,
    this.kitchenInstructions,
  });

  final List<Map<String, Object?>> modifiers;
  final String? notes;
  final int? seatNumber;
  final int? courseNumber;
  final String? courseName;
  final bool holdForCourse;
  final String? allergyInstructions;
  final String? kitchenInstructions;
}

class _ItemOptionsDialog extends StatefulWidget {
  const _ItemOptionsDialog({
    required this.itemName,
    required this.groups,
    required this.coursesEnabled,
  });

  final String itemName;
  final List<Object?> groups;
  final bool coursesEnabled;

  @override
  State<_ItemOptionsDialog> createState() => _ItemOptionsDialogState();
}

class _ItemOptionsDialogState extends State<_ItemOptionsDialog> {
  final Map<String, Set<String>> _selected = {};
  final _notes = TextEditingController();
  final _allergy = TextEditingController();
  final _kitchen = TextEditingController();
  final _seat = TextEditingController();
  final _course = TextEditingController();
  final _courseName = TextEditingController();
  bool _holdForCourse = false;
  String? _error;

  @override
  void dispose() {
    _notes.dispose();
    _allergy.dispose();
    _kitchen.dispose();
    _seat.dispose();
    _course.dispose();
    _courseName.dispose();
    super.dispose();
  }

  void _submit() {
    final modifiers = <Map<String, Object?>>[];

    for (final raw in widget.groups) {
      final group = Map<String, Object?>.from(raw! as Map<Object?, Object?>);
      final id = group['id']!.toString();
      final min = (group['min_selections'] as num?)?.toInt() ?? 0;
      final max = (group['max_selections'] as num?)?.toInt() ?? 1;
      final selected = _selected[id] ?? const <String>{};

      if (selected.length < min || selected.length > max) {
        setState(() {
          _error =
              'Select between ' +
              min.toString() +
              ' and ' +
              max.toString() +
              ' option(s) for ' +
              group['name']!.toString() +
              '.';
        });
        return;
      }

      for (final optionId in selected) {
        modifiers.add(<String, Object?>{'option_id': optionId});
      }
    }

    Navigator.of(context).pop(
      _OrderItemOptions(
        modifiers: modifiers,
        notes: _clean(_notes.text),
        seatNumber: int.tryParse(_seat.text.trim()),
        courseNumber: widget.coursesEnabled
            ? int.tryParse(_course.text.trim())
            : null,
        courseName: widget.coursesEnabled ? _clean(_courseName.text) : null,
        holdForCourse: widget.coursesEnabled && _holdForCourse,
        allergyInstructions: _clean(_allergy.text),
        kitchenInstructions: _clean(_kitchen.text),
      ),
    );
  }

  String? _clean(String value) {
    final cleaned = value.trim();
    return cleaned.isEmpty ? null : cleaned;
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: Text(widget.itemName),
      content: SizedBox(
        width: 460,
        child: SingleChildScrollView(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              ...widget.groups.map((raw) {
                final group = Map<String, Object?>.from(
                  raw! as Map<Object?, Object?>,
                );
                final id = group['id']!.toString();
                final options =
                    group['options'] as List<Object?>? ?? const <Object?>[];

                return Padding(
                  padding: const EdgeInsets.only(bottom: 16),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        group['name']!.toString(),
                        style: const TextStyle(fontWeight: FontWeight.w800),
                      ),
                      const SizedBox(height: 8),
                      Wrap(
                        spacing: 8,
                        runSpacing: 8,
                        children: options.map((rawOption) {
                          final option = Map<String, Object?>.from(
                            rawOption! as Map<Object?, Object?>,
                          );
                          final optionId = option['id']!.toString();
                          final selected =
                              _selected[id]?.contains(optionId) ?? false;
                          final label =
                              option['name']!.toString() +
                              ' (+' +
                              formatMoney(option['price_delta']) +
                              ')';

                          return FilterChip(
                            selected: selected,
                            label: Text(label),
                            onSelected: (value) {
                              setState(() {
                                final set = _selected.putIfAbsent(
                                  id,
                                  () => <String>{},
                                );
                                if (value) {
                                  final max =
                                      (group['max_selections'] as num?)
                                          ?.toInt() ??
                                      1;
                                  if (set.length < max) set.add(optionId);
                                } else {
                                  set.remove(optionId);
                                }
                              });
                            },
                          );
                        }).toList(),
                      ),
                    ],
                  ),
                );
              }),
              Row(
                children: [
                  Expanded(
                    child: TextField(
                      controller: _seat,
                      keyboardType: TextInputType.number,
                      decoration: const InputDecoration(
                        labelText: 'Seat #',
                        prefixIcon: Icon(Icons.event_seat_outlined),
                      ),
                    ),
                  ),
                  if (widget.coursesEnabled) ...[
                    const SizedBox(width: 10),
                    Expanded(
                      child: TextField(
                        controller: _course,
                        keyboardType: TextInputType.number,
                        decoration: const InputDecoration(
                          labelText: 'Course #',
                          prefixIcon: Icon(Icons.format_list_numbered),
                        ),
                      ),
                    ),
                  ],
                ],
              ),
              if (widget.coursesEnabled) ...[
                const SizedBox(height: 10),
                TextField(
                  controller: _courseName,
                  decoration: const InputDecoration(
                    labelText: 'Course name',
                    prefixIcon: Icon(Icons.restaurant_menu_outlined),
                  ),
                ),
                SwitchListTile.adaptive(
                  contentPadding: EdgeInsets.zero,
                  title: const Text('Hold until course is fired'),
                  value: _holdForCourse,
                  onChanged: (value) => setState(() => _holdForCourse = value),
                ),
              ],
              const SizedBox(height: 10),
              TextField(
                controller: _notes,
                decoration: const InputDecoration(
                  labelText: 'Order note',
                  prefixIcon: Icon(Icons.note_alt_outlined),
                ),
              ),
              const SizedBox(height: 10),
              TextField(
                controller: _kitchen,
                decoration: const InputDecoration(
                  labelText: 'Kitchen instructions',
                  prefixIcon: Icon(Icons.soup_kitchen_outlined),
                ),
              ),
              const SizedBox(height: 10),
              TextField(
                controller: _allergy,
                decoration: const InputDecoration(
                  labelText: 'Allergy / critical instruction',
                  prefixIcon: Icon(Icons.warning_amber_rounded),
                ),
              ),
              if (_error != null) ...[
                const SizedBox(height: 10),
                Text(
                  _error!,
                  style: const TextStyle(
                    color: Colors.red,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('Cancel'),
        ),
        FilledButton(onPressed: _submit, child: const Text('Add to order')),
      ],
    );
  }
}


class _MoveItemChoice {
  const _MoveItemChoice({
    required this.localLineId,
    required this.targetOrderId,
    required this.quantity,
  });

  final String localLineId;
  final String targetOrderId;
  final int quantity;
}

class _MoveItemDialog extends StatefulWidget {
  const _MoveItemDialog({
    required this.items,
    required this.targets,
  });

  final List<Map<String, Object?>> items;
  final List<Map<String, Object?>> targets;

  @override
  State<_MoveItemDialog> createState() => _MoveItemDialogState();
}

class _MoveItemDialogState extends State<_MoveItemDialog> {
  late String lineId;
  late String targetOrderId;
  int quantity = 1;

  @override
  void initState() {
    super.initState();
    lineId = widget.items.first['local_line_id']!.toString();
    targetOrderId = widget.targets.first['local_order_id']!.toString();
  }

  int get maxQuantity {
    final item = widget.items.firstWhere(
      (item) => item['local_line_id']!.toString() == lineId,
    );
    final total = (item['quantity'] as num?)?.toInt() ?? 0;
    final dispatched = (item['dispatched_quantity'] as num?)?.toInt() ?? 0;
    return total - dispatched;
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('Move unsent item'),
      content: SizedBox(
        width: 440,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            DropdownButtonFormField<String>(
              initialValue: lineId,
              decoration: const InputDecoration(labelText: 'Item'),
              items: widget.items
                  .map(
                    (item) => DropdownMenuItem<String>(
                      value: item['local_line_id']!.toString(),
                      child: Text(
                        item['item_name']!.toString() +
                            ' · unsent ' +
                            (((item['quantity'] as num?)?.toInt() ?? 0) -
                                    ((item['dispatched_quantity'] as num?)
                                            ?.toInt() ??
                                        0))
                                .toString(),
                      ),
                    ),
                  )
                  .toList(),
              onChanged: (value) {
                if (value != null) {
                  setState(() {
                    lineId = value;
                    quantity = 1;
                  });
                }
              },
            ),
            const SizedBox(height: 12),
            DropdownButtonFormField<String>(
              initialValue: targetOrderId,
              decoration: const InputDecoration(labelText: 'Target order'),
              items: widget.targets
                  .map(
                    (order) => DropdownMenuItem<String>(
                      value: order['local_order_id']!.toString(),
                      child: Text(
                        _orderLabel(
                          order,
                          fallback:
                              'Order ${order['local_order_id']!.toString().substring(0, 8)}',
                        ),
                      ),
                    ),
                  )
                  .toList(),
              onChanged: (value) {
                if (value != null) {
                  setState(() => targetOrderId = value);
                }
              },
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                const Text('Quantity'),
                const Spacer(),
                IconButton(
                  onPressed:
                      quantity > 1 ? () => setState(() => quantity--) : null,
                  icon: const Icon(Icons.remove_circle_outline),
                ),
                Text(quantity.toString()),
                IconButton(
                  onPressed: quantity < maxQuantity
                      ? () => setState(() => quantity++)
                      : null,
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
          onPressed: () => Navigator.of(context).pop(
            _MoveItemChoice(
              localLineId: lineId,
              targetOrderId: targetOrderId,
              quantity: quantity,
            ),
          ),
          child: const Text('Move'),
        ),
      ],
    );
  }
}
