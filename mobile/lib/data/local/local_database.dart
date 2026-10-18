import 'dart:convert';

import 'package:path/path.dart' as p;
import 'package:sqflite/sqflite.dart';

import '../../sync/sync_store.dart';
import 'outbox_mutation.dart';

class LocalDatabase implements SyncStore {
  LocalDatabase._(this._db);

  final Database _db;

  static Future<LocalDatabase> open({String? databasePath}) async {
    final path = databasePath ??
        p.join(await getDatabasesPath(), 'businessos_restaurant_waiter.db');
    final database = await openDatabase(
      path,
      version: 9,
      onCreate: (db, version) async {
        await db.execute('''
          CREATE TABLE settings (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
          )
        ''');
        await db.execute('''
          CREATE TABLE branches (
            id TEXT PRIMARY KEY,
            code TEXT NOT NULL,
            name TEXT NOT NULL,
            is_active INTEGER NOT NULL DEFAULT 1
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
            modifier_groups_json TEXT,
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
            display_number INTEGER NOT NULL DEFAULT 0,
            table_id TEXT,
            branch_id TEXT,
            service_type TEXT NOT NULL DEFAULT 'dine_in',
            service_reference TEXT,
            status TEXT NOT NULL,
            guest_count INTEGER NOT NULL,
            notes TEXT,
            subtotal TEXT NOT NULL DEFAULT '0.00',
            total TEXT NOT NULL DEFAULT '0.00',
            opened_at TEXT,
            submitted_at TEXT,
            served_at TEXT,
            closed_at TEXT,
            kot_rounds_json TEXT NOT NULL DEFAULT '[]',
            kitchen_tickets_json TEXT NOT NULL DEFAULT '[]',
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
            dispatched_quantity INTEGER NOT NULL DEFAULT 0,
            line_total TEXT NOT NULL,
            notes TEXT,
            seat_number INTEGER,
            course_number INTEGER,
            course_name TEXT,
            course_state TEXT NOT NULL DEFAULT 'open',
            modifiers_snapshot_json TEXT,
            allergy_instructions TEXT,
            kitchen_instructions TEXT,
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
            created_at TEXT NOT NULL,
            resolved_at TEXT,
            resolution TEXT
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
          await db.execute('ALTER TABLE menu_items ADD COLUMN image_url TEXT');
        }
        if (oldVersion < 3) {
          await db.execute(
            'ALTER TABLE menu_items ADD COLUMN modifier_groups_json TEXT',
          );
          await db.execute('ALTER TABLE orders ADD COLUMN branch_id TEXT');
          await db.execute(
            "ALTER TABLE orders ADD COLUMN service_type TEXT NOT NULL DEFAULT 'dine_in'",
          );
          await db.execute(
            'ALTER TABLE orders ADD COLUMN service_reference TEXT',
          );
          await db.execute(
            'ALTER TABLE order_items ADD COLUMN dispatched_quantity INTEGER NOT NULL DEFAULT 0',
          );
          await db.execute(
            'ALTER TABLE order_items ADD COLUMN seat_number INTEGER',
          );
          await db.execute(
            'ALTER TABLE order_items ADD COLUMN course_number INTEGER',
          );
          await db.execute(
            'ALTER TABLE order_items ADD COLUMN course_name TEXT',
          );
          await db.execute(
            'ALTER TABLE order_items ADD COLUMN modifiers_snapshot_json TEXT',
          );
          await db.execute(
            'ALTER TABLE order_items ADD COLUMN allergy_instructions TEXT',
          );
          await db.execute(
            'ALTER TABLE order_items ADD COLUMN kitchen_instructions TEXT',
          );
        }
        if (oldVersion < 4) {
          await db.execute(
            "ALTER TABLE order_items ADD COLUMN course_state TEXT NOT NULL DEFAULT 'open'",
          );
        }
        if (oldVersion < 5) {
          await db.execute('''
            CREATE TABLE branches (
              id TEXT PRIMARY KEY,
              code TEXT NOT NULL,
              name TEXT NOT NULL,
              is_active INTEGER NOT NULL DEFAULT 1
            )
          ''');
        }
        if (oldVersion < 6) {
          await db.execute(
            "ALTER TABLE orders ADD COLUMN kot_rounds_json TEXT NOT NULL DEFAULT '[]'",
          );
          await db.execute(
            "ALTER TABLE orders ADD COLUMN kitchen_tickets_json TEXT NOT NULL DEFAULT '[]'",
          );
        }
        if (oldVersion < 7) {
          // Existing installs built from the original schema have a NOT NULL
          // table_id despite fresh installs already permitting null. Rebuild
          // only when necessary, preserving orders and the durable outbox.
          await _upgradeNullableOrderTable(db);
        }
        if (oldVersion < 8) {
          await db.execute(
            'ALTER TABLE orders ADD COLUMN display_number INTEGER NOT NULL DEFAULT 0',
          );
        }
        if (oldVersion < 9) {
          // Legacy v6 databases can omit the conflicts table altogether.
          // Add missing columns only when they do not already exist.
          await db.execute('''
            CREATE TABLE IF NOT EXISTS conflicts (
              mutation_id TEXT PRIMARY KEY,
              operation TEXT NOT NULL,
              code TEXT NOT NULL,
              message TEXT NOT NULL,
              payload_json TEXT NOT NULL,
              created_at TEXT NOT NULL,
              resolved_at TEXT,
              resolution TEXT
            )
          ''');
          final fields = (await db.rawQuery('PRAGMA table_info(conflicts)'))
              .map((row) => row['name']?.toString())
              .toSet();
          if (!fields.contains('resolved_at')) {
            await db.execute('ALTER TABLE conflicts ADD COLUMN resolved_at TEXT');
          }
          if (!fields.contains('resolution')) {
            await db.execute('ALTER TABLE conflicts ADD COLUMN resolution TEXT');
          }
        }
      },
    );

    return LocalDatabase._(database);
  }

  Future<void> close() => _db.close();

  static Future<void> _upgradeNullableOrderTable(DatabaseExecutor db) async {
    final columns = await db.rawQuery('PRAGMA table_info(orders)');
    final oldTableId = columns.where((row) => row['name'] == 'table_id');
    if (oldTableId.isEmpty || oldTableId.first['notnull'] != 1) return;

    // SQLite cannot DROP a NOT NULL column constraint in place.
    // onUpgrade executes inside the database upgrade transaction.
    await db.execute('''
      CREATE TABLE orders_nullable_upgrade (
        local_order_id TEXT PRIMARY KEY,
        client_order_id TEXT,
        server_id TEXT,
        table_id TEXT,
        branch_id TEXT,
        service_type TEXT NOT NULL DEFAULT 'dine_in',
        service_reference TEXT,
        status TEXT NOT NULL,
        guest_count INTEGER NOT NULL,
        notes TEXT,
        subtotal TEXT NOT NULL DEFAULT '0.00',
        total TEXT NOT NULL DEFAULT '0.00',
        opened_at TEXT,
        submitted_at TEXT,
        served_at TEXT,
        closed_at TEXT,
        kot_rounds_json TEXT NOT NULL DEFAULT '[]',
        kitchen_tickets_json TEXT NOT NULL DEFAULT '[]',
        updated_at TEXT NOT NULL
      )
    ''');
    const fields = 'local_order_id, client_order_id, server_id, table_id, '
        'branch_id, service_type, service_reference, status, guest_count, '
        'notes, subtotal, total, opened_at, submitted_at, served_at, '
        'closed_at, kot_rounds_json, kitchen_tickets_json, updated_at';
    await db.execute(
      'INSERT INTO orders_nullable_upgrade ($fields) SELECT $fields FROM orders',
    );
    await db.execute('DROP TABLE orders');
    await db.execute('ALTER TABLE orders_nullable_upgrade RENAME TO orders');
    await db.execute(
      'CREATE INDEX IF NOT EXISTS orders_status_idx ON orders(status, updated_at)',
    );
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

    return rows
        .map((row) {
          return OutboxMutation(
            id: row['id']! as int,
            mutationId: row['mutation_id']! as String,
            operation: row['operation']! as String,
            payload: Map<String, Object?>.from(
              jsonDecode(row['payload_json']! as String)
                  as Map<String, dynamic>,
            ),
            occurredAt: DateTime.parse(row['occurred_at']! as String),
            attempts: row['attempts']! as int,
          );
        })
        .toList(growable: false);
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
      await txn.delete('branches');

      for (final rawBranch in data['branches'] as List<Object?>? ?? const []) {
        final branch = Map<String, Object?>.from(
          rawBranch! as Map<Object?, Object?>,
        );
        await txn.insert(
          'branches',
          <String, Object?>{
            'id': branch['id']!.toString(),
            'code': branch['code']!.toString(),
            'name': branch['name']!.toString(),
            'is_active': _boolInt(branch['is_active'], defaultValue: true),
          },
          conflictAlgorithm: ConflictAlgorithm.replace,
        );
      }

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

      final serverOrderIds = <String>{};
      for (final rawOrder in data['orders'] as List<Object?>? ?? const []) {
        final order = Map<String, Object?>.from(rawOrder! as Map<Object?, Object?>);
        final clientId = order['client_order_id']?.toString();
        final serverId = order['id']?.toString();
        if (clientId != null && clientId.isNotEmpty) {
          serverOrderIds.add(clientId);
        } else if (serverId != null && serverId.isNotEmpty) {
          serverOrderIds.add('server:$serverId');
        }
        await _upsertOrderSnapshot(txn, order);
      }

      // The LAN is the operational authority for the in-restaurant floor.
      // An active server-backed order absent from its complete waiter snapshot
      // is no longer actionable on this device. Do not delete its record or
      // touch locally queued offline mutations.
      if (cursorScope == 'local') {
        final oldOrders = await txn.query(
          'orders',
          columns: <String>['local_order_id'],
          where: "server_id IS NOT NULL AND status NOT IN ('closed', 'cancelled', 'inactive_remote')",
        );
        for (final row in oldOrders) {
          final localId = row['local_order_id']!.toString();
          if (serverOrderIds.contains(localId)) continue;
          final unsent = await txn.rawQuery(
            r'''SELECT 1 FROM outbox
               WHERE status = 'pending'
                 AND json_extract(payload_json, '$.client_order_id') = ?
               LIMIT 1''',
            <Object?>[localId],
          );
          if (unsent.isNotEmpty) continue;
          await txn.update(
            'orders',
            <String, Object?>{
              'status': 'inactive_remote',
              'updated_at': DateTime.now().toUtc().toIso8601String(),
            },
            where: 'local_order_id = ?',
            whereArgs: <Object?>[localId],
          );
        }
      }

      final restaurantSettings = data['restaurant_settings'];
      if (restaurantSettings is Map<Object?, Object?>) {
        await _setSettingTxn(
          txn,
          'restaurant_settings_json',
          jsonEncode(Map<String, Object?>.from(restaurantSettings)),
        );
      }

      await _setSettingTxn(
        txn,
        'sync_cursor_' + cursorScope,
        ((data['cursor'] as num?)?.toInt() ?? 0).toString(),
      );
      await _setSettingTxn(txn, 'server_locked', '0');
      // A successful setup/bootstrap supersedes any pre-login
      // not_configured sync error left over from a prior session.
      await _setSettingTxn(txn, 'last_sync_error', '');
      await _reapplyLocalOccupancy(txn);
      await _reconcileSubmittedConflicts(txn);
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
      await _reconcileSubmittedConflicts(txn);
    });
  }

  @override
  Future<void> applyAcceptedResult(Map<String, Object?> result) async {
    await _db.transaction((txn) async {
      final mutationId = result['mutation_id']!.toString();
      final data = result['data'];

      if (data is Map<Object?, Object?>) {
        final typed = Map<String, Object?>.from(data);
        final entityType = result['entity_type']?.toString();
        final source = typed['source'];
        final target = typed['target'];

        if (source is Map<Object?, Object?>) {
          await _upsertOrderSnapshot(txn, Map<String, Object?>.from(source));
        }
        if (target is Map<Object?, Object?>) {
          await _upsertOrderSnapshot(txn, Map<String, Object?>.from(target));
        }

        final order = switch (entityType) {
          'order_item' || 'kitchen_ticket_item' => typed['order'],
          _ when source != null || target != null => null,
          _ => typed,
        };

        if (order is Map<Object?, Object?>) {
          await _upsertOrderSnapshot(txn, Map<String, Object?>.from(order));
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
      // A successful LAN request can still be rejected when another terminal
      // already cancelled/closed this order. Reconcile the authoritative
      // terminal status, preserving the rejected mutation in Conflicts.
      final clientOrderId = result['client_order_id']?.toString();
      final serverStatus = result['server_order_status']?.toString();
      if (result['code'] == 'order_state_conflict' &&
          clientOrderId == mutation.payload['client_order_id'] &&
          const <String>{'closed', 'cancelled', 'billed'}.contains(serverStatus)) {
        await txn.update(
          'orders',
          <String, Object?>{
            'status': serverStatus,
            'updated_at': DateTime.now().toUtc().toIso8601String(),
          },
          where: 'local_order_id = ?',
          whereArgs: <Object?>[clientOrderId],
        );
      }
      await txn.update(
        'outbox',
        <String, Object?>{
          'status': 'conflict',
          'last_error': result['code']?.toString() ?? 'conflict',
        },
        where: 'mutation_id = ?',
        whereArgs: <Object?>[mutation.mutationId],
      );
      await txn.insert('conflicts', <String, Object?>{
        'mutation_id': mutation.mutationId,
        'operation': mutation.operation,
        'code': result['code']?.toString() ?? 'conflict',
        'message': result['message']?.toString() ?? 'Sync conflict',
        'payload_json': jsonEncode(mutation.payload),
        'created_at': DateTime.now().toUtc().toIso8601String(),
      }, conflictAlgorithm: ConflictAlgorithm.replace);
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
    await _db.insert('settings', <String, Object?>{
      'key': key,
      'value': value,
    }, conflictAlgorithm: ConflictAlgorithm.replace);
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

  Future<Map<String, Object?>> restaurantSettings() async {
    final raw = await systemState('restaurant_settings_json');

    if (raw == null || raw.isEmpty) {
      return const <String, Object?>{
        'kitchen_queue_enabled': true,
        'preparing_stage_enabled': true,
        'expo_enabled': false,
        'courses_enabled': false,
        'kot_sound_enabled': true,
        'kitchen_warning_minutes': 10,
        'kitchen_late_minutes': 20,
        'require_manager_approval_post_kot_void': false,
        'negative_stock_policy': 'block',
      };
    }

    return Map<String, Object?>.from(
      jsonDecode(raw) as Map<String, dynamic>,
    );
  }

  Future<List<Map<String, Object?>>> branches() {
    return _db.query(
      'branches',
      where: 'is_active = 1',
      orderBy: 'name ASC',
    );
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
      where: "status NOT IN ('closed', 'cancelled', 'inactive_remote')",
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

  Future<void> fireCourse({
    required String localOrderId,
    required int courseNumber,
    required String mutationId,
    String priority = 'normal',
  }) async {
    await _db.transaction((txn) async {
      await txn.update(
        'order_items',
        <String, Object?>{'course_state': 'fired'},
        where:
            "local_order_id = ? AND course_number = ? AND course_state = 'held'",
        whereArgs: <Object?>[localOrderId, courseNumber],
      );

      await _enqueue(
        txn,
        mutationId: mutationId,
        operation: 'order.course.fire',
        payload: <String, Object?>{
          'client_order_id': localOrderId,
          'course_number': courseNumber,
          'priority': priority,
        },
      );
    });
  }

  Future<void> enqueueKitchenItemOperation({
    required String mutationId,
    required String operation,
    required String kitchenTicketItemId,
    required String reason,
  }) async {
    const allowed = <String>{
      'order.item.void',
      'order.item.refire',
      'order.item.recall',
    };

    if (!allowed.contains(operation)) {
      throw ArgumentError.value(operation, 'operation');
    }

    await _db.transaction((txn) async {
      await _enqueue(
        txn,
        mutationId: mutationId,
        operation: operation,
        payload: <String, Object?>{
          'kitchen_ticket_item_id': kitchenTicketItemId,
          'reason': reason,
        },
      );
    });
  }

  Future<void> transferOrderTable({
    required String localOrderId,
    required String targetTableId,
    required String mutationId,
  }) async {
    await _db.transaction((txn) async {
      final orderRows = await txn.query(
        'orders',
        where: 'local_order_id = ?',
        whereArgs: <Object?>[localOrderId],
        limit: 1,
      );

      if (orderRows.isEmpty) {
        throw StateError('Order not found.');
      }

      final currentTableId = orderRows.first['table_id']?.toString();

      if (currentTableId == null || currentTableId.isEmpty) {
        throw StateError('Only dine-in orders can be transferred.');
      }

      await txn.update(
        'orders',
        <String, Object?>{
          'table_id': targetTableId,
          'updated_at': DateTime.now().toUtc().toIso8601String(),
        },
        where: 'local_order_id = ?',
        whereArgs: <Object?>[localOrderId],
      );

      await txn.update(
        'dining_tables',
        <String, Object?>{'status': 'available'},
        where: 'id = ?',
        whereArgs: <Object?>[currentTableId],
      );
      await txn.update(
        'dining_tables',
        <String, Object?>{'status': 'occupied'},
        where: 'id = ?',
        whereArgs: <Object?>[targetTableId],
      );

      await _enqueue(
        txn,
        mutationId: mutationId,
        operation: 'order.table.transfer',
        payload: <String, Object?>{
          'client_order_id': localOrderId,
          'target_table_id': targetTableId,
        },
      );
    });
  }

  Future<void> moveUnsentOrderItem({
    required String sourceLocalOrderId,
    required String targetLocalOrderId,
    required String localLineId,
    required String targetLocalLineId,
    required int quantity,
    required String mutationId,
  }) async {
    await _db.transaction((txn) async {
      final rows = await txn.query(
        'order_items',
        where: 'local_order_id = ? AND local_line_id = ?',
        whereArgs: <Object?>[sourceLocalOrderId, localLineId],
        limit: 1,
      );

      if (rows.isEmpty) {
        throw StateError('Order item not found.');
      }

      final line = rows.first;
      final totalQuantity = (line['quantity'] as num?)?.toInt() ?? 0;
      final dispatched = (line['dispatched_quantity'] as num?)?.toInt() ?? 0;
      final unsent = totalQuantity - dispatched;

      if (quantity < 1 || quantity > unsent) {
        throw StateError('Only unsent quantity can be moved.');
      }

      final unitPrice = line['unit_price']!.toString();

      await txn.insert('order_items', <String, Object?>{
        ...line,
        'local_line_id': targetLocalLineId,
        'client_line_id': targetLocalLineId,
        'server_id': null,
        'local_order_id': targetLocalOrderId,
        'quantity': quantity,
        'dispatched_quantity': 0,
        'line_total': _multiplyMoney(unitPrice, quantity),
        'status': 'pending',
      });

      final remaining = totalQuantity - quantity;

      if (remaining == 0) {
        await txn.delete(
          'order_items',
          where: 'local_line_id = ?',
          whereArgs: <Object?>[localLineId],
        );
      } else {
        await txn.update(
          'order_items',
          <String, Object?>{
            'quantity': remaining,
            'line_total': _multiplyMoney(unitPrice, remaining),
          },
          where: 'local_line_id = ?',
          whereArgs: <Object?>[localLineId],
        );
      }

      await _recalculateOrderTxn(txn, sourceLocalOrderId);
      await _recalculateOrderTxn(txn, targetLocalOrderId);

      await _enqueue(
        txn,
        mutationId: mutationId,
        operation: 'order.item.move',
        payload: <String, Object?>{
          'source_client_order_id': sourceLocalOrderId,
          'target_client_order_id': targetLocalOrderId,
          'client_line_id': line['client_line_id']?.toString() ?? localLineId,
          'target_client_line_id': targetLocalLineId,
          'quantity': quantity,
        },
      );
    });
  }

  Future<void> mergeOrders({
    required String sourceLocalOrderId,
    required String targetLocalOrderId,
    required String mutationId,
  }) async {
    await _db.transaction((txn) async {
      final sourceLines = await txn.query(
        'order_items',
        where: 'local_order_id = ?',
        whereArgs: <Object?>[sourceLocalOrderId],
      );

      if (sourceLines.any(
        (line) => ((line['dispatched_quantity'] as num?)?.toInt() ?? 0) > 0,
      )) {
        throw StateError(
          'Orders with dispatched kitchen production cannot be fully merged.',
        );
      }

      final sourceOrders = await txn.query(
        'orders',
        where: 'local_order_id = ?',
        whereArgs: <Object?>[sourceLocalOrderId],
        limit: 1,
      );

      if (sourceOrders.isEmpty) {
        throw StateError('Source order not found.');
      }

      for (final line in sourceLines) {
        await txn.update(
          'order_items',
          <String, Object?>{'local_order_id': targetLocalOrderId},
          where: 'local_line_id = ?',
          whereArgs: <Object?>[line['local_line_id']],
        );
      }

      final sourceTableId = sourceOrders.first['table_id']?.toString();

      if (sourceTableId != null && sourceTableId.isNotEmpty) {
        await txn.update(
          'dining_tables',
          <String, Object?>{'status': 'available'},
          where: 'id = ?',
          whereArgs: <Object?>[sourceTableId],
        );
      }

      await txn.update(
        'orders',
        <String, Object?>{
          'status': 'cancelled',
          'subtotal': '0.00',
          'total': '0.00',
          'closed_at': DateTime.now().toUtc().toIso8601String(),
          'updated_at': DateTime.now().toUtc().toIso8601String(),
        },
        where: 'local_order_id = ?',
        whereArgs: <Object?>[sourceLocalOrderId],
      );

      await _recalculateOrderTxn(txn, targetLocalOrderId);

      await _enqueue(
        txn,
        mutationId: mutationId,
        operation: 'order.merge',
        payload: <String, Object?>{
          'source_client_order_id': sourceLocalOrderId,
          'target_client_order_id': targetLocalOrderId,
        },
      );
    });
  }

  Future<bool> hasUnsentItems(String localOrderId) async {
    final rows = await _db.rawQuery(
      "SELECT COUNT(*) AS total FROM order_items WHERE local_order_id = ? AND dispatched_quantity < quantity AND course_state != 'held'",
      <Object?>[localOrderId],
    );
    return ((rows.first['total'] as num?)?.toInt() ?? 0) > 0;
  }

  /// The latest kitchen-submit mutation stays in the outbox until accepted.
  /// A null result means no submit remains queued or conflicted for this order.
  Future<String?> kitchenSubmissionStatus(String localOrderId) async {
    final rows = await _db.query(
      'outbox',
      columns: <String>['payload_json', 'status'],
      where: "operation = 'order.submit'",
      orderBy: 'id DESC',
    );
    for (final row in rows) {
      final payload = jsonDecode(row['payload_json']! as String)
          as Map<String, dynamic>;
      if (payload['client_order_id'] == localOrderId) {
        return row['status'] as String?;
      }
    }
    return null;
  }

  Future<int> pendingCount() async {
    final rows = await _db.rawQuery(
      "SELECT COUNT(*) AS total FROM outbox WHERE status = 'pending'",
    );
    return (rows.first['total'] as num?)?.toInt() ?? 0;
  }

  Future<int> conflictCount() async {
    final rows = await _db.rawQuery(
      'SELECT COUNT(*) AS total FROM conflicts WHERE resolved_at IS NULL',
    );
    return (rows.first['total'] as num?)?.toInt() ?? 0;
  }

  Future<List<Map<String, Object?>>> conflicts({bool archived = false}) {
    return _db.query(
      'conflicts',
      where: archived ? 'resolved_at IS NOT NULL' : 'resolved_at IS NULL',
      orderBy: 'created_at DESC',
    );
  }

  /// Deliberately retain the rejected operation and its audit record.
  /// Acknowledgement is a user decision, NOT a retry or an acceptance.
  Future<void> acknowledgeConflict(String mutationId) async {
    await _db.update(
      'conflicts',
      <String, Object?>{
        'resolved_at': DateTime.now().toUtc().toIso8601String(),
        'resolution': 'Manually acknowledged after review. '
            'The rejected operation was NOT applied or replayed.',
      },
      where: 'mutation_id = ? AND resolved_at IS NULL',
      whereArgs: <Object?>[mutationId],
    );
  }

  /// Only reconcile a duplicate kitchen submission once the desktop has
  /// confirmed it is already dispatched or the order is terminal.
  /// Rejected item additions and payments remain unresolved for review.
  Future<void> _reconcileSubmittedConflicts(DatabaseExecutor txn) async {
    final candidates = await txn.query(
      'conflicts',
      columns: <String>['mutation_id', 'payload_json'],
      where: "resolved_at IS NULL AND operation = 'order.submit' AND code = 'order_state_conflict'",
    );
    for (final conflict in candidates) {
      final payload = jsonDecode(conflict['payload_json']! as String);
      if (payload is! Map<String, dynamic>) continue;
      final localId = payload['client_order_id']?.toString();
      if (localId == null || localId.isEmpty) continue;
      final orders = await txn.query(
        'orders',
        columns: <String>['status', 'server_id', 'kot_rounds_json'],
        where: 'local_order_id = ?',
        whereArgs: <Object?>[localId],
        limit: 1,
      );
      if (orders.isEmpty || orders.first['server_id'] == null) continue;
      final status = orders.first['status']?.toString();
      final terminal = const <String>{
        'closed', 'cancelled', 'billed', 'served', 'inactive_remote',
      }.contains(status);
      final rounds = jsonDecode(
        orders.first['kot_rounds_json']?.toString() ?? '[]',
      );
      final hasDispatchedRound = rounds is List && rounds.isNotEmpty;
      final unsent = Sqflite.firstIntValue(await txn.rawQuery(
        'SELECT COUNT(*) FROM order_items WHERE local_order_id = ? AND dispatched_quantity < quantity',
        <Object?>[localId],
      )) ?? 0;
      if (!terminal && !(hasDispatchedRound && unsent == 0)) continue;
      await txn.update(
        'conflicts',
        <String, Object?>{
          'resolved_at': DateTime.now().toUtc().toIso8601String(),
          'resolution': 'Duplicate KOT submission superseded by confirmed '
              'desktop state; the rejected request was not replayed.',
        },
        where: 'mutation_id = ? AND resolved_at IS NULL',
        whereArgs: <Object?>[conflict['mutation_id']],
      );
    }
  }

  Future<void> createDraftOrder({
    required String clientOrderId,
    required String mutationId,
    String? tableId,
    required String branchId,
    required String serviceType,
    String? serviceReference,
    required int guestCount,
    String? notes,
  }) async {
    final now = DateTime.now().toUtc().toIso8601String();

    await _db.transaction((txn) async {
      await txn.insert('orders', <String, Object?>{
        'local_order_id': clientOrderId,
        'client_order_id': clientOrderId,
        'table_id': tableId,
        'branch_id': branchId,
        'service_type': serviceType,
        'service_reference': serviceReference,
        'status': 'draft',
        'guest_count': guestCount,
        'notes': notes,
        'subtotal': '0.00',
        'total': '0.00',
        'opened_at': now,
        'updated_at': now,
      });
      if (tableId != null) {
        await txn.update(
          'dining_tables',
          <String, Object?>{'status': 'occupied'},
          where: 'id = ?',
          whereArgs: <Object?>[tableId],
        );
      }
      await _enqueue(
        txn,
        mutationId: mutationId,
        operation: 'order.open',
        payload: <String, Object?>{
          'client_order_id': clientOrderId,
          'branch_id': branchId,
          'service_type': serviceType,
          'service_reference': serviceReference,
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
    int? seatNumber,
    int? courseNumber,
    String? courseName,
    bool holdForCourse = false,
    List<Map<String, Object?>> modifiers = const [],
    String? allergyInstructions,
    String? kitchenInstructions,
  }) async {
    await _db.transaction((txn) async {
      final orderRows = await txn.query(
        'orders',
        where: 'local_order_id = ?',
        whereArgs: <Object?>[localOrderId],
        limit: 1,
      );

      const editableStatuses = <String>{
        'draft',
        'submitted',
        'submitted_pending_sync',
        'preparing',
        'ready',
        'served',
      };
      final status = orderRows.isEmpty
          ? null
          : orderRows.first['status']?.toString();

      if (status == null || !editableStatuses.contains(status)) {
        throw StateError('This order can no longer receive new items.');
      }

      final modifierResolution = _resolveLocalModifiers(
        menuItem,
        modifiers,
      );
      final unitPrice = _fromMinor(
        _toMinor(menuItem['price']!.toString()) + modifierResolution.$1,
      );
      final lineTotal = _multiplyMoney(unitPrice, quantity);

      await txn.insert('order_items', <String, Object?>{
        'local_line_id': clientLineId,
        'client_line_id': clientLineId,
        'local_order_id': localOrderId,
        'menu_item_id': menuItem['id']!.toString(),
        'item_name': menuItem['name']!.toString(),
        'unit_price': unitPrice,
        'quantity': quantity,
        'dispatched_quantity': 0,
        'line_total': lineTotal,
        'notes': notes,
        'seat_number': seatNumber,
        'course_number': courseNumber,
        'course_name': courseName,
        'course_state': holdForCourse ? 'held' : 'open',
        'modifiers_snapshot_json': jsonEncode(modifierResolution.$2),
        'allergy_instructions': allergyInstructions,
        'kitchen_instructions': kitchenInstructions,
        'status': 'pending',
      });

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
          'seat_number': seatNumber,
          'course_number': courseNumber,
          'course_name': courseName,
          'hold_for_course': holdForCourse,
          'modifiers': modifiers,
          'allergy_instructions': allergyInstructions,
          'kitchen_instructions': kitchenInstructions,
        },
      );
    });
  }

  Future<void> submitDraftOrder({
    required String localOrderId,
    required String mutationId,
  }) async {
    await _db.transaction((txn) async {
      final orderRows = await txn.query(
        'orders',
        columns: <String>['status'],
        where: 'local_order_id = ?',
        whereArgs: <Object?>[localOrderId],
        limit: 1,
      );
      final status = orderRows.isEmpty ? null : orderRows.first['status']?.toString();
      if (status == null ||
          !const <String>{
            'draft', 'submitted', 'submitted_pending_sync',
            'preparing', 'ready', 'served',
          }.contains(status)) {
        throw StateError(
          'This order is closed or cancelled. Start a new order before sending to Kitchen.',
        );
      }
      final unsentCount =
          Sqflite.firstIntValue(
            await txn.rawQuery(
              "SELECT COUNT(*) FROM order_items WHERE local_order_id = ? AND dispatched_quantity < quantity AND course_state != 'held'",
              <Object?>[localOrderId],
            ),
          ) ??
          0;

      if (unsentCount < 1) {
        throw StateError('There are no new items to send to Kitchen.');
      }

      final now = DateTime.now().toUtc().toIso8601String();
      await txn.update(
        'orders',
        <String, Object?>{
          'status': 'submitted_pending_sync',
          'submitted_at': now,
          'updated_at': now,
        },
        where: "local_order_id = ? AND status NOT IN ('closed', 'cancelled')",
        whereArgs: <Object?>[localOrderId],
      );
      await txn.rawUpdate(
        "UPDATE order_items SET dispatched_quantity = quantity, status = 'submitted_pending_sync' WHERE local_order_id = ? AND dispatched_quantity < quantity AND course_state != 'held'",
        <Object?>[localOrderId],
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
        await _upsertOrderSnapshot(txn, Map<String, Object?>.from(rawData));
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
        await _upsertMenuItem(txn, item, item['menu_category_id']?.toString());
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
    await txn.insert('menu_categories', <String, Object?>{
      'id': category['id']!.toString(),
      'name': category['name']!.toString(),
      'sort_order': (category['sort_order'] as num?)?.toInt() ?? 0,
      'is_active': _boolInt(category['is_active'], defaultValue: true),
    }, conflictAlgorithm: ConflictAlgorithm.replace);
  }

  Future<void> _upsertMenuItem(
    DatabaseExecutor txn,
    Map<String, Object?> item,
    String? categoryId,
  ) async {
    await txn.insert('menu_items', <String, Object?>{
      'id': item['id']!.toString(),
      'category_id': categoryId ?? item['menu_category_id']?.toString(),
      'sku': item['sku']?.toString(),
      'name': item['name']!.toString(),
      'description': item['description']?.toString(),
      'image_url': item['image_url']?.toString(),
      'modifier_groups_json': jsonEncode(
        item['modifier_groups'] as List<Object?>? ?? const <Object?>[],
      ),
      'price': item['price']!.toString(),
      'sort_order': (item['sort_order'] as num?)?.toInt() ?? 0,
      'is_available': _boolInt(item['is_available'], defaultValue: true),
    }, conflictAlgorithm: ConflictAlgorithm.replace);
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

    await txn.insert('dining_tables', <String, Object?>{
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
    }, conflictAlgorithm: ConflictAlgorithm.replace);
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
    final rawTable = order['table'];
    final table = rawTable is Map<Object?, Object?>
        ? Map<String, Object?>.from(rawTable)
        : null;
    // READY/KOT updates use the desktop's lightweight BasicOrderSnapshot.
    // Missing keys are NOT null updates: preserve the table, waiter-facing
    // service identity, original items and earlier receipt details.
    final existingOrders = await txn.query(
      'orders',
      where: 'local_order_id = ?',
      whereArgs: <Object?>[localId],
      limit: 1,
    );
    final existingOrder = existingOrders.isEmpty
        ? const <String, Object?>{}
        : existingOrders.first;
    Object? keep(String key, String column) =>
        order.containsKey(key) ? order[key] : existingOrder[column];
    final hasTable = order.containsKey('table');
    final tableId = hasTable
        ? (table == null ? null : table['id']?.toString())
        : existingOrder['table_id']?.toString();

    await txn.insert('orders', <String, Object?>{
      'local_order_id': localId,
      'client_order_id': clientId ?? existingOrder['client_order_id'],
      'server_id': serverId,
      'display_number': (keep('display_number', 'display_number') as num?)?.toInt() ?? 0,
      'table_id': tableId,
      'branch_id': keep('branch_id', 'branch_id')?.toString(),
      'service_type': keep('service_type', 'service_type')?.toString() ?? 'dine_in',
      'service_reference': keep('service_reference', 'service_reference')?.toString(),
      'status': order['status']?.toString() ?? existingOrder['status']?.toString() ?? 'draft',
      'guest_count': (keep('guest_count', 'guest_count') as num?)?.toInt() ?? 1,
      'notes': keep('notes', 'notes')?.toString(),
      'subtotal': keep('subtotal', 'subtotal')?.toString() ?? '0.00',
      'total': keep('total', 'total')?.toString() ?? '0.00',
      'opened_at': keep('opened_at', 'opened_at')?.toString(),
      'submitted_at': keep('submitted_at', 'submitted_at')?.toString(),
      'served_at': keep('served_at', 'served_at')?.toString(),
      'closed_at': keep('closed_at', 'closed_at')?.toString(),
      'kot_rounds_json': order.containsKey('kot_rounds')
          ? jsonEncode(order['kot_rounds'] ?? const <Object?>[])
          : existingOrder['kot_rounds_json'] ?? '[]',
      'kitchen_tickets_json': order.containsKey('kitchen_tickets')
          ? jsonEncode(order['kitchen_tickets'] ?? const <Object?>[])
          : existingOrder['kitchen_tickets_json'] ?? '[]',
      'updated_at': DateTime.now().toUtc().toIso8601String(),
    }, conflictAlgorithm: ConflictAlgorithm.replace);

    for (final rawItem in order['items'] as List<Object?>? ?? const []) {
      final item = Map<String, Object?>.from(rawItem! as Map<Object?, Object?>);
      final serverLineId = item['id']!.toString();
      final clientLineId = item['client_line_id']?.toString();
      final localLineId = clientLineId == null || clientLineId.isEmpty
          ? 'server:$serverLineId'
          : clientLineId;
      final existingLines = await txn.query(
        'order_items',
        columns: <String>[
          'menu_item_id', 'item_name', 'unit_price', 'line_total',
          'dispatched_quantity', 'course_state', 'notes', 'seat_number',
          'course_number', 'course_name', 'modifiers_snapshot_json',
          'allergy_instructions', 'kitchen_instructions', 'status',
        ],
        where: 'local_line_id = ?',
        whereArgs: <Object?>[localLineId],
        limit: 1,
      );
      final oldLine = existingLines.isEmpty
          ? const <String, Object?>{}
          : existingLines.first;
      final quantity = (item['quantity'] as num?)?.toInt() ?? 1;
      final roundId = item['kot_round_id']?.toString();
      final roundNumber = (item['round_number'] as num?)?.toInt() ?? 0;
      // The desktop doesn't publish dispatched_quantity. A line assigned to
      // a KOT round is already sent; don't reset it to zero on every sync.
      final lineStatus = item['status']?.toString() ??
          oldLine['status']?.toString() ?? 'pending';
      final sentToKitchen = (roundId != null && roundId.isNotEmpty) ||
          roundNumber > 0 ||
          const <String>{
            'submitted', 'submitted_pending_sync',
            'preparing', 'ready', 'served', 'completed',
          }.contains(lineStatus);
      final priorSent = (oldLine['dispatched_quantity'] as num?)?.toInt() ?? 0;
      final dispatched = sentToKitchen
          ? quantity
          : ((item['dispatched_quantity'] as num?)?.toInt() ?? priorSent)
              .clamp(0, quantity);

      // Live desktop READY/served/closed events omit unit_price and menu
      // metadata. Keep existing details or derive the price from line_total.
      final lineTotal = item['line_total']?.toString() ??
          oldLine['line_total']?.toString() ?? '0.00';
      final effectiveQuantity = quantity < 1 ? 1 : quantity;
      final unitPrice = item['unit_price']?.toString() ??
          oldLine['unit_price']?.toString() ??
          _fromMinor(_toMinor(lineTotal) ~/ effectiveQuantity);
      final modifiers = item.containsKey('modifiers_snapshot')
          ? jsonEncode(item['modifiers_snapshot'] ?? const <Object?>[])
          : oldLine['modifiers_snapshot_json']?.toString() ?? '[]';
      await txn.insert('order_items', <String, Object?>{
        'local_line_id': localLineId,
        'client_line_id': clientLineId,
        'server_id': serverLineId,
        'local_order_id': localId,
        'menu_item_id': item['menu_item_id']?.toString() ??
            oldLine['menu_item_id']?.toString(),
        'item_name': item['item_name']?.toString() ??
            oldLine['item_name']?.toString() ?? 'Unknown item',
        'unit_price': unitPrice,
        'quantity': quantity,
        'dispatched_quantity': dispatched,
        'line_total': lineTotal,
        'notes': item['notes']?.toString() ?? oldLine['notes']?.toString(),
        'seat_number': (item['seat_number'] as num?)?.toInt() ??
            (oldLine['seat_number'] as num?)?.toInt(),
        'course_number': (item['course_number'] as num?)?.toInt() ??
            (oldLine['course_number'] as num?)?.toInt(),
        'course_name': item['course_name']?.toString() ??
            oldLine['course_name']?.toString(),
        'course_state': item['course_state']?.toString() ??
            oldLine['course_state']?.toString() ?? 'open',
        'modifiers_snapshot_json': modifiers,
        'allergy_instructions': item['allergy_instructions']?.toString() ??
            oldLine['allergy_instructions']?.toString(),
        'kitchen_instructions': item['kitchen_instructions']?.toString() ??
            oldLine['kitchen_instructions']?.toString(),
        'status': lineStatus,
      }, conflictAlgorithm: ConflictAlgorithm.replace);
    }
  }

  Future<void> _reapplyLocalOccupancy(DatabaseExecutor txn) async {
    final rows = await txn.rawQuery(
      r'''SELECT DISTINCT orders.table_id FROM orders
         WHERE orders.table_id IS NOT NULL
           AND orders.status IN ('draft', 'submitted_pending_sync')
           AND EXISTS (
             SELECT 1 FROM outbox
             WHERE outbox.status = 'pending'
               AND json_extract(outbox.payload_json, '$.client_order_id') = orders.local_order_id
           )''',
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

  Future<void> _recalculateOrderTxn(
    Transaction txn,
    String localOrderId,
  ) async {
    final rows = await txn.query(
      'order_items',
      columns: const <String>['line_total'],
      where: 'local_order_id = ?',
      whereArgs: <Object?>[localOrderId],
    );

    var subtotalMinor = 0;
    for (final row in rows) {
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
  }

  Future<void> _enqueue(
    Transaction txn, {
    required String mutationId,
    required String operation,
    required Map<String, Object?> payload,
  }) async {
    await txn.insert('outbox', <String, Object?>{
      'mutation_id': mutationId,
      'operation': operation,
      'payload_json': jsonEncode(payload),
      'occurred_at': DateTime.now().toUtc().toIso8601String(),
      'status': 'pending',
      'attempts': 0,
    });
  }

  Future<void> _setSettingTxn(
    DatabaseExecutor txn,
    String key,
    String value,
  ) async {
    await txn.insert('settings', <String, Object?>{
      'key': key,
      'value': value,
    }, conflictAlgorithm: ConflictAlgorithm.replace);
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

  (int, List<Map<String, Object?>>) _resolveLocalModifiers(
    Map<String, Object?> menuItem,
    List<Map<String, Object?>> selections,
  ) {
    final rawGroups = menuItem['modifier_groups_json']?.toString();
    final groups = rawGroups == null || rawGroups.isEmpty
        ? const <Object?>[]
        : jsonDecode(rawGroups) as List<Object?>;
    final selectedIds = selections
        .map((selection) => selection['option_id']?.toString())
        .whereType<String>()
        .toSet();

    var deltaMinor = 0;
    final matched = <String>{};
    final snapshot = <Map<String, Object?>>[];

    for (final rawGroup in groups) {
      final group = Map<String, Object?>.from(
        rawGroup! as Map<Object?, Object?>,
      );
      final options = group['options'] as List<Object?>? ?? const <Object?>[];
      final groupSelections = <Map<String, Object?>>[];

      for (final rawOption in options) {
        final option = Map<String, Object?>.from(
          rawOption! as Map<Object?, Object?>,
        );
        final optionId = option['id']!.toString();

        if (!selectedIds.contains(optionId)) {
          continue;
        }

        matched.add(optionId);
        deltaMinor += _toMinor(option['price_delta']?.toString() ?? '0.00');
        groupSelections.add(<String, Object?>{
          'option_id': optionId,
          'option_name': option['name']!.toString(),
          'price_delta': option['price_delta']?.toString() ?? '0.00',
        });
      }

      final min = (group['min_selections'] as num?)?.toInt() ?? 0;
      final max = (group['max_selections'] as num?)?.toInt() ?? 1;

      if (groupSelections.length < min || groupSelections.length > max) {
        throw StateError(
          'Select between ' +
              min.toString() +
              ' and ' +
              max.toString() +
              ' option(s) for ' +
              group['name']!.toString() +
              '.',
        );
      }

      if (groupSelections.isNotEmpty) {
        snapshot.add(<String, Object?>{
          'group_id': group['id']!.toString(),
          'group_name': group['name']!.toString(),
          'options': groupSelections,
        });
      }
    }

    if (matched.length != selectedIds.length) {
      throw StateError(
        'One or more selected modifiers are unavailable for this menu item.',
      );
    }

    return (deltaMinor, snapshot);
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
