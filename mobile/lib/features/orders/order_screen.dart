import 'package:flutter/material.dart';

import '../../app/app_strings.dart';
import '../../app/dependencies.dart';

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
  List<Map<String, Object?>> _items = const [];
  List<Map<String, Object?>> _categories = const [];
  Map<String, List<Map<String, Object?>>> _menu = const {};
  bool _working = false;

  @override
  void initState() {
    super.initState();
    _refresh();
  }

  Future<void> _refresh() async {
    final db = widget.dependencies.database;
    final order = await db.order(widget.localOrderId);
    final items = await db.orderItems(widget.localOrderId);
    final categories = await db.menuCategories();
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
      _items = items;
      _categories = categories;
      _menu = menu;
    });
  }

  Future<void> _addItem(Map<String, Object?> item) async {
    if (_working) {
      return;
    }

    setState(() => _working = true);

    try {
      await widget.dependencies.orders.addItem(
        localOrderId: widget.localOrderId,
        menuItem: item,
      );
      await _refresh();
    } on Object catch (error) {
      _showError(error);
    } finally {
      if (mounted) {
        setState(() => _working = false);
      }
    }
  }

  Future<void> _submit() async {
    if (_working) {
      return;
    }

    setState(() => _working = true);

    try {
      await widget.dependencies.orders.submit(widget.localOrderId);
      await _refresh();

      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(widget.strings.queuedForKitchen)),
        );
      }

      await widget.dependencies.syncCoordinator.syncNow();
      await _refresh();
    } on Object catch (error) {
      _showError(error);
    } finally {
      if (mounted) {
        setState(() => _working = false);
      }
    }
  }

  void _showError(Object error) {
    if (!mounted) {
      return;
    }

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(error.toString())),
    );
  }

  @override
  Widget build(BuildContext context) {
    final order = _order;

    if (order == null) {
      return const Scaffold(
        body: Center(child: CircularProgressIndicator()),
      );
    }

    final draft = order['status'] == 'draft';

    return Scaffold(
      appBar: AppBar(
        title: Text(
          'Order ' + widget.localOrderId.substring(0, 8),
        ),
        actions: [
          Padding(
            padding: const EdgeInsetsDirectional.only(end: 16),
            child: Center(
              child: Text(order['status']!.toString()),
            ),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          if (_items.isNotEmpty) ...[
            Text(
              'Current order',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 8),
            ..._items.map(
              (item) => ListTile(
                contentPadding: EdgeInsets.zero,
                title: Text(item['item_name']!.toString()),
                subtitle: Text('x' + item['quantity'].toString()),
                trailing: Text(item['line_total'].toString() + ' AFN'),
              ),
            ),
            const Divider(height: 32),
          ],
          Text(
            widget.strings.menu,
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 8),
          ..._categories.map((category) {
            final categoryId = category['id']!.toString();
            final items = _menu[categoryId] ?? const [];

            return ExpansionTile(
              initiallyExpanded: _categories.length == 1,
              title: Text(category['name']!.toString()),
              children: items.map((item) {
                return ListTile(
                  title: Text(item['name']!.toString()),
                  subtitle: item['description'] == null
                      ? null
                      : Text(item['description']!.toString()),
                  trailing: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(item['price'].toString() + ' AFN'),
                      const SizedBox(width: 8),
                      IconButton(
                        tooltip: widget.strings.add,
                        onPressed: draft && !_working
                            ? () => _addItem(item)
                            : null,
                        icon: const Icon(Icons.add_circle_outline),
                      ),
                    ],
                  ),
                );
              }).toList(),
            );
          }),
          const SizedBox(height: 24),
        ],
      ),
      bottomNavigationBar: draft
          ? SafeArea(
              minimum: const EdgeInsets.all(16),
              child: FilledButton.icon(
                onPressed: _items.isEmpty || _working ? null : _submit,
                icon: const Icon(Icons.soup_kitchen_outlined),
                label: Text(widget.strings.submitOrder),
              ),
            )
          : null,
    );
  }
}
