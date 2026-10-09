import 'package:flutter/material.dart';

import '../../app/dependencies.dart';
import '../../core/models/session_credentials.dart';
import 'menu_item_photo.dart';

/// Browse the *real* synced restaurant catalog before opening an order.
/// On a wide iPad it uses the same category-rail / photo-card visual language
/// as the reference POS. Selecting an item prompts for an order service.
class MenuBrowserScreen extends StatefulWidget {
  const MenuBrowserScreen({
    required this.dependencies,
    required this.onStartServiceOrder,
    super.key,
  });

  final AppDependencies dependencies;
  final Future<void> Function(String serviceType) onStartServiceOrder;

  @override
  State<MenuBrowserScreen> createState() => _MenuBrowserScreenState();
}

class _MenuBrowserScreenState extends State<MenuBrowserScreen> {
  List<Map<String, Object?>> _categories = const [];
  Map<String, List<Map<String, Object?>>> _menu = const {};
  SessionCredentials? _credentials;
  String? _categoryId;
  String _search = '';
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final db = widget.dependencies.database;
    final categories = await db.menuCategories();
    final menu = <String, List<Map<String, Object?>>>{};
    for (final category in categories) {
      final id = category['id']!.toString();
      menu[id] = await db.menuItems(id);
    }
    final credentials = await widget.dependencies.credentials.readSession();
    if (!mounted) return;
    setState(() {
      _categories = categories;
      _menu = menu;
      _credentials = credentials;
      _loading = false;
    });
  }

  Future<void> _sync() async {
    try {
      await widget.dependencies.syncCoordinator.syncNow();
      await _load();
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Menu refresh failed: $error')),
        );
      }
    }
  }

  List<Map<String, Object?>> get _visibleItems {
    final items = _categoryId == null
        ? _menu.values.expand((items) => items)
        : (_menu[_categoryId] ?? const <Map<String, Object?>>[]);
    final query = _search.trim().toLowerCase();
    return items
        .where((item) =>
            query.isEmpty ||
            (item['name']?.toString().toLowerCase().contains(query) ?? false) ||
            (item['description']?.toString().toLowerCase().contains(query) ?? false))
        .toList(growable: false);
  }

  Future<void> _startOrder(String serviceType) async {
    Navigator.of(context).pop();
    await widget.onStartServiceOrder(serviceType);
  }

  Future<void> _chooseOrderType() async {
    await showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      builder: (sheetContext) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text('Start a new order',
                  style: Theme.of(sheetContext).textTheme.titleLarge),
              const SizedBox(height: 8),
              const Text('Or go back to Tables to start a dine-in order.'),
              const SizedBox(height: 16),
              for (final entry in const [
                ('takeaway', 'Takeaway', Icons.shopping_bag_outlined),
                ('delivery', 'Delivery', Icons.delivery_dining_outlined),
                ('counter', 'Counter', Icons.point_of_sale_outlined),
              ])
                Padding(
                  padding: const EdgeInsets.only(bottom: 8),
                  child: FilledButton.icon(
                    onPressed: () {
                      Navigator.of(sheetContext).pop();
                      _startOrder(entry.$1);
                    },
                    icon: Icon(entry.$3),
                    label: Text(entry.$2),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final wide = MediaQuery.sizeOf(context).width >= 740;
    return Scaffold(
      appBar: AppBar(
        title: const Text('Menu'),
        actions: [
          IconButton(
            tooltip: 'Refresh menu',
            onPressed: _sync,
            icon: const Icon(Icons.refresh_rounded),
          ),
          Padding(
            padding: const EdgeInsetsDirectional.only(end: 12),
            child: FilledButton.icon(
              onPressed: _chooseOrderType,
              icon: const Icon(Icons.add_rounded),
              label: const Text('New order'),
            ),
          ),
        ],
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : Row(
              children: [
                if (wide)
                  SizedBox(
                    width: 184,
                    child: _categoriesView(),
                  ),
                Expanded(
                  child: Column(
                    children: [
                      Padding(
                        padding: const EdgeInsets.all(14),
                        child: TextField(
                          onChanged: (value) =>
                              setState(() => _search = value),
                          decoration: const InputDecoration(
                            prefixIcon: Icon(Icons.search_rounded),
                            hintText: 'Search menu items',
                          ),
                        ),
                      ),
                      if (!wide)
                        SizedBox(
                          height: 52,
                          child: ListView(
                            scrollDirection: Axis.horizontal,
                            padding: const EdgeInsets.symmetric(horizontal: 14),
                            children: [
                              _categoryChip('All', null),
                              for (final category in _categories)
                                Padding(
                                  padding: const EdgeInsetsDirectional.only(start: 8),
                                  child: _categoryChip(category['name']!.toString(),
                                      category['id']!.toString()),
                                ),
                            ],
                          ),
                        ),
                      Expanded(
                        child: RefreshIndicator(
                          onRefresh: _sync,
                          child: _buildGrid(wide),
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
    );
  }

  Widget _categoriesView() => Container(
        color: const Color(0xFFF7F8F9),
        child: ListView(
          padding: const EdgeInsets.all(12),
          children: [
            _categoryChip('All items', null),
            const SizedBox(height: 10),
            for (final category in _categories)
              Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: _categoryChip(category['name']!.toString(),
                    category['id']!.toString()),
              ),
          ],
        ),
      );

  Widget _categoryChip(String name, String? id) => ChoiceChip(
        selected: _categoryId == id,
        onSelected: (_) => setState(() => _categoryId = id),
        label: Text(name),
        backgroundColor: Colors.white,
        selectedColor: const Color(0xFF171B20),
        labelStyle: TextStyle(
          color: _categoryId == id ? const Color(0xFFF3C96B) : const Color(0xFF303947),
        ),
        side: BorderSide.none,
      );

  Widget _buildGrid(bool wide) {
    final items = _visibleItems;
    if (items.isEmpty) {
      return ListView(children: [
        const SizedBox(height: 90),
        const Center(child: Icon(Icons.restaurant_menu_rounded, size: 50)),
        const SizedBox(height: 12),
        Center(
          child: Text(_menu.isEmpty
              ? 'No menu has synced yet. Connect and refresh your restaurant catalog.'
              : 'No items match this category or search.',
              textAlign: TextAlign.center),
        ),
      ]);
    }

    return GridView.builder(
      padding: const EdgeInsets.all(14),
      itemCount: items.length,
      gridDelegate: SliverGridDelegateWithMaxCrossAxisExtent(
        maxCrossAxisExtent: wide ? 260 : 210,
        mainAxisExtent: wide ? 255 : 230,
        crossAxisSpacing: 14,
        mainAxisSpacing: 14,
      ),
      itemBuilder: (context, index) {
        final item = items[index];
        return Card(
          margin: EdgeInsets.zero,
          clipBehavior: Clip.antiAlias,
          child: InkWell(
            onTap: _chooseOrderType,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Expanded(
                  child: MenuItemPhoto(
                    imageUrl: item['image_url']?.toString(),
                    credentials: _credentials,
                  ),
                ),
                Padding(
                  padding: const EdgeInsets.fromLTRB(12, 10, 8, 10),
                  child: Row(
                    children: [
                      Expanded(child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(item['name']!.toString(),
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: const TextStyle(fontWeight: FontWeight.w800)),
                          const SizedBox(height: 4),
                          Text('${item['price']} AFN',
                              style: const TextStyle(
                                color: Color(0xFF946915),
                                fontWeight: FontWeight.w700,
                              )),
                        ],
                      )),
                      const Icon(Icons.add_circle_rounded,
                          color: Color(0xFFD4A94A), size: 32),
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
}
