import 'dart:convert';

import 'package:path/path.dart' as p;
import 'package:sqflite/sqflite.dart';

import '../../sync/sync_store.dart';
import 'outbox_mutation.dart';

class LocalDatabase implements SyncStore {
  LocalDatabase._(this._db);

  final Database _db;

  static Future<LocalDatabase> open() async {
    final root = await getDatabasesPath();
    final database = await openDatabase(
      p.join(root, 'businessos_restaurant_waiter.db'),
      version: 2,
      onCreate: (db, version) async {
        await db.execute('''
          CREATE TABLE settings (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
          )
        ''');
        await db.execute('''
          CREATE TABLE menu_categories (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            sort_order INTEGER NOT NULL DEFAULT 0,
            is_active INTEGER NOT NULL DEFAULT 1
          )
        ''');
        await db.execute('''
          CREATE TABLE menu_items (
            id TEXT PRIMARY KEY,
            category_id TEXT,
            sku TEXT,
            name TEXT NOT NULL,
            description TEXT,
            image_url TEXT,
            price TEXT NOT NULL,
            sort_order INTEGER NOT NULL DEFAULT 0,
            is_available INTEGER NOT NULL DEFAULT 1
          )
        ''');
        await db.execute('''
          CREATE TABLE dining_tables (
            id TEXT PRIMARY KEY,
            code TEXT NOT NULL,
            name TEXT NOT NULL,
            capacity INTEGER NOT NULL,
            status TEXT NOT NULL,
            is_active INTEGER NOT NULL DEFAULT 1,
            area_id TEXT,
            area_name TEXT,
            branch_id TEXT,
            branch_name TEXT
          )
        ''');
        await db.execute('''
          CREATE TABLE orders (
            local_order_id TEXT PRIMARY KEY,
            client_order_id TEXT,
            server_id TEXT,
            table_id TEXT NOT NULL,
            status TEXT NOT NULL,
            guest_count INTEGER NOT NULL,
            notes TEXT,
            subtotal TEXT NOT NULL DEFAULT '0.00',
            total TEXT NOT NULL DEFAULT '0.00',
            opened_at TEXT,
            submitted_at TEXT,
            served_at TEXT,
            closed_at TEXT,
            updated_at TEXT NOT NULL
          )
        ''');
        await db.execute('''
          CREATE TABLE order_items (
            local_line_id TEXT PRIMARY KEY,
            client_line_id TEXT,
            server_id TEXT,
            local_order_id TEXT NOT NULL,
            menu_item_id TEXT,
            item_name TEXT NOT NULL,
            unit_price TEXT NOT NULL,
            quantity INTEGER NOT NULL,
            line_total TEXT NOT NULL,
            notes TEXT,
            status TEXT NOT NULL
          )
        ''');
        await db.execute('''
          CREATE TABLE outbox (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            mutation_id TEXT NOT NULL UNIQUE,
            operation TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            occurred_at TEXT NOT NULL,
            status TEXT NOT NULL DEFAULT 'pending',
            attempts INTEGER NOT NULL DEFAULT 0,
            next_attempt_at TEXT,
            last_error TEXT
          )
        ''');
        await db.execute('''
          CREATE TABLE conflicts (
            mutation_id TEXT PRIMARY KEY,
            operation TEXT NOT NULL,
            code TEXT NOT NULL,
            message TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            created_at TEXT NOT NULL
          )
        ''');
        await db.execute(
          'CREATE INDEX outbox_ready_idx ON outbox(status, next_attempt_at, id)',
        );
        await db.execute(
          'CREATE INDEX orders_status_idx ON orders(status, updated_at)',
        );
        await db.execute(
          'CREATE INDEX order_items_order_idx ON order_items(local_order_id)',
        );
      },
      onUpgrade: (db, oldVersion, newVersion) async {
        if (oldVersion < 2) {
          await db.execute(
            'ALTER TABLE menu_items ADD COLUMN image_url TEXT',
          );
        }
      },
    );

    return LocalDatabase._(database);
  }

