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
  String? _selectedCategoryId;
  String _query = '';
  bool _working = false;
  bool _hasUnsent = false;

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
    final hasUnsent = await db.hasUnsentItems(widget.localOrderId);
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
      _hasUnsent = hasUnsent;
      _selectedCategoryId ??=
          categories.isEmpty ? null : categories.first['id']!.toString();
    });
  }

  Future<void> _addItem(Map<String, Object?> item) async {
    if (_working) return;
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
      if (mounted) setState(() => _working = false);
    }
  }

  Future<void> _submit() async {
    if (_working) return;
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
      if (mounted) setState(() => _working = false);
    }
  }

  void _showError(Object error) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(error.toString())),
    );
  }

  bool get _editable => const <String>{
        'draft',
        'submitted',
        'submitted_pending_sync',
        'preparing',
        'ready',
        'served',
      }.contains(_order?['status']?.toString());

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
      backgroundColor: const Color(0xFFF3F5F7),
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
    return Container(
      height: 68,
      padding: const EdgeInsets.symmetric(horizontal: 16),
      color: const Color(0xFF171B20),
      child: Row(
        children: [
          IconButton(
            onPressed: () => Navigator.of(context).maybePop(),
            color: Colors.white,
            icon: const Icon(Icons.arrow_back_rounded),
          ),
          Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(
              border: Border.all(color: const Color(0xFFFFD96B)),
              borderRadius: BorderRadius.circular(11),
            ),
            child: const Icon(
              Icons.restaurant_rounded,
              color: Color(0xFFFFD96B),
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
                  color: Colors.white,
                  fontWeight: FontWeight.w800,
                ),
              ),
              Text(
                'Restaurant',
                style: TextStyle(
                  color: Color(0xFFFFD96B),
                  fontSize: 11,
                ),
              ),
            ],
          ),
          const SizedBox(width: 20),
          const Icon(Icons.circle, size: 9, color: Color(0xFF55D46A)),
          const SizedBox(width: 6),
          const Text(
            'Connected',
            style: TextStyle(color: Colors.white70, fontSize: 12),
          ),
          const Spacer(),
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 7),
            decoration: BoxDecoration(
              color: const Color(0xFF252A30),
              borderRadius: BorderRadius.circular(10),
            ),
            child: Text(
              (order['status']?.toString() ?? '').replaceAll('_', ' '),
              style: const TextStyle(
                color: Colors.white70,
                fontSize: 11,
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _tabletBody(BuildContext context, Map<String, Object?> order) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SizedBox(width: 160, child: _categoryRail()),
        Expanded(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(12, 12, 8, 12),
            child: Column(
              children: [
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
    return ListView(
      padding: const EdgeInsets.all(12),
      children: [
        SizedBox(
          height: 46,
          child: ListView(
            scrollDirection: Axis.horizontal,
            children: [
              _categoryChip('All', null),
              ..._categories.map(
                (category) => Padding(
                  padding: const EdgeInsetsDirectional.only(start: 8),
                  child: _categoryChip(
                    category['name']!.toString(),
                    category['id']!.toString(),
                  ),
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 10),
        _searchBar(),
        const SizedBox(height: 12),
        _menuGrid(phone: true),
        const SizedBox(height: 16),
        _orderPanel(order),
      ],
    );
  }

  Widget _searchBar() {
    return TextField(
      onChanged: (value) => setState(() => _query = value),
      decoration: InputDecoration(
        hintText: 'Search menu items...',
        prefixIcon: const Icon(Icons.search_rounded),
        filled: true,
        fillColor: Colors.white,
        contentPadding: EdgeInsets.zero,
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: BorderSide.none,
        ),
      ),
    );
  }

  Widget _categoryRail() {
    return Container(
      color: const Color(0xFFF8F9FA),
      padding: const EdgeInsets.all(10),
      child: ListView(
        children: [
          _railTile('All Items', null, Icons.grid_view_rounded),
          const SizedBox(height: 8),
          ..._categories.map(
            (category) => Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: _railTile(
                category['name']!.toString(),
                category['id']!.toString(),
                Icons.restaurant_menu_rounded,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _railTile(String label, String? id, IconData icon) {
    final selected = _selectedCategoryId == id;

    return Material(
      color: selected ? const Color(0xFF171B20) : Colors.transparent,
      borderRadius: BorderRadius.circular(14),
      child: InkWell(
        onTap: () => setState(() => _selectedCategoryId = id),
        borderRadius: BorderRadius.circular(14),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 14),
          child: Row(
            children: [
              Icon(
                icon,
                size: 20,
                color: selected ? const Color(0xFFFFD96B) : Colors.black54,
              ),
              const SizedBox(width: 9),
              Expanded(
                child: Text(
                  label,
                  style: TextStyle(
                    color: selected ? Colors.white : const Color(0xFF34383D),
                    fontSize: 12,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _categoryChip(String label, String? id) {
    return ChoiceChip(
      selected: _selectedCategoryId == id,
      onSelected: (_) => setState(() => _selectedCategoryId = id),
      label: Text(label),
      selectedColor: const Color(0xFFFFD96B),
      backgroundColor: Colors.white,
      side: BorderSide.none,
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
      shrinkWrap: phone,
      physics: phone ? const NeverScrollableScrollPhysics() : null,
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
          color: Colors.white,
          clipBehavior: Clip.antiAlias,
          borderRadius: BorderRadius.circular(18),
          child: InkWell(
            onTap: _editable && !_working ? () => _addItem(item) : null,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: SizedBox(
                    width: double.infinity,
                    child: _MenuItemImage(
                      imageUrl: item['image_url']?.toString(),
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
                              '\${item['price']} AFN',
                              style: const TextStyle(
                                color: Color(0xFF9A6C00),
                                fontWeight: FontWeight.w700,
                              ),
                            ),
                          ],
                        ),
                      ),
                      IconButton.filled(
                        onPressed:
                            _editable && !_working ? () => _addItem(item) : null,
                        style: IconButton.styleFrom(
                          backgroundColor: const Color(0xFFFFD96B),
                          foregroundColor: const Color(0xFF171B20),
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
        color: Colors.white,
        borderRadius: BorderRadius.circular(22),
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
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            padding: const EdgeInsets.all(16),
            color: const Color(0xFF171B20),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    order['service_reference']?.toString().isNotEmpty == true
                        ? order['service_reference']!.toString()
                        : 'Current Order',
                    style: const TextStyle(
                      color: Colors.white,
                      fontWeight: FontWeight.w800,
                      fontSize: 16,
                    ),
                  ),
                ),
                Container(
                  padding:
                      const EdgeInsets.symmetric(horizontal: 9, vertical: 6),
                  decoration: BoxDecoration(
                    color: const Color(0xFFE73737),
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
          Flexible(
            child: _items.isEmpty
                ? const Padding(
                    padding: EdgeInsets.all(30),
                    child: Text(
                      'Add menu items to start the order.',
                      textAlign: TextAlign.center,
                    ),
                  )
                : ListView.separated(
                    shrinkWrap: true,
                    padding: const EdgeInsets.all(14),
                    itemCount: _items.length,
                    separatorBuilder: (_, __) => const Divider(height: 18),
                    itemBuilder: (context, index) {
                      final item = _items[index];
                      final allergy = item['allergy_instructions']?.toString();

                      return Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Container(
                            width: 42,
                            height: 42,
                            decoration: BoxDecoration(
                              color: const Color(0xFFF1F2F4),
                              borderRadius: BorderRadius.circular(11),
                            ),
                            child: const Icon(
                              Icons.restaurant_menu_rounded,
                              size: 19,
                            ),
                          ),
                          const SizedBox(width: 10),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  item['item_name']!.toString(),
                                  style: const TextStyle(
                                    fontWeight: FontWeight.w800,
                                  ),
                                ),
                                Text(
                                  'x\${item['quantity']} · \${item['line_total']} AFN',
                                  style: const TextStyle(
                                    color: Colors.black54,
                                    fontSize: 12,
                                  ),
                                ),
                                if (allergy != null && allergy.isNotEmpty)
                                  Text(
                                    'ALLERGY: $allergy',
                                    style: const TextStyle(
                                      color: Colors.red,
                                      fontWeight: FontWeight.w800,
                                      fontSize: 10,
                                    ),
                                  ),
                              ],
                            ),
                          ),
                        ],
                      );
                    },
                  ),
          ),
          const Divider(height: 1),
          Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              children: [
                _totalRow(
                  'Subtotal',
                  "\${order['subtotal'] ?? '0.00'} AFN",
                ),
                const SizedBox(height: 8),
                _totalRow(
                  'Total',
                  "\${order['total'] ?? '0.00'} AFN",
                  emphasized: true,
                ),
                const SizedBox(height: 14),
                SizedBox(
                  width: double.infinity,
                  height: 50,
                  child: FilledButton.icon(
                    onPressed:
                        _editable && _hasUnsent && !_working ? _submit : null,
                    style: FilledButton.styleFrom(
                      backgroundColor: const Color(0xFFFFD96B),
                      foregroundColor: const Color(0xFF171B20),
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(13),
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

  Widget _totalRow(String label, String value, {bool emphasized = false}) {
    return Row(
      children: [
        Text(
          label,
          style: TextStyle(
            fontSize: emphasized ? 16 : 13,
            fontWeight: emphasized ? FontWeight.w900 : FontWeight.w600,
            color: emphasized ? const Color(0xFF171B20) : Colors.black54,
          ),
        ),
        const Spacer(),
        Text(
          value,
          style: TextStyle(
            fontSize: emphasized ? 16 : 13,
            fontWeight: emphasized ? FontWeight.w900 : FontWeight.w700,
            color: emphasized ? const Color(0xFF9A6C00) : Colors.black87,
          ),
        ),
      ],
    );
  }

  Widget _bottomNav() {
    const entries = [
      (Icons.home_rounded, 'Home'),
      (Icons.table_restaurant_rounded, 'Tables'),
      (Icons.receipt_long_rounded, 'Orders'),
      (Icons.soup_kitchen_rounded, 'Kitchen'),
      (Icons.more_horiz_rounded, 'More'),
    ];

    return Container(
      height: 60,
      color: const Color(0xFF171B20),
      child: Row(
        children: List.generate(entries.length, (index) {
          final selected = index == 0;
          final entry = entries[index];

          return Expanded(
            child: InkWell(
              onTap: index == 1 ? () => Navigator.of(context).maybePop() : null,
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Icon(
                    entry.$1,
                    size: 20,
                    color: selected
                        ? const Color(0xFFFFD96B)
                        : Colors.white54,
                  ),
                  const SizedBox(height: 2),
                  Text(
                    entry.$2,
                    style: TextStyle(
                      color: selected
                          ? const Color(0xFFFFD96B)
                          : Colors.white54,
                      fontSize: 10,
                    ),
                  ),
                ],
              ),
            ),
          );
        }),
      ),
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

class _MenuItemImage extends StatelessWidget {
  const _MenuItemImage({required this.imageUrl});

  final String? imageUrl;

  @override
  Widget build(BuildContext context) {
    final url = imageUrl?.trim() ?? '';

    if (url.isEmpty) return _placeholder(context);

    return Image.network(
      url,
      fit: BoxFit.cover,
      errorBuilder: (context, error, stackTrace) => _placeholder(context),
    );
  }

  Widget _placeholder(BuildContext context) {
    return Container(
      color: Theme.of(context).colorScheme.surfaceContainerHighest,
      alignment: Alignment.center,
      child: Icon(
        Icons.restaurant_menu,
        size: 34,
        color: Theme.of(context).colorScheme.onSurfaceVariant,
      ),
    );
  }
}
