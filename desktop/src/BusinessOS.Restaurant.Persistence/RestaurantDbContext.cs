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

    }
}
