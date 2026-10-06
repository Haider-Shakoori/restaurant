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
    public DbSet<LocalOrder> Orders => Set<LocalOrder>();
    public DbSet<LocalOrderItem> OrderItems => Set<LocalOrderItem>();
    public DbSet<LocalMutation> Mutations => Set<LocalMutation>();
    public DbSet<LocalChange> Changes => Set<LocalChange>();
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
        modelBuilder.Entity<LocalKitchenTicket>(entity =>
        {
            entity.ToTable("kitchen_tickets");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.TicketNumber).IsUnique();
            entity.HasIndex(value => new { value.OrderId, value.KitchenStationId }).IsUnique();
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



    }
}
