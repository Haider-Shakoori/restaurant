using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.Persistence;

public sealed class RestaurantDbContext(DbContextOptions<RestaurantDbContext> options) : DbContext(options)
{
    public DbSet<LocalBranch> Branches => Set<LocalBranch>();
    public DbSet<LocalDiningArea> DiningAreas => Set<LocalDiningArea>();
    public DbSet<LocalDiningTable> DiningTables => Set<LocalDiningTable>();
    public DbSet<LocalMenuCategory> MenuCategories => Set<LocalMenuCategory>();
    public DbSet<LocalMenuItem> MenuItems => Set<LocalMenuItem>();
    public DbSet<LocalModifierGroup> ModifierGroups => Set<LocalModifierGroup>();
    public DbSet<LocalModifierOption> ModifierOptions => Set<LocalModifierOption>();
    public DbSet<LocalMenuItemModifierGroup> MenuItemModifierGroups => Set<LocalMenuItemModifierGroup>();
    public DbSet<LocalStaffUser> StaffUsers => Set<LocalStaffUser>();
    public DbSet<LocalOperationalState> OperationalStates => Set<LocalOperationalState>();
    public DbSet<LocalKitchenStation> KitchenStations => Set<LocalKitchenStation>();
    public DbSet<LocalMenuItemKitchenRoute> MenuItemKitchenRoutes => Set<LocalMenuItemKitchenRoute>();
    public DbSet<LocalPairedTerminal> PairedTerminals => Set<LocalPairedTerminal>();
    public DbSet<LocalTerminalRuntime> TerminalRuntimes => Set<LocalTerminalRuntime>();
    public DbSet<LocalOrder> Orders => Set<LocalOrder>();
    public DbSet<LocalOrderItem> OrderItems => Set<LocalOrderItem>();
    public DbSet<LocalMutation> Mutations => Set<LocalMutation>();
    public DbSet<LocalChange> Changes => Set<LocalChange>();
    public DbSet<LocalRestaurantSetting> RestaurantSettings => Set<LocalRestaurantSetting>();
    public DbSet<LocalKotRound> KotRounds => Set<LocalKotRound>();
    public DbSet<LocalKotCounter> KotCounters => Set<LocalKotCounter>();
    public DbSet<LocalKitchenTicket> KitchenTickets => Set<LocalKitchenTicket>();
    public DbSet<LocalKitchenTicketItem> KitchenTicketItems => Set<LocalKitchenTicketItem>();
    public DbSet<LocalKitchenPrinterBinding> KitchenPrinterBindings => Set<LocalKitchenPrinterBinding>();
    public DbSet<LocalPrintJob> PrintJobs => Set<LocalPrintJob>();
    public DbSet<LocalCashierSession> CashierSessions => Set<LocalCashierSession>();
    public DbSet<LocalBill> Bills => Set<LocalBill>();
    public DbSet<LocalBillLine> BillLines => Set<LocalBillLine>();
    public DbSet<LocalBillSplit> BillSplits => Set<LocalBillSplit>();
    public DbSet<LocalTenantPayment> Payments => Set<LocalTenantPayment>();
    public DbSet<LocalReceiptPrinterSetting> ReceiptPrinterSettings => Set<LocalReceiptPrinterSetting>();
    public DbSet<LocalReceiptPrintJob> ReceiptPrintJobs => Set<LocalReceiptPrintJob>();
    public DbSet<LocalDailyClosing> DailyClosings => Set<LocalDailyClosing>();
    public DbSet<LocalDailyClosingSnapshot> DailyClosingSnapshots => Set<LocalDailyClosingSnapshot>();
    public DbSet<LocalWaiterShift> WaiterShifts => Set<LocalWaiterShift>();
    public DbSet<LocalAuditEvent> AuditEvents => Set<LocalAuditEvent>();
    public DbSet<LocalSupplier> Suppliers => Set<LocalSupplier>();
    public DbSet<LocalInventoryItem> InventoryItems => Set<LocalInventoryItem>();
    public DbSet<LocalInventoryBalance> InventoryBalances => Set<LocalInventoryBalance>();
    public DbSet<LocalInventoryValuation> InventoryValuations => Set<LocalInventoryValuation>();
    public DbSet<LocalStockMovement> StockMovements => Set<LocalStockMovement>();
    public DbSet<LocalRecipe> Recipes => Set<LocalRecipe>();
    public DbSet<LocalRecipeItem> RecipeItems => Set<LocalRecipeItem>();
    public DbSet<LocalInventoryConsumption> InventoryConsumptions => Set<LocalInventoryConsumption>();
    public DbSet<LocalInventoryConsumptionLine> InventoryConsumptionLines => Set<LocalInventoryConsumptionLine>();
    public DbSet<LocalInventoryReservation> InventoryReservations => Set<LocalInventoryReservation>();
    public DbSet<LocalInventoryReservationLine> InventoryReservationLines => Set<LocalInventoryReservationLine>();
    public DbSet<LocalPurchaseOrder> PurchaseOrders => Set<LocalPurchaseOrder>();
    public DbSet<LocalPurchaseOrderLine> PurchaseOrderLines => Set<LocalPurchaseOrderLine>();
    public DbSet<LocalGoodsReceipt> GoodsReceipts => Set<LocalGoodsReceipt>();
    public DbSet<LocalGoodsReceiptLine> GoodsReceiptLines => Set<LocalGoodsReceiptLine>();
    public DbSet<LocalExpense> Expenses => Set<LocalExpense>();
    public DbSet<LocalCloudOutboxMutation> CloudOutbox => Set<LocalCloudOutboxMutation>();
    public DbSet<LocalCloudEntityLink> CloudEntityLinks => Set<LocalCloudEntityLink>();
    public DbSet<LocalCloudSyncState> CloudSyncStates => Set<LocalCloudSyncState>();
    public DbSet<LocalCloudConflict> CloudConflicts => Set<LocalCloudConflict>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocalBranch>(entity =>
        {
            entity.ToTable("branches");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.Code).IsUnique();
        });

        modelBuilder.Entity<LocalDiningArea>(entity =>
        {
            entity.ToTable("dining_areas");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.BranchId, value.Name });
        });

        modelBuilder.Entity<LocalDiningTable>(entity =>
        {
            entity.ToTable("dining_tables");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.Code).IsUnique();
            entity.HasIndex(value => new { value.DiningAreaId, value.Status });
        });

        modelBuilder.Entity<LocalMenuCategory>(entity =>
        {
            entity.ToTable("menu_categories");
            entity.HasKey(value => value.Id);
        });

        modelBuilder.Entity<LocalMenuItem>(entity =>
        {
            entity.ToTable("menu_items");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.Sku).IsUnique();
            entity.Property(value => value.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<LocalModifierGroup>(entity =>
        {
            entity.ToTable("menu_modifier_groups");
            entity.HasKey(value => value.Id);
        });

        modelBuilder.Entity<LocalModifierOption>(entity =>
        {
            entity.ToTable("menu_modifier_options");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.PriceDelta).HasPrecision(18, 2);
        });

        modelBuilder.Entity<LocalMenuItemModifierGroup>(entity =>
        {
            entity.ToTable("menu_item_modifier_group");
            entity.HasKey(value => new { value.MenuItemId, value.ModifierGroupId });
        });

        modelBuilder.Entity<LocalStaffUser>(entity =>
        {
            entity.ToTable("staff_users");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.PublicId).IsUnique();
            entity.HasIndex(value => value.Email).IsUnique();
            entity.HasIndex(value => value.Role);
        });

        modelBuilder.Entity<LocalOperationalState>(entity =>
        {
            entity.ToTable("operational_state");
            entity.HasKey(value => value.Id);
        });
        modelBuilder.Entity<LocalKitchenStation>(entity =>
        {
            entity.ToTable("kitchen_stations");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.BranchId, value.Code }).IsUnique();
            entity.HasIndex(value => new { value.BranchId, value.SortOrder });
        });

        modelBuilder.Entity<LocalMenuItemKitchenRoute>(entity =>
        {
            entity.ToTable("menu_item_kitchen_routes");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.MenuItemId, value.BranchId }).IsUnique();
            entity.HasIndex(value => value.KitchenStationId);
        });

        modelBuilder.Entity<LocalPairedTerminal>(entity =>
        {
            entity.ToTable("paired_terminals");
            entity.HasKey(value => value.DeviceId);
            entity.HasIndex(value => value.UserId);
            entity.HasIndex(value => value.LastSeenAtUtc);
        });

        modelBuilder.Entity<LocalTerminalRuntime>(entity =>
        {
            entity.ToTable("terminal_runtime");
            entity.HasKey(value => value.DeviceId);
            entity.HasIndex(value => value.LastHeartbeatAtUtc);
            entity.HasIndex(value => value.IsEnabled);
        });

        modelBuilder.Entity<LocalOrder>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.ClientOrderId).IsUnique();
            entity.HasIndex(value => new { value.DiningTableId, value.Status });
            entity.HasIndex(value => new { value.WaiterId, value.Status });
            entity.Property(value => value.Subtotal).HasPrecision(18, 2);
            entity.Property(value => value.Total).HasPrecision(18, 2);
        });

        modelBuilder.Entity<LocalOrderItem>(entity =>
        {
            entity.ToTable("order_items");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.OrderId, value.ClientLineId }).IsUnique();
            entity.Property(value => value.UnitPrice).HasPrecision(18, 2);
            entity.Property(value => value.LineTotal).HasPrecision(18, 2);
        });

        modelBuilder.Entity<LocalMutation>(entity =>
        {
            entity.ToTable("local_mutations");
            entity.HasKey(value => new { value.DeviceId, value.MutationId });
            entity.HasIndex(value => value.ProcessedAtUtc);
        });

        modelBuilder.Entity<LocalChange>(entity =>
        {
            entity.ToTable("local_changes");
            entity.HasKey(value => value.Sequence);
            entity.Property(value => value.Sequence).ValueGeneratedOnAdd();
            entity.HasIndex(value => new { value.EntityType, value.EntityId });
            entity.HasIndex(value => value.OwnerUserId);
        });
        modelBuilder.Entity<LocalRestaurantSetting>(entity =>
        {
            entity.ToTable("restaurant_settings");
            entity.HasKey(value => value.Key);
            entity.HasIndex(value => value.UpdatedAtUtc);
        });

        modelBuilder.Entity<LocalKotRound>(entity =>
        {
            entity.ToTable("kot_rounds");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.OrderId, value.RoundNumber }).IsUnique();
            entity.HasIndex(value => new { value.OrderId, value.MutationId }).IsUnique();
            entity.HasIndex(value => new { value.BranchId, value.BusinessDate, value.DisplayNumber }).IsUnique();
            entity.HasIndex(value => value.KotNumber);
            entity.HasIndex(value => value.SentAt);
        });

        modelBuilder.Entity<LocalKotCounter>(entity =>
        {
            entity.ToTable("kot_counters");
            entity.HasKey(value => new { value.BranchId, value.BusinessDate });
        });

        modelBuilder.Entity<LocalKitchenTicket>(entity =>
        {
            entity.ToTable("kitchen_tickets");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.TicketNumber).IsUnique();
            entity.HasIndex(value => new { value.KotRoundId, value.KitchenStationId }).IsUnique();
            entity.HasIndex(value => new { value.OrderId, value.RoundNumber });
            entity.HasIndex(value => new { value.KitchenStationId, value.Status });
        });

        modelBuilder.Entity<LocalKitchenTicketItem>(entity =>
        {
            entity.ToTable("kitchen_ticket_items");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.OrderItemId).IsUnique();
            entity.HasIndex(value => value.KitchenTicketId);
        });

        modelBuilder.Entity<LocalKitchenPrinterBinding>(entity =>
        {
            entity.ToTable("kitchen_printer_bindings");
            entity.HasKey(value => value.KitchenStationId);
        });

        modelBuilder.Entity<LocalPrintJob>(entity =>
        {
            entity.ToTable("print_jobs");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.KitchenTicketId).IsUnique();
            entity.HasIndex(value => new { value.Status, value.CreatedAtUtc });
        });

        modelBuilder.Entity<LocalCashierSession>(entity =>
        {
            entity.ToTable("cashier_sessions");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.CashierUserId, value.Status });
            entity.HasIndex(value => new { value.BranchId, value.Status });
            entity.Property(value => value.OpeningCash).HasPrecision(18, 2);
            entity.Property(value => value.ExpectedCash).HasPrecision(18, 2);
            entity.Property(value => value.DeclaredCash).HasPrecision(18, 2);
            entity.Property(value => value.CashVariance).HasPrecision(18, 2);
        });

        modelBuilder.Entity<LocalBill>(entity =>
        {
            entity.ToTable("bills");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.OrderId).IsUnique();
            entity.HasIndex(value => value.BillNumber).IsUnique();
            entity.HasIndex(value => new { value.BranchId, value.Status });
            entity.Property(value => value.Subtotal).HasPrecision(18, 2);
            entity.Property(value => value.DiscountValue).HasPrecision(18, 4);
            entity.Property(value => value.DiscountAmount).HasPrecision(18, 2);
            entity.Property(value => value.Total).HasPrecision(18, 2);
            entity.Property(value => value.PaidAmount).HasPrecision(18, 2);
            entity.Property(value => value.BalanceDue).HasPrecision(18, 2);
        });

        modelBuilder.Entity<LocalBillLine>(entity =>
        {
            entity.ToTable("bill_lines");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.OrderItemId).IsUnique();
            entity.HasIndex(value => value.BillId);
            entity.Property(value => value.UnitPrice).HasPrecision(18, 2);
            entity.Property(value => value.LineTotal).HasPrecision(18, 2);
        });

        modelBuilder.Entity<LocalBillSplit>(entity =>
        {
            entity.ToTable("bill_splits");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.BillId, value.SplitNumber }).IsUnique();
            entity.Property(value => value.Amount).HasPrecision(18, 2);
            entity.Property(value => value.PaidAmount).HasPrecision(18, 2);
            entity.Property(value => value.BalanceDue).HasPrecision(18, 2);
        });

        modelBuilder.Entity<LocalTenantPayment>(entity =>
        {
            entity.ToTable("tenant_payments");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.ClientPaymentId).IsUnique();
            entity.HasIndex(value => new { value.CashierSessionId, value.Status });
            entity.HasIndex(value => value.BillId);
            entity.Property(value => value.Amount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<LocalReceiptPrinterSetting>(entity =>
        {
            entity.ToTable("receipt_printer_settings");
            entity.HasKey(value => value.Id);
        });

        modelBuilder.Entity<LocalReceiptPrintJob>(entity =>
        {
            entity.ToTable("receipt_print_jobs");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.BillId);
            entity.HasIndex(value => new { value.Status, value.CreatedAtUtc });
        });


        modelBuilder.Entity<LocalDailyClosing>(entity =>
        {
            entity.ToTable("daily_closings");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.BranchId, value.BusinessDate }).IsUnique();
            entity.HasIndex(value => new { value.BranchId, value.Status });
        });

        modelBuilder.Entity<LocalDailyClosingSnapshot>(entity =>
        {
            entity.ToTable("daily_closing_snapshots");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.DailyClosingId, value.Version }).IsUnique();
            entity.Property(value => value.GrossSales).HasPrecision(18, 2);
            entity.Property(value => value.Discounts).HasPrecision(18, 2);
            entity.Property(value => value.NetSales).HasPrecision(18, 2);
            entity.Property(value => value.PaymentsTotal).HasPrecision(18, 2);
            entity.Property(value => value.CashPayments).HasPrecision(18, 2);
            entity.Property(value => value.CardPayments).HasPrecision(18, 2);
            entity.Property(value => value.BankPayments).HasPrecision(18, 2);
            entity.Property(value => value.MobileMoneyPayments).HasPrecision(18, 2);
            entity.Property(value => value.OtherPayments).HasPrecision(18, 2);
            entity.Property(value => value.ExpectedCash).HasPrecision(18, 2);
            entity.Property(value => value.DeclaredCash).HasPrecision(18, 2);
            entity.Property(value => value.CashVariance).HasPrecision(18, 2);
        });

        modelBuilder.Entity<LocalWaiterShift>(entity =>
        {
            entity.ToTable("waiter_shifts");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.UserId, value.Status });
            entity.HasIndex(value => new { value.BranchId, value.StartedAt });
        });

        modelBuilder.Entity<LocalAuditEvent>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(value => value.Sequence);
            entity.Property(value => value.Sequence).ValueGeneratedOnAdd();
            entity.HasIndex(value => value.EventId).IsUnique();
            entity.HasIndex(value => new { value.Category, value.OccurredAtUtc });
            entity.HasIndex(value => new { value.EntityType, value.EntityId });
            entity.HasIndex(value => value.ActorUserId);
        });


        modelBuilder.Entity<LocalSupplier>(entity =>
        {
            entity.ToTable("suppliers");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.Code).IsUnique();
            entity.HasIndex(value => value.IsActive);
        });

        modelBuilder.Entity<LocalInventoryItem>(entity =>
        {
            entity.ToTable("inventory_items");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.Sku).IsUnique();
            entity.HasIndex(value => value.IsActive);
            entity.Property(value => value.PurchaseToBaseFactor).HasPrecision(18, 6);
            entity.Property(value => value.ReorderLevel).HasPrecision(18, 4);
        });

        modelBuilder.Entity<LocalInventoryBalance>(entity =>
        {
            entity.ToTable("inventory_balances");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.BranchId, value.InventoryItemId }).IsUnique();
            entity.Property(value => value.Quantity).HasPrecision(18, 4);
        });

        modelBuilder.Entity<LocalInventoryValuation>(entity =>
        {
            entity.ToTable("inventory_valuations");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.BranchId, value.InventoryItemId }).IsUnique();
            entity.Property(value => value.Quantity).HasPrecision(18, 4);
            entity.Property(value => value.Value).HasPrecision(18, 2);
            entity.Property(value => value.AverageUnitCost).HasPrecision(18, 6);
        });

        modelBuilder.Entity<LocalStockMovement>(entity =>
        {
            entity.ToTable("stock_movements");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.IdempotencyKey).IsUnique();
            entity.HasIndex(value => new { value.BranchId, value.InventoryItemId, value.OccurredAt });
            entity.HasIndex(value => new { value.SourceType, value.SourceId });
            entity.Property(value => value.QuantityDelta).HasPrecision(18, 4);
            entity.Property(value => value.UnitCost).HasPrecision(18, 2);
        });

        modelBuilder.Entity<LocalRecipe>(entity =>
        {
            entity.ToTable("recipes");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.BranchId, value.MenuItemId, value.Version }).IsUnique();
            entity.HasIndex(value => value.IsActive);
        });

        modelBuilder.Entity<LocalRecipeItem>(entity =>
        {
            entity.ToTable("recipe_items");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.RecipeId, value.InventoryItemId }).IsUnique();
            entity.Property(value => value.QuantityBase).HasPrecision(18, 4);
        });

        modelBuilder.Entity<LocalInventoryConsumption>(entity =>
        {
            entity.ToTable("inventory_consumptions");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.ProductionKey).IsUnique();
            entity.HasIndex(value => value.OrderId);
            entity.HasIndex(value => value.KitchenTicketItemId);
            entity.HasIndex(value => new { value.BranchId, value.ConsumedAt });
        });

        modelBuilder.Entity<LocalInventoryConsumptionLine>(entity =>
        {
            entity.ToTable("inventory_consumption_lines");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.InventoryConsumptionId, value.OrderItemId, value.InventoryItemId }).IsUnique();
            entity.Property(value => value.QuantityBase).HasPrecision(18, 4);
        });

        modelBuilder.Entity<LocalInventoryReservation>(entity =>
        {
            entity.ToTable("inventory_reservations");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.KitchenTicketItemId).IsUnique();
            entity.HasIndex(value => new { value.BranchId, value.Status });
            entity.HasIndex(value => value.OrderId);
        });

        modelBuilder.Entity<LocalInventoryReservationLine>(entity =>
        {
            entity.ToTable("inventory_reservation_lines");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.InventoryReservationId, value.InventoryItemId }).IsUnique();
            entity.HasIndex(value => value.InventoryItemId);
            entity.Property(value => value.QuantityBase).HasPrecision(18, 4);
        });

        modelBuilder.Entity<LocalPurchaseOrder>(entity =>
        {
            entity.ToTable("purchase_orders");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.PoNumber).IsUnique();
            entity.HasIndex(value => new { value.BranchId, value.Status, value.OrderedAt });
            entity.Property(value => value.EstimatedTotal).HasPrecision(18, 2);
        });

        modelBuilder.Entity<LocalPurchaseOrderLine>(entity =>
        {
            entity.ToTable("purchase_order_lines");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.PurchaseOrderId);
            entity.Property(value => value.ConversionFactor).HasPrecision(18, 6);
            entity.Property(value => value.OrderedPurchaseQuantity).HasPrecision(18, 4);
            entity.Property(value => value.OrderedBaseQuantity).HasPrecision(18, 4);
            entity.Property(value => value.ReceivedBaseQuantity).HasPrecision(18, 4);
            entity.Property(value => value.UnitCost).HasPrecision(18, 2);
            entity.Property(value => value.LineTotal).HasPrecision(18, 2);
        });

        modelBuilder.Entity<LocalGoodsReceipt>(entity =>
        {
            entity.ToTable("goods_receipts");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.ReceiptNumber).IsUnique();
            entity.HasIndex(value => value.ClientReceiptId).IsUnique();
            entity.HasIndex(value => new { value.PurchaseOrderId, value.ReceivedAt });
        });

        modelBuilder.Entity<LocalGoodsReceiptLine>(entity =>
        {
            entity.ToTable("goods_receipt_lines");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.GoodsReceiptId);
            entity.Property(value => value.ReceivedPurchaseQuantity).HasPrecision(18, 4);
            entity.Property(value => value.ReceivedBaseQuantity).HasPrecision(18, 4);
            entity.Property(value => value.UnitCost).HasPrecision(18, 2);
            entity.Property(value => value.LineTotal).HasPrecision(18, 2);
        });



        modelBuilder.Entity<LocalExpense>(entity =>
        {
            entity.ToTable("expenses");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.BranchId, value.ExpenseDate });
            entity.HasIndex(value => value.Category);
            entity.Property(value => value.Amount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<LocalCloudOutboxMutation>(entity =>
        {
            entity.ToTable("cloud_outbox");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.Status, value.OccurredAtUtc });
            entity.HasIndex(value => new { value.EntityType, value.LocalEntityId });
        });

        modelBuilder.Entity<LocalCloudEntityLink>(entity =>
        {
            entity.ToTable("cloud_entity_links");
            entity.HasKey(value => new { value.EntityType, value.LocalEntityId });
            entity.HasIndex(value => new { value.EntityType, value.CloudEntityId });
        });

        modelBuilder.Entity<LocalCloudSyncState>(entity =>
        {
            entity.ToTable("cloud_sync_state");
            entity.HasKey(value => value.Id);
        });

        modelBuilder.Entity<LocalCloudConflict>(entity =>
        {
            entity.ToTable("cloud_conflicts");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.MutationId).IsUnique();
            entity.HasIndex(value => new { value.Status, value.CreatedAtUtc });
        });



    }
}