  @override
  Future<List<OutboxMutation>> pendingMutations({int limit = 50}) async {
    final now = DateTime.now().toUtc().toIso8601String();
    final rows = await _db.query(
      'outbox',
      where:
          "status = 'pending' AND (next_attempt_at IS NULL OR next_attempt_at <= ?)",
      whereArgs: <Object?>[now],
      orderBy: 'id ASC',
      limit: limit,
    );

    return rows.map((row) {
      return OutboxMutation(
        id: row['id']! as int,
        mutationId: row['mutation_id']! as String,
        operation: row['operation']! as String,
        payload: Map<String, Object?>.from(
          jsonDecode(row['payload_json']! as String) as Map<String, dynamic>,
        ),
        occurredAt: DateTime.parse(row['occurred_at']! as String),
        attempts: row['attempts']! as int,
      );
    }).toList(growable: false);
  }

  @override
  Future<int> syncCursor({required String scope}) async {
    final value = await systemState('sync_cursor_' + scope);
    return int.tryParse(value ?? '') ?? 0;
  }

  @override
  Future<void> applyBootstrap(
    Map<String, Object?> data, {
    required String cursorScope,
  }) async {
    await _db.transaction((txn) async {
      await txn.delete('menu_items');
      await txn.delete('menu_categories');

      for (final rawCategory in data['menu'] as List<Object?>? ?? const []) {
        final category = Map<String, Object?>.from(
          rawCategory! as Map<Object?, Object?>,
        );
        await _upsertCategory(txn, category);

        for (final rawItem in category['items'] as List<Object?>? ?? const []) {
          final item = Map<String, Object?>.from(
            rawItem! as Map<Object?, Object?>,
          );
          await _upsertMenuItem(txn, item, category['id']!.toString());
        }
      }

      for (final rawTable in data['tables'] as List<Object?>? ?? const []) {
        await _upsertTable(
          txn,
          Map<String, Object?>.from(rawTable! as Map<Object?, Object?>),
        );
      }

      for (final rawOrder in data['orders'] as List<Object?>? ?? const []) {
        await _upsertOrderSnapshot(
          txn,
          Map<String, Object?>.from(rawOrder! as Map<Object?, Object?>),
        );
      }

      await _setSettingTxn(
        txn,
        'sync_cursor_' + cursorScope,
        ((data['cursor'] as num?)?.toInt() ?? 0).toString(),
      );
      await _setSettingTxn(txn, 'server_locked', '0');
      await _reapplyLocalOccupancy(txn);
    });
  }

  @override
  Future<void> applyPull(
    Map<String, Object?> data, {
    required String cursorScope,
  }) async {
    await _db.transaction((txn) async {
      for (final rawChange in data['changes'] as List<Object?>? ?? const []) {
        final change = Map<String, Object?>.from(
          rawChange! as Map<Object?, Object?>,
        );
        await _applyChange(txn, change);
      }

      await _setSettingTxn(
        txn,
        'sync_cursor_' + cursorScope,
        ((data['cursor'] as num?)?.toInt() ?? 0).toString(),
      );
      await _setSettingTxn(txn, 'server_locked', '0');
      await _reapplyLocalOccupancy(txn);
    });
  }

  @override
  Future<void> applyAcceptedResult(Map<String, Object?> result) async {
    await _db.transaction((txn) async {
      final mutationId = result['mutation_id']!.toString();
      final data = result['data'];

      if (data is Map<Object?, Object?>) {
        final typed = Map<String, Object?>.from(data);
        final order = result['entity_type'] == 'order_item'
            ? typed['order']
            : typed;

        if (order is Map<Object?, Object?>) {
          await _upsertOrderSnapshot(
            txn,
            Map<String, Object?>.from(order),
          );
        }
      }

      await txn.delete(
        'outbox',
        where: 'mutation_id = ?',
        whereArgs: <Object?>[mutationId],
      );
      await txn.delete(
        'conflicts',
        where: 'mutation_id = ?',
        whereArgs: <Object?>[mutationId],
      );
      await _reapplyLocalOccupancy(txn);
    });
  }

