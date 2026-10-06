using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.Persistence;

public sealed class LocalDatabaseFactory
{
    private readonly string _databasePath;

    public LocalDatabaseFactory(string? rootDirectory = null)
    {
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BusinessOS",
            "Restaurant");

        _databasePath = Path.Combine(root, "data", "restaurant.db");
    }

    public string DatabasePath => _databasePath;

    public SqliteConnection CreateConnection() => new(new SqliteConnectionStringBuilder
    {
        DataSource = _databasePath,
        ForeignKeys = true,
        Pooling = false,
    }.ToString());

    public RestaurantDbContext Create()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();

        var options = new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new RestaurantDbContext(options);
    }

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = Create();
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureOrderingSchemaAsync(db, cancellationToken);
    }

    private static async Task EnsureOrderingSchemaAsync(
        RestaurantDbContext db,
        CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS paired_terminals (
                DeviceId TEXT NOT NULL PRIMARY KEY,
                TenantId TEXT NOT NULL,
                DeviceSecretHash TEXT NOT NULL,
                AccessTokenHash TEXT NOT NULL,
                UserId INTEGER NOT NULL,
                UserPublicId TEXT NOT NULL,
                UserName TEXT NOT NULL,
                UserRole TEXT NOT NULL,
                ValidatedAtUtc TEXT NOT NULL,
                LastSeenAtUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_paired_terminals_UserId ON paired_terminals (UserId);
            CREATE INDEX IF NOT EXISTS IX_paired_terminals_LastSeenAtUtc ON paired_terminals (LastSeenAtUtc);

            CREATE TABLE IF NOT EXISTS terminal_runtime (
                DeviceId TEXT NOT NULL PRIMARY KEY,
                DisplayName TEXT NULL,
                ClientType TEXT NULL,
                AppVersion TEXT NULL,
                LastIpAddress TEXT NULL,
                LastUserAgent TEXT NULL,
                IsEnabled INTEGER NOT NULL,
                FirstSeenAtUtc TEXT NOT NULL,
                LastHeartbeatAtUtc TEXT NOT NULL,
                DisabledAtUtc TEXT NULL,
                DisabledByUserId INTEGER NULL
            );
            CREATE INDEX IF NOT EXISTS IX_terminal_runtime_LastHeartbeatAtUtc ON terminal_runtime (LastHeartbeatAtUtc);
            CREATE INDEX IF NOT EXISTS IX_terminal_runtime_IsEnabled ON terminal_runtime (IsEnabled);

            CREATE TABLE IF NOT EXISTS orders (
                Id TEXT NOT NULL PRIMARY KEY,
                ClientOrderId TEXT NOT NULL,
                DiningTableId TEXT NOT NULL,
                WaiterId INTEGER NOT NULL,
                WaiterPublicId TEXT NOT NULL,
                WaiterName TEXT NOT NULL,
                Status TEXT NOT NULL,
                GuestCount INTEGER NOT NULL,
                Notes TEXT NULL,
                Subtotal TEXT NOT NULL,
                Total TEXT NOT NULL,
                OpenedAt TEXT NULL,
                SubmittedAt TEXT NULL,
                ServedAt TEXT NULL,
                ClosedAt TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_orders_ClientOrderId ON orders (ClientOrderId);
            CREATE INDEX IF NOT EXISTS IX_orders_DiningTableId_Status ON orders (DiningTableId, Status);
            CREATE INDEX IF NOT EXISTS IX_orders_WaiterId_Status ON orders (WaiterId, Status);

            CREATE TABLE IF NOT EXISTS order_items (
                Id TEXT NOT NULL PRIMARY KEY,
                OrderId TEXT NOT NULL,
                MenuItemId TEXT NOT NULL,
                ClientLineId TEXT NOT NULL,
                ItemName TEXT NOT NULL,
                UnitPrice TEXT NOT NULL,
                Quantity INTEGER NOT NULL,
                LineTotal TEXT NOT NULL,
                Notes TEXT NULL,
                Status TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_order_items_OrderId_ClientLineId ON order_items (OrderId, ClientLineId);

            CREATE TABLE IF NOT EXISTS local_mutations (
                DeviceId TEXT NOT NULL,
                MutationId TEXT NOT NULL,
                UserId INTEGER NOT NULL,
                Operation TEXT NOT NULL,
                RequestHash TEXT NOT NULL,
                Status TEXT NOT NULL,
                ErrorCode TEXT NULL,
                ErrorMessage TEXT NULL,
                ResponseJson TEXT NOT NULL,
                ClientOccurredAt TEXT NULL,
                ProcessedAtUtc TEXT NOT NULL,
                PRIMARY KEY (DeviceId, MutationId)
            );
            CREATE INDEX IF NOT EXISTS IX_local_mutations_ProcessedAtUtc ON local_mutations (ProcessedAtUtc);

            CREATE TABLE IF NOT EXISTS local_changes (
                Sequence INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EntityType TEXT NOT NULL,
                EntityId TEXT NOT NULL,
                Operation TEXT NOT NULL,
                OwnerUserId INTEGER NULL,
                DataJson TEXT NULL,
                OccurredAtUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_local_changes_EntityType_EntityId ON local_changes (EntityType, EntityId);
            CREATE INDEX IF NOT EXISTS IX_local_changes_OwnerUserId ON local_changes (OwnerUserId);

            CREATE TABLE IF NOT EXISTS kitchen_stations (
                Id TEXT NOT NULL PRIMARY KEY,
                BranchId TEXT NOT NULL,
                Code TEXT NOT NULL,
                Name TEXT NOT NULL,
                SortOrder INTEGER NOT NULL,
                IsActive INTEGER NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_kitchen_stations_BranchId_Code ON kitchen_stations (BranchId, Code);
            CREATE INDEX IF NOT EXISTS IX_kitchen_stations_BranchId_SortOrder ON kitchen_stations (BranchId, SortOrder);

            CREATE TABLE IF NOT EXISTS menu_item_kitchen_routes (
                Id TEXT NOT NULL PRIMARY KEY,
                MenuItemId TEXT NOT NULL,
                BranchId TEXT NOT NULL,
                KitchenStationId TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_menu_item_kitchen_routes_MenuItemId_BranchId ON menu_item_kitchen_routes (MenuItemId, BranchId);
            CREATE INDEX IF NOT EXISTS IX_menu_item_kitchen_routes_KitchenStationId ON menu_item_kitchen_routes (KitchenStationId);

            CREATE TABLE IF NOT EXISTS kitchen_tickets (
                Id TEXT NOT NULL PRIMARY KEY,
                OrderId TEXT NOT NULL,
                KitchenStationId TEXT NOT NULL,
                SubmittedByUserId INTEGER NOT NULL,
                TicketNumber TEXT NOT NULL,
                Status TEXT NOT NULL,
                QueuedAt TEXT NOT NULL,
                StartedAt TEXT NULL,
                ReadyAt TEXT NULL,
                CompletedAt TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_kitchen_tickets_TicketNumber ON kitchen_tickets (TicketNumber);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_kitchen_tickets_OrderId_KitchenStationId ON kitchen_tickets (OrderId, KitchenStationId);
            CREATE INDEX IF NOT EXISTS IX_kitchen_tickets_KitchenStationId_Status ON kitchen_tickets (KitchenStationId, Status);

            CREATE TABLE IF NOT EXISTS kitchen_ticket_items (
                Id TEXT NOT NULL PRIMARY KEY,
                KitchenTicketId TEXT NOT NULL,
                OrderItemId TEXT NOT NULL,
                ItemName TEXT NOT NULL,
                Quantity INTEGER NOT NULL,
                Notes TEXT NULL,
                Status TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_kitchen_ticket_items_OrderItemId ON kitchen_ticket_items (OrderItemId);
            CREATE INDEX IF NOT EXISTS IX_kitchen_ticket_items_KitchenTicketId ON kitchen_ticket_items (KitchenTicketId);

            CREATE TABLE IF NOT EXISTS kitchen_printer_bindings (
                KitchenStationId TEXT NOT NULL PRIMARY KEY,
                PrinterName TEXT NOT NULL,
                Copies INTEGER NOT NULL,
                IsEnabled INTEGER NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS print_jobs (
                Id TEXT NOT NULL PRIMARY KEY,
                KitchenTicketId TEXT NOT NULL,
                PrinterName TEXT NOT NULL,
                DocumentName TEXT NOT NULL,
                PayloadText TEXT NOT NULL,
                Copies INTEGER NOT NULL,
                Status TEXT NOT NULL,
                Attempts INTEGER NOT NULL,
                LastError TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                PrintedAtUtc TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_print_jobs_KitchenTicketId ON print_jobs (KitchenTicketId);
            CREATE INDEX IF NOT EXISTS IX_print_jobs_Status_CreatedAtUtc ON print_jobs (Status, CreatedAtUtc);

            CREATE TABLE IF NOT EXISTS cashier_sessions (
                Id TEXT NOT NULL PRIMARY KEY,
                BranchId TEXT NOT NULL,
                CashierUserId INTEGER NOT NULL,
                CashierName TEXT NOT NULL,
                Status TEXT NOT NULL,
                OpeningCash TEXT NOT NULL,
                ExpectedCash TEXT NULL,
                DeclaredCash TEXT NULL,
                CashVariance TEXT NULL,
                OpenedAt TEXT NOT NULL,
                ClosedAt TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_cashier_sessions_CashierUserId_Status ON cashier_sessions (CashierUserId, Status);
            CREATE INDEX IF NOT EXISTS IX_cashier_sessions_BranchId_Status ON cashier_sessions (BranchId, Status);

            CREATE TABLE IF NOT EXISTS bills (
                Id TEXT NOT NULL PRIMARY KEY,
                OrderId TEXT NOT NULL,
                BranchId TEXT NOT NULL,
                CreatedByUserId INTEGER NOT NULL,
                BillNumber TEXT NOT NULL,
                Status TEXT NOT NULL,
                Subtotal TEXT NOT NULL,
                DiscountType TEXT NULL,
                DiscountValue TEXT NULL,
                DiscountAmount TEXT NOT NULL,
                DiscountReason TEXT NULL,
                Total TEXT NOT NULL,
                PaidAmount TEXT NOT NULL,
                BalanceDue TEXT NOT NULL,
                IssuedAt TEXT NOT NULL,
                PaidAt TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_bills_OrderId ON bills (OrderId);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_bills_BillNumber ON bills (BillNumber);
            CREATE INDEX IF NOT EXISTS IX_bills_BranchId_Status ON bills (BranchId, Status);

            CREATE TABLE IF NOT EXISTS bill_lines (
                Id TEXT NOT NULL PRIMARY KEY,
                BillId TEXT NOT NULL,
                OrderItemId TEXT NOT NULL,
                ItemName TEXT NOT NULL,
                Quantity INTEGER NOT NULL,
                UnitPrice TEXT NOT NULL,
                LineTotal TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_bill_lines_OrderItemId ON bill_lines (OrderItemId);
            CREATE INDEX IF NOT EXISTS IX_bill_lines_BillId ON bill_lines (BillId);

            CREATE TABLE IF NOT EXISTS bill_splits (
                Id TEXT NOT NULL PRIMARY KEY,
                BillId TEXT NOT NULL,
                SplitNumber INTEGER NOT NULL,
                Label TEXT NOT NULL,
                Amount TEXT NOT NULL,
                PaidAmount TEXT NOT NULL,
                BalanceDue TEXT NOT NULL,
                Status TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_bill_splits_BillId_SplitNumber ON bill_splits (BillId, SplitNumber);

            CREATE TABLE IF NOT EXISTS tenant_payments (
                Id TEXT NOT NULL PRIMARY KEY,
                BillId TEXT NOT NULL,
                BillSplitId TEXT NULL,
                CashierSessionId TEXT NOT NULL,
                ReceivedByUserId INTEGER NOT NULL,
                ClientPaymentId TEXT NULL,
                Method TEXT NOT NULL,
                Amount TEXT NOT NULL,
                Reference TEXT NULL,
                Status TEXT NOT NULL,
                ReceivedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_tenant_payments_ClientPaymentId ON tenant_payments (ClientPaymentId);
            CREATE INDEX IF NOT EXISTS IX_tenant_payments_CashierSessionId_Status ON tenant_payments (CashierSessionId, Status);
            CREATE INDEX IF NOT EXISTS IX_tenant_payments_BillId ON tenant_payments (BillId);

            CREATE TABLE IF NOT EXISTS receipt_printer_settings (
                Id INTEGER NOT NULL PRIMARY KEY,
                PrinterName TEXT NOT NULL,
                Copies INTEGER NOT NULL,
                IsEnabled INTEGER NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS receipt_print_jobs (
                Id TEXT NOT NULL PRIMARY KEY,
                BillId TEXT NOT NULL,
                PrinterName TEXT NOT NULL,
                DocumentName TEXT NOT NULL,
                PayloadText TEXT NOT NULL,
                Copies INTEGER NOT NULL,
                Status TEXT NOT NULL,
                Attempts INTEGER NOT NULL,
                LastError TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                PrintedAtUtc TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_receipt_print_jobs_BillId ON receipt_print_jobs (BillId);
            CREATE INDEX IF NOT EXISTS IX_receipt_print_jobs_Status_CreatedAtUtc ON receipt_print_jobs (Status, CreatedAtUtc);

            CREATE TABLE IF NOT EXISTS daily_closings (
                Id TEXT NOT NULL PRIMARY KEY,
                BranchId TEXT NOT NULL,
                BusinessDate TEXT NOT NULL,
                Status TEXT NOT NULL,
                CreatedByUserId INTEGER NOT NULL,
                FinalizedAt TEXT NULL,
                ReopenedAt TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_daily_closings_BranchId_BusinessDate ON daily_closings (BranchId, BusinessDate);
            CREATE INDEX IF NOT EXISTS IX_daily_closings_BranchId_Status ON daily_closings (BranchId, Status);

            CREATE TABLE IF NOT EXISTS daily_closing_snapshots (
                Id TEXT NOT NULL PRIMARY KEY,
                DailyClosingId TEXT NOT NULL,
                Version INTEGER NOT NULL,
                FinalizedByUserId INTEGER NOT NULL,
                BillCount INTEGER NOT NULL,
                PaymentCount INTEGER NOT NULL,
                CashierSessionCount INTEGER NOT NULL,
                GrossSales TEXT NOT NULL,
                Discounts TEXT NOT NULL,
                NetSales TEXT NOT NULL,
                PaymentsTotal TEXT NOT NULL,
                CashPayments TEXT NOT NULL,
                CardPayments TEXT NOT NULL,
                BankPayments TEXT NOT NULL,
                MobileMoneyPayments TEXT NOT NULL,
                OtherPayments TEXT NOT NULL,
                ExpectedCash TEXT NOT NULL,
                DeclaredCash TEXT NOT NULL,
                CashVariance TEXT NOT NULL,
                FinalizedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_daily_closing_snapshots_DailyClosingId_Version ON daily_closing_snapshots (DailyClosingId, Version);

            CREATE TABLE IF NOT EXISTS waiter_shifts (
                Id TEXT NOT NULL PRIMARY KEY,
                BranchId TEXT NOT NULL,
                UserId INTEGER NOT NULL,
                UserPublicId TEXT NOT NULL,
                UserName TEXT NOT NULL,
                Role TEXT NOT NULL,
                Status TEXT NOT NULL,
                StartedAt TEXT NOT NULL,
                EndedAt TEXT NULL,
                BreakMinutes INTEGER NOT NULL,
                ClosingNote TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_waiter_shifts_UserId_Status ON waiter_shifts (UserId, Status);
            CREATE INDEX IF NOT EXISTS IX_waiter_shifts_BranchId_StartedAt ON waiter_shifts (BranchId, StartedAt);

            CREATE TABLE IF NOT EXISTS audit_events (
                Sequence INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EventId TEXT NOT NULL,
                Category TEXT NOT NULL,
                EventType TEXT NOT NULL,
                ActorUserId INTEGER NOT NULL,
                ActorName TEXT NOT NULL,
                ActorRole TEXT NOT NULL,
                BranchId TEXT NULL,
                EntityType TEXT NULL,
                EntityId TEXT NULL,
                PayloadJson TEXT NULL,
                OccurredAtUtc TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_audit_events_EventId ON audit_events (EventId);
            CREATE INDEX IF NOT EXISTS IX_audit_events_Category_OccurredAtUtc ON audit_events (Category, OccurredAtUtc);
            CREATE INDEX IF NOT EXISTS IX_audit_events_EntityType_EntityId ON audit_events (EntityType, EntityId);
            CREATE INDEX IF NOT EXISTS IX_audit_events_ActorUserId ON audit_events (ActorUserId);

            CREATE TABLE IF NOT EXISTS suppliers (
                Id TEXT NOT NULL PRIMARY KEY,
                Code TEXT NOT NULL,
                Name TEXT NOT NULL,
                Phone TEXT NULL,
                Email TEXT NULL,
                Address TEXT NULL,
                IsActive INTEGER NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_suppliers_Code ON suppliers (Code);
            CREATE INDEX IF NOT EXISTS IX_suppliers_IsActive ON suppliers (IsActive);

            CREATE TABLE IF NOT EXISTS inventory_items (
                Id TEXT NOT NULL PRIMARY KEY,
                Sku TEXT NOT NULL,
                Name TEXT NOT NULL,
                BaseUnit TEXT NOT NULL,
                PurchaseUnit TEXT NULL,
                PurchaseToBaseFactor TEXT NOT NULL,
                ReorderLevel TEXT NOT NULL,
                IsActive INTEGER NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_inventory_items_Sku ON inventory_items (Sku);
            CREATE INDEX IF NOT EXISTS IX_inventory_items_IsActive ON inventory_items (IsActive);

            CREATE TABLE IF NOT EXISTS inventory_balances (
                Id TEXT NOT NULL PRIMARY KEY,
                BranchId TEXT NOT NULL,
                InventoryItemId TEXT NOT NULL,
                Quantity TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_inventory_balances_BranchId_InventoryItemId ON inventory_balances (BranchId, InventoryItemId);

            CREATE TABLE IF NOT EXISTS inventory_valuations (
                Id TEXT NOT NULL PRIMARY KEY,
                BranchId TEXT NOT NULL,
                InventoryItemId TEXT NOT NULL,
                Quantity TEXT NOT NULL,
                Value TEXT NOT NULL,
                AverageUnitCost TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_inventory_valuations_BranchId_InventoryItemId ON inventory_valuations (BranchId, InventoryItemId);

            CREATE TABLE IF NOT EXISTS stock_movements (
                Id TEXT NOT NULL PRIMARY KEY,
                BranchId TEXT NOT NULL,
                InventoryItemId TEXT NOT NULL,
                ActorUserId INTEGER NOT NULL,
                MovementType TEXT NOT NULL,
                QuantityDelta TEXT NOT NULL,
                UnitCost TEXT NULL,
                SourceType TEXT NOT NULL,
                SourceId TEXT NOT NULL,
                SourceLineId TEXT NULL,
                IdempotencyKey TEXT NOT NULL,
                Notes TEXT NULL,
                OccurredAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_stock_movements_IdempotencyKey ON stock_movements (IdempotencyKey);
            CREATE INDEX IF NOT EXISTS IX_stock_movements_BranchId_InventoryItemId_OccurredAt ON stock_movements (BranchId, InventoryItemId, OccurredAt);
            CREATE INDEX IF NOT EXISTS IX_stock_movements_SourceType_SourceId ON stock_movements (SourceType, SourceId);

            CREATE TABLE IF NOT EXISTS recipes (
                Id TEXT NOT NULL PRIMARY KEY,
                BranchId TEXT NOT NULL,
                MenuItemId TEXT NOT NULL,
                Name TEXT NOT NULL,
                Version INTEGER NOT NULL,
                IsActive INTEGER NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_recipes_BranchId_MenuItemId_Version ON recipes (BranchId, MenuItemId, Version);
            CREATE INDEX IF NOT EXISTS IX_recipes_IsActive ON recipes (IsActive);

            CREATE TABLE IF NOT EXISTS recipe_items (
                Id TEXT NOT NULL PRIMARY KEY,
                RecipeId TEXT NOT NULL,
                InventoryItemId TEXT NOT NULL,
                QuantityBase TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_recipe_items_RecipeId_InventoryItemId ON recipe_items (RecipeId, InventoryItemId);

            CREATE TABLE IF NOT EXISTS inventory_consumptions (
                Id TEXT NOT NULL PRIMARY KEY,
                OrderId TEXT NOT NULL,
                BranchId TEXT NOT NULL,
                ConsumedByUserId INTEGER NOT NULL,
                ConsumedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_inventory_consumptions_OrderId ON inventory_consumptions (OrderId);
            CREATE INDEX IF NOT EXISTS IX_inventory_consumptions_BranchId_ConsumedAt ON inventory_consumptions (BranchId, ConsumedAt);

            CREATE TABLE IF NOT EXISTS inventory_consumption_lines (
                Id TEXT NOT NULL PRIMARY KEY,
                InventoryConsumptionId TEXT NOT NULL,
                OrderItemId TEXT NOT NULL,
                RecipeId TEXT NOT NULL,
                InventoryItemId TEXT NOT NULL,
                StockMovementId TEXT NOT NULL,
                QuantityBase TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_inventory_consumption_lines_Consumption_Order_Item
                ON inventory_consumption_lines (InventoryConsumptionId, OrderItemId, InventoryItemId);

            CREATE TABLE IF NOT EXISTS purchase_orders (
                Id TEXT NOT NULL PRIMARY KEY,
                BranchId TEXT NOT NULL,
                SupplierId TEXT NOT NULL,
                OrderedByUserId INTEGER NOT NULL,
                PoNumber TEXT NOT NULL,
                Status TEXT NOT NULL,
                EstimatedTotal TEXT NOT NULL,
                Notes TEXT NULL,
                OrderedAt TEXT NOT NULL,
                CompletedAt TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_purchase_orders_PoNumber ON purchase_orders (PoNumber);
            CREATE INDEX IF NOT EXISTS IX_purchase_orders_BranchId_Status_OrderedAt ON purchase_orders (BranchId, Status, OrderedAt);

            CREATE TABLE IF NOT EXISTS purchase_order_lines (
                Id TEXT NOT NULL PRIMARY KEY,
                PurchaseOrderId TEXT NOT NULL,
                InventoryItemId TEXT NOT NULL,
                ItemName TEXT NOT NULL,
                PurchaseUnit TEXT NOT NULL,
                ConversionFactor TEXT NOT NULL,
                OrderedPurchaseQuantity TEXT NOT NULL,
                OrderedBaseQuantity TEXT NOT NULL,
                ReceivedBaseQuantity TEXT NOT NULL,
                UnitCost TEXT NOT NULL,
                LineTotal TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_purchase_order_lines_PurchaseOrderId ON purchase_order_lines (PurchaseOrderId);

            CREATE TABLE IF NOT EXISTS goods_receipts (
                Id TEXT NOT NULL PRIMARY KEY,
                PurchaseOrderId TEXT NOT NULL,
                BranchId TEXT NOT NULL,
                SupplierId TEXT NOT NULL,
                ReceivedByUserId INTEGER NOT NULL,
                ReceiptNumber TEXT NOT NULL,
                ClientReceiptId TEXT NULL,
                Status TEXT NOT NULL,
                ReceivedAt TEXT NOT NULL,
                Notes TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_goods_receipts_ReceiptNumber ON goods_receipts (ReceiptNumber);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_goods_receipts_ClientReceiptId ON goods_receipts (ClientReceiptId);
            CREATE INDEX IF NOT EXISTS IX_goods_receipts_PurchaseOrderId_ReceivedAt ON goods_receipts (PurchaseOrderId, ReceivedAt);

            CREATE TABLE IF NOT EXISTS goods_receipt_lines (
                Id TEXT NOT NULL PRIMARY KEY,
                GoodsReceiptId TEXT NOT NULL,
                PurchaseOrderLineId TEXT NOT NULL,
                InventoryItemId TEXT NOT NULL,
                ReceivedPurchaseQuantity TEXT NOT NULL,
                ReceivedBaseQuantity TEXT NOT NULL,
                UnitCost TEXT NOT NULL,
                LineTotal TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_goods_receipt_lines_GoodsReceiptId ON goods_receipt_lines (GoodsReceiptId);

            CREATE TABLE IF NOT EXISTS expenses (
                Id TEXT NOT NULL PRIMARY KEY,
                BranchId TEXT NOT NULL,
                RecordedByUserId INTEGER NOT NULL,
                Category TEXT NOT NULL,
                Description TEXT NOT NULL,
                Amount TEXT NOT NULL,
                Currency TEXT NOT NULL,
                PaymentMethod TEXT NOT NULL,
                Reference TEXT NULL,
                ExpenseDate TEXT NOT NULL,
                RecordedAtUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_expenses_BranchId_ExpenseDate ON expenses (BranchId, ExpenseDate);
            CREATE INDEX IF NOT EXISTS IX_expenses_Category ON expenses (Category);

            CREATE TABLE IF NOT EXISTS cloud_outbox (
                Id TEXT NOT NULL PRIMARY KEY,
                Operation TEXT NOT NULL,
                EntityType TEXT NOT NULL,
                LocalEntityId TEXT NOT NULL,
                ActorUserId INTEGER NOT NULL,
                ActorPublicId TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                Status TEXT NOT NULL,
                Attempts INTEGER NOT NULL,
                CloudEntityId TEXT NULL,
                ErrorCode TEXT NULL,
                ErrorMessage TEXT NULL,
                OccurredAtUtc TEXT NOT NULL,
                LastAttemptAtUtc TEXT NULL,
                SyncedAtUtc TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_cloud_outbox_Status_OccurredAtUtc ON cloud_outbox (Status, OccurredAtUtc);
            CREATE INDEX IF NOT EXISTS IX_cloud_outbox_EntityType_LocalEntityId ON cloud_outbox (EntityType, LocalEntityId);

            CREATE TABLE IF NOT EXISTS cloud_entity_links (
                EntityType TEXT NOT NULL,
                LocalEntityId TEXT NOT NULL,
                CloudEntityId TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                PRIMARY KEY (EntityType, LocalEntityId)
            );
            CREATE INDEX IF NOT EXISTS IX_cloud_entity_links_EntityType_CloudEntityId ON cloud_entity_links (EntityType, CloudEntityId);

            CREATE TABLE IF NOT EXISTS cloud_sync_state (
                Id INTEGER NOT NULL PRIMARY KEY,
                PullCursor INTEGER NOT NULL,
                LastPushAtUtc TEXT NULL,
                LastPullAtUtc TEXT NULL,
                LastSuccessAtUtc TEXT NULL,
                LastError TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS cloud_conflicts (
                Id TEXT NOT NULL PRIMARY KEY,
                MutationId TEXT NOT NULL,
                Operation TEXT NOT NULL,
                EntityType TEXT NOT NULL,
                LocalEntityId TEXT NOT NULL,
                Code TEXT NOT NULL,
                Message TEXT NOT NULL,
                LocalPayloadJson TEXT NULL,
                CloudPayloadJson TEXT NULL,
                Status TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                ResolvedAtUtc TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_cloud_conflicts_MutationId ON cloud_conflicts (MutationId);
            CREATE INDEX IF NOT EXISTS IX_cloud_conflicts_Status_CreatedAtUtc ON cloud_conflicts (Status, CreatedAtUtc);
            """;

        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }
}
