using System.Text;
using System.Text.Json;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.LocalServer;

public sealed class LocalKitchenService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalDatabaseFactory _databaseFactory;

    public LocalKitchenService(LocalDatabaseFactory databaseFactory)
    {
        _databaseFactory = databaseFactory;
    }

    public async Task DispatchAsync(
        RestaurantDbContext db,
        LocalOrder order,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        var existing = await db.KitchenTickets
            .Where(value => value.OrderId == order.Id)
            .AnyAsync(cancellationToken);

        if (existing)
        {
            return;
        }

        var table = await db.DiningTables
            .SingleAsync(value => value.Id == order.DiningTableId, cancellationToken);
        var area = await db.DiningAreas
            .SingleAsync(value => value.Id == table.DiningAreaId, cancellationToken);
        var branchId = area.BranchId;

        var items = await db.OrderItems
            .Where(value => value.OrderId == order.Id)
            .ToArrayAsync(cancellationToken);

        var menuItemIds = items
            .Select(value => value.MenuItemId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var routes = await db.MenuItemKitchenRoutes
            .Where(value => value.BranchId == branchId && menuItemIds.Contains(value.MenuItemId))
            .AsNoTracking()
            .ToDictionaryAsync(value => value.MenuItemId, StringComparer.Ordinal, cancellationToken);

        LocalKitchenStation? general = null;
        var grouped = new Dictionary<string, List<LocalOrderItem>>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            string stationId;

            if (routes.TryGetValue(item.MenuItemId, out var route))
            {
                stationId = route.KitchenStationId;
            }
            else
            {
                general ??= await EnsureGeneralStationAsync(db, branchId, cancellationToken);
                stationId = general.Id;
            }

            if (!grouped.TryGetValue(stationId, out var stationItems))
            {
                stationItems = [];
                grouped[stationId] = stationItems;
            }

            stationItems.Add(item);
        }

        foreach (var pair in grouped)
        {
            var station = await db.KitchenStations
                .SingleAsync(value => value.Id == pair.Key, cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var ticket = new LocalKitchenTicket
            {
                Id = Guid.CreateVersion7().ToString("N"),
                OrderId = order.Id,
                KitchenStationId = station.Id,
                SubmittedByUserId = actor.UserId,
                TicketNumber = $"KOT-{Guid.CreateVersion7():N}".ToUpperInvariant(),
                Status = "queued",
                QueuedAt = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };

            db.KitchenTickets.Add(ticket);

            foreach (var orderItem in pair.Value)
            {
                db.KitchenTicketItems.Add(new LocalKitchenTicketItem
                {
                    Id = Guid.CreateVersion7().ToString("N"),
                    KitchenTicketId = ticket.Id,
                    OrderItemId = orderItem.Id,
                    ItemName = orderItem.ItemName,
                    Quantity = orderItem.Quantity,
                    Notes = orderItem.Notes,
                    Status = "queued",
                });

                orderItem.Status = "queued";
                orderItem.UpdatedAtUtc = now;
            }

            await db.SaveChangesAsync(cancellationToken);

            var ticketSnapshot = await TicketSnapshotAsync(db, ticket, cancellationToken);
            AddChange(db, "kitchen_ticket", ticket.Id, null, ticketSnapshot);

            var printer = await db.KitchenPrinterBindings
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.KitchenStationId == station.Id && value.IsEnabled,
                    cancellationToken);

            if (printer is not null)
            {
                db.PrintJobs.Add(new LocalPrintJob
                {
                    Id = Guid.CreateVersion7().ToString("N"),
                    KitchenTicketId = ticket.Id,
                    PrinterName = printer.PrinterName,
                    DocumentName = ticket.TicketNumber,
                    PayloadText = await BuildKotTextAsync(db, ticket, station, order, cancellationToken),
                    Copies = Math.Clamp(printer.Copies, 1, 5),
                    Status = "pending",
                    Attempts = 0,
                    CreatedAtUtc = now,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<object[]> ActiveTicketsAsync(
        string? stationId,
        CancellationToken cancellationToken)
    {
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var query = db.KitchenTickets
            .Where(value => value.Status == "queued" ||
                            value.Status == "preparing" ||
                            value.Status == "ready")
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(stationId))
        {
            query = query.Where(value => value.KitchenStationId == stationId);
        }

        var tickets = (await query.ToArrayAsync(cancellationToken))
            .OrderBy(value => value.QueuedAt)
            .ToArray();

        var snapshots = new List<object>(tickets.Length);

        foreach (var ticket in tickets)
        {
            snapshots.Add(await TicketSnapshotAsync(db, ticket, cancellationToken));
        }

        return snapshots.ToArray();
    }

    public async Task<object> StartAsync(
        string ticketId,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureKitchenRole(actor);

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var ticket = await db.KitchenTickets
            .SingleOrDefaultAsync(value => value.Id == ticketId, cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "Kitchen ticket does not exist.");

        if (ticket.Status != "queued" && ticket.Status != "preparing")
        {
            throw new LocalSyncConflictException(
                "order_state_conflict",
                $"Kitchen ticket cannot move from {ticket.Status} to preparing.");
        }

        if (ticket.Status == "queued")
        {
            ticket.Status = "preparing";
            ticket.StartedAt = DateTimeOffset.UtcNow;
            ticket.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await db.KitchenTicketItems
                .Where(value => value.KitchenTicketId == ticket.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(value => value.Status, "preparing"),
                    cancellationToken);

            await SynchronizeOrderStatusAsync(db, ticket.OrderId, cancellationToken);
        }

        var snapshot = await TicketSnapshotAsync(db, ticket, cancellationToken);
        AddChange(db, "kitchen_ticket", ticket.Id, null, snapshot);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }

    public async Task<object> ReadyAsync(
        string ticketId,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureKitchenRole(actor);

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var ticket = await db.KitchenTickets
            .SingleOrDefaultAsync(value => value.Id == ticketId, cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "Kitchen ticket does not exist.");

        if (ticket.Status != "queued" &&
            ticket.Status != "preparing" &&
            ticket.Status != "ready")
        {
            throw new LocalSyncConflictException(
                "order_state_conflict",
                $"Kitchen ticket cannot move from {ticket.Status} to ready.");
        }

        if (ticket.Status != "ready")
        {
            ticket.Status = "ready";
            ticket.StartedAt ??= DateTimeOffset.UtcNow;
            ticket.ReadyAt = DateTimeOffset.UtcNow;
            ticket.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await db.KitchenTicketItems
                .Where(value => value.KitchenTicketId == ticket.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(value => value.Status, "ready"),
                    cancellationToken);

            await SynchronizeOrderStatusAsync(db, ticket.OrderId, cancellationToken);
        }

        var snapshot = await TicketSnapshotAsync(db, ticket, cancellationToken);
        AddChange(db, "kitchen_ticket", ticket.Id, null, snapshot);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }

    public async Task ConfigurePrinterAsync(
        string stationId,
        string printerName,
        int copies,
        bool enabled,
        CancellationToken cancellationToken)
    {
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var stationExists = await db.KitchenStations
            .AnyAsync(value => value.Id == stationId && value.IsActive, cancellationToken);

        if (!stationExists)
        {
            throw new LocalSyncConflictException("dependency_missing", "Kitchen station does not exist.");
        }

        var binding = await db.KitchenPrinterBindings.FindAsync([stationId], cancellationToken);

        if (binding is null)
        {
            binding = new LocalKitchenPrinterBinding
            {
                KitchenStationId = stationId,
                PrinterName = printerName.Trim(),
            };
            db.KitchenPrinterBindings.Add(binding);
        }

        binding.PrinterName = printerName.Trim();
        binding.Copies = Math.Clamp(copies, 1, 5);
        binding.IsEnabled = enabled;
        binding.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SynchronizeOrderStatusAsync(
        RestaurantDbContext db,
        string orderId,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .SingleAsync(value => value.Id == orderId, cancellationToken);

        var statuses = await db.KitchenTickets
            .Where(value => value.OrderId == orderId)
            .Select(value => value.Status)
            .ToArrayAsync(cancellationToken);

        var previous = order.Status;

        if (statuses.Length > 0 &&
            statuses.All(status => status is "ready" or "completed"))
        {
            order.Status = "ready";
        }
        else if (statuses.Any(status => status == "preparing") &&
                 order.Status == "submitted")
        {
            order.Status = "preparing";
        }

        if (!string.Equals(previous, order.Status, StringComparison.Ordinal))
        {
            order.UpdatedAtUtc = DateTimeOffset.UtcNow;
            var snapshot = await BasicOrderSnapshotAsync(db, order, cancellationToken);
            AddChange(db, "order", order.Id, order.WaiterId, snapshot);
        }
    }

    public static async Task<object> TicketSnapshotAsync(
        RestaurantDbContext db,
        LocalKitchenTicket ticket,
        CancellationToken cancellationToken)
    {
        var station = await db.KitchenStations
            .AsNoTracking()
            .SingleAsync(value => value.Id == ticket.KitchenStationId, cancellationToken);

        var items = await db.KitchenTicketItems
            .Where(value => value.KitchenTicketId == ticket.Id)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        return new
        {
            id = ticket.Id,
            ticket_number = ticket.TicketNumber,
            status = ticket.Status,
            queued_at = ticket.QueuedAt,
            started_at = ticket.StartedAt,
            ready_at = ticket.ReadyAt,
            completed_at = ticket.CompletedAt,
            station = new
            {
                id = station.Id,
                code = station.Code,
                name = station.Name,
            },
            items = items.Select(item => new
            {
                id = item.Id,
                order_item_id = item.OrderItemId,
                item_name = item.ItemName,
                quantity = item.Quantity,
                notes = item.Notes,
                status = item.Status,
            }).ToArray(),
        };
    }

    private static async Task<object> BasicOrderSnapshotAsync(
        RestaurantDbContext db,
        LocalOrder order,
        CancellationToken cancellationToken)
    {
        var tickets = await db.KitchenTickets
            .Where(value => value.OrderId == order.Id)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        var ticketSnapshots = new List<object>(tickets.Length);
        foreach (var ticket in tickets)
        {
            ticketSnapshots.Add(await TicketSnapshotAsync(db, ticket, cancellationToken));
        }

        return new
        {
            id = order.Id,
            client_order_id = order.ClientOrderId,
            status = order.Status,
            subtotal = order.Subtotal.ToString("0.00"),
            total = order.Total.ToString("0.00"),
            kitchen_tickets = ticketSnapshots.ToArray(),
        };
    }

    private static async Task<LocalKitchenStation> EnsureGeneralStationAsync(
        RestaurantDbContext db,
        string branchId,
        CancellationToken cancellationToken)
    {
        var station = await db.KitchenStations
            .SingleOrDefaultAsync(
                value => value.BranchId == branchId && value.Code == "GENERAL",
                cancellationToken);

        if (station is not null)
        {
            station.IsActive = true;
            return station;
        }

        station = new LocalKitchenStation
        {
            Id = Guid.CreateVersion7().ToString("N"),
            BranchId = branchId,
            Code = "GENERAL",
            Name = "General Kitchen",
            SortOrder = 999,
            IsActive = true,
        };

        db.KitchenStations.Add(station);
        await db.SaveChangesAsync(cancellationToken);
        return station;
    }

    private static async Task<string> BuildKotTextAsync(
        RestaurantDbContext db,
        LocalKitchenTicket ticket,
        LocalKitchenStation station,
        LocalOrder order,
        CancellationToken cancellationToken)
    {
        var table = await db.DiningTables
            .AsNoTracking()
            .SingleAsync(value => value.Id == order.DiningTableId, cancellationToken);

        var items = await db.KitchenTicketItems
            .Where(value => value.KitchenTicketId == ticket.Id)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        var builder = new StringBuilder();
        builder.AppendLine("BUSINESSOS RESTAURANT");
        builder.AppendLine($"KOT: {ticket.TicketNumber}");
        builder.AppendLine($"STATION: {station.Name}");
        builder.AppendLine($"TABLE: {table.Name} ({table.Code})");
        builder.AppendLine($"WAITER: {order.WaiterName}");
        builder.AppendLine($"TIME: {ticket.QueuedAt:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine("--------------------------------");

        foreach (var item in items)
        {
            builder.AppendLine($"{item.Quantity} x {item.ItemName}");
            if (!string.IsNullOrWhiteSpace(item.Notes))
            {
                builder.AppendLine($"  NOTE: {item.Notes}");
            }
        }

        builder.AppendLine("--------------------------------");
        if (!string.IsNullOrWhiteSpace(order.Notes))
        {
            builder.AppendLine($"ORDER NOTE: {order.Notes}");
        }

        return builder.ToString();
    }

    private static void EnsureKitchenRole(LocalTerminalPrincipal actor)
    {
        if (actor.UserRole is not ("owner" or "manager" or "kitchen"))
        {
            throw new LocalSyncConflictException(
                "forbidden",
                "This user role cannot operate the kitchen display.",
                "rejected");
        }
    }

    private static void AddChange(
        RestaurantDbContext db,
        string entityType,
        string entityId,
        long? ownerUserId,
        object data)
    {
        db.Changes.Add(new LocalChange
        {
            EntityType = entityType,
            EntityId = entityId,
            Operation = "upsert",
            OwnerUserId = ownerUserId,
            DataJson = JsonSerializer.Serialize(data, JsonOptions),
            OccurredAtUtc = DateTimeOffset.UtcNow,
        });
    }
}