  @override
  Future<void> markConflict(
    OutboxMutation mutation,
    Map<String, Object?> result,
  ) async {
    await _db.transaction((txn) async {
      await txn.update(
        'outbox',
        <String, Object?>{
          'status': 'conflict',
          'last_error': result['code']?.toString() ?? 'conflict',
        },
        where: 'mutation_id = ?',
        whereArgs: <Object?>[mutation.mutationId],
      );
      await txn.insert(
        'conflicts',
        <String, Object?>{
          'mutation_id': mutation.mutationId,
          'operation': mutation.operation,
          'code': result['code']?.toString() ?? 'conflict',
          'message': result['message']?.toString() ?? 'Sync conflict',
          'payload_json': jsonEncode(mutation.payload),
          'created_at': DateTime.now().toUtc().toIso8601String(),
        },
        conflictAlgorithm: ConflictAlgorithm.replace,
      );
    });
  }

  @override
  Future<void> markRetry(
    OutboxMutation mutation, {
    required String message,
    required DateTime retryAt,
  }) async {
    await _db.update(
      'outbox',
      <String, Object?>{
        'attempts': mutation.attempts + 1,
        'next_attempt_at': retryAt.toUtc().toIso8601String(),
        'last_error': message,
      },
      where: 'mutation_id = ?',
      whereArgs: <Object?>[mutation.mutationId],
    );
  }

  @override
  Future<void> setSystemState(String key, String value) async {
    await _db.insert(
      'settings',
      <String, Object?>{'key': key, 'value': value},
      conflictAlgorithm: ConflictAlgorithm.replace,
    );
  }

  Future<String?> systemState(String key) async {
    final rows = await _db.query(
      'settings',
      columns: const <String>['value'],
      where: 'key = ?',
      whereArgs: <Object?>[key],
      limit: 1,
    );

    return rows.isEmpty ? null : rows.first['value'] as String?;
  }

  Future<List<Map<String, Object?>>> tables() async {
    return _db.query(
      'dining_tables',
      where: 'is_active = 1',
      orderBy: 'code ASC',
    );
  }

  Future<List<Map<String, Object?>>> menuCategories() {
    return _db.query(
      'menu_categories',
      where: 'is_active = 1',
      orderBy: 'sort_order ASC, name ASC',
    );
  }

  Future<List<Map<String, Object?>>> menuItems(String categoryId) {
    return _db.query(
      'menu_items',
      where: 'category_id = ? AND is_available = 1',
      whereArgs: <Object?>[categoryId],
      orderBy: 'sort_order ASC, name ASC',
    );
  }

  Future<List<Map<String, Object?>>> activeOrders() {
    return _db.query(
      'orders',
      where: "status NOT IN ('closed', 'cancelled')",
      orderBy: 'updated_at DESC',
    );
  }

  Future<Map<String, Object?>?> order(String localOrderId) async {
    final rows = await _db.query(
      'orders',
      where: 'local_order_id = ?',
      whereArgs: <Object?>[localOrderId],
      limit: 1,
    );
    return rows.isEmpty ? null : rows.first;
  }

  Future<List<Map<String, Object?>>> orderItems(String localOrderId) {
    return _db.query(
      'order_items',
      where: 'local_order_id = ?',
      whereArgs: <Object?>[localOrderId],
      orderBy: 'rowid ASC',
    );
  }

  Future<int> pendingCount() async {
    final rows = await _db.rawQuery(
      "SELECT COUNT(*) AS total FROM outbox WHERE status = 'pending'",
    );
    return (rows.first['total'] as num?)?.toInt() ?? 0;
  }

  Future<int> conflictCount() async {
    final rows = await _db.rawQuery('SELECT COUNT(*) AS total FROM conflicts');
    return (rows.first['total'] as num?)?.toInt() ?? 0;
  }

  Future<List<Map<String, Object?>>> conflicts() {
    return _db.query('conflicts', orderBy: 'created_at DESC');
  }

  Future<void> createDraftOrder({
    required String clientOrderId,
    required String mutationId,
    required String tableId,
    required int guestCount,
    String? notes,
  }) async {
    final now = DateTime.now().toUtc().toIso8601String();

    await _db.transaction((txn) async {
      await txn.insert(
        'orders',
        <String, Object?>{
          'local_order_id': clientOrderId,
          'client_order_id': clientOrderId,
          'table_id': tableId,
          'status': 'draft',
          'guest_count': guestCount,
          'notes': notes,
          'subtotal': '0.00',
          'total': '0.00',
          'opened_at': now,
          'updated_at': now,
        },
      );
      await txn.update(
        'dining_tables',
        <String, Object?>{'status': 'occupied'},
        where: 'id = ?',
        whereArgs: <Object?>[tableId],
      );
      await _enqueue(
        txn,
        mutationId: mutationId,
        operation: 'order.open',
        payload: <String, Object?>{
          'client_order_id': clientOrderId,
          'dining_table_id': tableId,
          'guest_count': guestCount,
          'notes': notes,
        },
      );
    });
  }

