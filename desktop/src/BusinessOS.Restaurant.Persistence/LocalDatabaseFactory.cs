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
            """;

        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }
}