  Future<void> addDraftItem({
    required String localOrderId,
    required String clientLineId,
    required String mutationId,
    required Map<String, Object?> menuItem,
    required int quantity,
    String? notes,
  }) async {
    await _db.transaction((txn) async {
      final orderRows = await txn.query(
        'orders',
        where: 'local_order_id = ?',
        whereArgs: <Object?>[localOrderId],
        limit: 1,
      );

      if (orderRows.isEmpty || orderRows.first['status'] != 'draft') {
        throw StateError('Only local draft orders can be edited.');
      }

      final unitPrice = menuItem['price']!.toString();
      final lineTotal = _multiplyMoney(unitPrice, quantity);

      await txn.insert(
        'order_items',
        <String, Object?>{
          'local_line_id': clientLineId,
          'client_line_id': clientLineId,
          'local_order_id': localOrderId,
          'menu_item_id': menuItem['id']!.toString(),
          'item_name': menuItem['name']!.toString(),
          'unit_price': unitPrice,
          'quantity': quantity,
          'line_total': lineTotal,
          'notes': notes,
          'status': 'pending',
        },
      );

      final totals = await txn.rawQuery(
        'SELECT line_total FROM order_items WHERE local_order_id = ?',
        <Object?>[localOrderId],
      );
      var subtotalMinor = 0;

      for (final row in totals) {
        subtotalMinor += _toMinor(row['line_total']!.toString());
      }

      final subtotal = _fromMinor(subtotalMinor);
      await txn.update(
        'orders',
        <String, Object?>{
          'subtotal': subtotal,
          'total': subtotal,
          'updated_at': DateTime.now().toUtc().toIso8601String(),
        },
        where: 'local_order_id = ?',
        whereArgs: <Object?>[localOrderId],
      );

      await _enqueue(
        txn,
        mutationId: mutationId,
        operation: 'order.item.add',
        payload: <String, Object?>{
          'client_order_id': localOrderId,
          'client_line_id': clientLineId,
          'menu_item_id': menuItem['id']!.toString(),
          'quantity': quantity,
          'notes': notes,
        },
      );
    });
  }

  Future<void> submitDraftOrder({
    required String localOrderId,
    required String mutationId,
  }) async {
    await _db.transaction((txn) async {
      final count = Sqflite.firstIntValue(
            await txn.rawQuery(
              'SELECT COUNT(*) FROM order_items WHERE local_order_id = ?',
              <Object?>[localOrderId],
            ),
          ) ??
          0;

      if (count < 1) {
        throw StateError('Add at least one item before submitting.');
      }

      await txn.update(
        'orders',
        <String, Object?>{
          'status': 'submitted_pending_sync',
          'submitted_at': DateTime.now().toUtc().toIso8601String(),
          'updated_at': DateTime.now().toUtc().toIso8601String(),
        },
        where: 'local_order_id = ? AND status = ?',
        whereArgs: <Object?>[localOrderId, 'draft'],
      );

      await _enqueue(
        txn,
        mutationId: mutationId,
        operation: 'order.submit',
        payload: <String, Object?>{'client_order_id': localOrderId},
      );
    });
  }

  Future<void> _applyChange(
    Transaction txn,
    Map<String, Object?> change,
  ) async {
    final entityType = change['entity_type']?.toString();
    final entityId = change['entity_id']?.toString();
    final operation = change['operation']?.toString();
    final rawData = change['data'];

    if (entityId == null) {
      return;
    }

    if (entityType == 'order') {
      if (operation == 'delete') {
        await txn.delete(
          'orders',
          where: 'server_id = ?',
          whereArgs: <Object?>[entityId],
        );
        return;
      }

      if (rawData is Map<Object?, Object?>) {
        await _upsertOrderSnapshot(
          txn,
          Map<String, Object?>.from(rawData),
        );
      }
      return;
    }

    if (entityType == 'dining_table') {
      if (operation == 'delete') {
        await txn.delete(
          'dining_tables',
          where: 'id = ?',
          whereArgs: <Object?>[entityId],
        );
      } else if (rawData is Map<Object?, Object?>) {
        await _upsertTable(txn, Map<String, Object?>.from(rawData));
      }
      return;
    }

    if (entityType == 'menu_category') {
      if (operation == 'delete') {
        await txn.delete(
          'menu_categories',
          where: 'id = ?',
          whereArgs: <Object?>[entityId],
        );
      } else if (rawData is Map<Object?, Object?>) {
        await _upsertCategory(txn, Map<String, Object?>.from(rawData));
      }
      return;
    }

    if (entityType == 'menu_item') {
      if (operation == 'delete') {
        await txn.delete(
          'menu_items',
          where: 'id = ?',
          whereArgs: <Object?>[entityId],
        );
      } else if (rawData is Map<Object?, Object?>) {
        final item = Map<String, Object?>.from(rawData);
        await _upsertMenuItem(
          txn,
          item,
          item['menu_category_id']?.toString(),
        );
      }
      return;
    }

    if (entityType == 'dining_area' && rawData is Map<Object?, Object?>) {
      final area = Map<String, Object?>.from(rawData);
      await txn.update(
        'dining_tables',
        <String, Object?>{'area_name': area['name']?.toString()},
        where: 'area_id = ?',
        whereArgs: <Object?>[entityId],
      );
    }
  }

  Future<void> _upsertCategory(
    DatabaseExecutor txn,
    Map<String, Object?> category,
  ) async {
    await txn.insert(
      'menu_categories',
      <String, Object?>{
        'id': category['id']!.toString(),
        'name': category['name']!.toString(),
        'sort_order': (category['sort_order'] as num?)?.toInt() ?? 0,
        'is_active': _boolInt(category['is_active'], defaultValue: true),
      },
      conflictAlgorithm: ConflictAlgorithm.replace,
    );
  }

  Future<void> _upsertMenuItem(
    DatabaseExecutor txn,
    Map<String, Object?> item,
    String? categoryId,
  ) async {
    await txn.insert(
      'menu_items',
      <String, Object?>{
        'id': item['id']!.toString(),
        'category_id': categoryId ?? item['menu_category_id']?.toString(),
        'sku': item['sku']?.toString(),
        'name': item['name']!.toString(),
        'description': item['description']?.toString(),
        'image_url': item['image_url']?.toString(),
        'price': item['price']!.toString(),
        'sort_order': (item['sort_order'] as num?)?.toInt() ?? 0,
        'is_available': _boolInt(
          item['is_available'],
          defaultValue: true,
        ),
      },
      conflictAlgorithm: ConflictAlgorithm.replace,
    );
  }

  Future<void> _upsertTable(
    DatabaseExecutor txn,
    Map<String, Object?> table,
  ) async {
    final area = table['area'] is Map<Object?, Object?>
        ? Map<String, Object?>.from(table['area']! as Map<Object?, Object?>)
        : <String, Object?>{};
    final branch = table['branch'] is Map<Object?, Object?>
        ? Map<String, Object?>.from(table['branch']! as Map<Object?, Object?>)
        : <String, Object?>{};

    await txn.insert(
      'dining_tables',
      <String, Object?>{
        'id': table['id']!.toString(),
        'code': table['code']!.toString(),
        'name': table['name']!.toString(),
        'capacity': (table['capacity'] as num?)?.toInt() ?? 1,
        'status': table['status']!.toString(),
        'is_active': _boolInt(table['is_active'], defaultValue: true),
        'area_id': area['id']?.toString(),
        'area_name': area['name']?.toString(),
        'branch_id': branch['id']?.toString(),
        'branch_name': branch['name']?.toString(),
      },
      conflictAlgorithm: ConflictAlgorithm.replace,
    );
  }

  Future<void> _upsertOrderSnapshot(
    DatabaseExecutor txn,
    Map<String, Object?> order,
  ) async {
    final serverId = order['id']!.toString();
    final clientId = order['client_order_id']?.toString();
    final localId = clientId == null || clientId.isEmpty
        ? 'server:$serverId'
        : clientId;
    final table = Map<String, Object?>.from(
      order['table']! as Map<Object?, Object?>,
    );

    await txn.insert(
      'orders',
      <String, Object?>{
        'local_order_id': localId,
        'client_order_id': clientId,
        'server_id': serverId,
        'table_id': table['id']!.toString(),
        'status': order['status']!.toString(),
        'guest_count': (order['guest_count'] as num?)?.toInt() ?? 1,
        'notes': order['notes']?.toString(),
        'subtotal': order['subtotal']?.toString() ?? '0.00',
        'total': order['total']?.toString() ?? '0.00',
        'opened_at': order['opened_at']?.toString(),
        'submitted_at': order['submitted_at']?.toString(),
        'served_at': order['served_at']?.toString(),
        'closed_at': order['closed_at']?.toString(),
        'updated_at': DateTime.now().toUtc().toIso8601String(),
      },
      conflictAlgorithm: ConflictAlgorithm.replace,
    );

    for (final rawItem in order['items'] as List<Object?>? ?? const []) {
      final item = Map<String, Object?>.from(
        rawItem! as Map<Object?, Object?>,
      );
      final serverLineId = item['id']!.toString();
      final clientLineId = item['client_line_id']?.toString();
      final localLineId = clientLineId == null || clientLineId.isEmpty
          ? 'server:$serverLineId'
          : clientLineId;

      await txn.insert(
        'order_items',
        <String, Object?>{
          'local_line_id': localLineId,
          'client_line_id': clientLineId,
          'server_id': serverLineId,
          'local_order_id': localId,
          'menu_item_id': item['menu_item_id']?.toString(),
          'item_name': item['item_name']!.toString(),
          'unit_price': item['unit_price']!.toString(),
          'quantity': (item['quantity'] as num?)?.toInt() ?? 1,
          'line_total': item['line_total']!.toString(),
          'notes': item['notes']?.toString(),
          'status': item['status']!.toString(),
        },
        conflictAlgorithm: ConflictAlgorithm.replace,
      );
    }
  }

  Future<void> _reapplyLocalOccupancy(DatabaseExecutor txn) async {
    final rows = await txn.rawQuery(
      "SELECT DISTINCT table_id FROM orders WHERE status IN ('draft', 'submitted_pending_sync')",
    );

    for (final row in rows) {
      await txn.update(
        'dining_tables',
        <String, Object?>{'status': 'occupied'},
        where: 'id = ?',
        whereArgs: <Object?>[row['table_id']],
      );
    }
  }

  Future<void> _enqueue(
    Transaction txn, {
    required String mutationId,
    required String operation,
    required Map<String, Object?> payload,
  }) async {
    await txn.insert(
      'outbox',
      <String, Object?>{
        'mutation_id': mutationId,
        'operation': operation,
        'payload_json': jsonEncode(payload),
        'occurred_at': DateTime.now().toUtc().toIso8601String(),
        'status': 'pending',
        'attempts': 0,
      },
    );
  }

  Future<void> _setSettingTxn(
    DatabaseExecutor txn,
    String key,
    String value,
  ) async {
    await txn.insert(
      'settings',
      <String, Object?>{'key': key, 'value': value},
      conflictAlgorithm: ConflictAlgorithm.replace,
    );
  }

  int _boolInt(Object? value, {required bool defaultValue}) {
    if (value == null) {
      return defaultValue ? 1 : 0;
    }
    if (value is bool) {
      return value ? 1 : 0;
    }
    if (value is num) {
      return value == 0 ? 0 : 1;
    }
    return value.toString() == '0' ? 0 : 1;
  }

  String _multiplyMoney(String amount, int quantity) {
    return _fromMinor(_toMinor(amount) * quantity);
  }

  int _toMinor(String amount) {
    final parts = amount.split('.');
    final whole = int.parse(parts.first);
    final fraction = parts.length > 1
        ? parts[1].padRight(2, '0').substring(0, 2)
        : '00';
    return (whole * 100) + int.parse(fraction);
  }

  String _fromMinor(int amount) {
    final sign = amount < 0 ? '-' : '';
    final absolute = amount.abs();
    final whole = absolute ~/ 100;
    final fraction = (absolute % 100).toString().padLeft(2, '0');
    return '$sign$whole.$fraction';
  }
}
