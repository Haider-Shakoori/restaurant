using System.Text;
using System.Text.Json;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.LocalServer;

public sealed class LocalKitchenService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalDatabaseFactory _databaseFactory;
    private readonly LocalInventoryService _inventory;

    public LocalKitchenService(
        LocalDatabaseFactory databaseFactory,
        LocalInventoryService? inventory = null)
    {
        _databaseFactory = databaseFactory;
        _inventory = inventory ?? new LocalInventoryService(databaseFactory);
    }

    // Backward-compatible one-shot entry point. New ordering code should use
    // DispatchRoundAsync so only unsent lines are dispatched.
    public async Task DispatchAsync(
        RestaurantDbContext db,
        LocalOrder order,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        var existing = await db.KitchenTickets
            .AnyAsync(value => value.OrderId == order.Id, cancellationToken);
        if (existing)
        {
            return;
        }

        var settings = await LocalRestaurantSettingsService.GetAsync(db, cancellationToken);
        var items = await db.OrderItems
            .Where(value => value.OrderId == order.Id && value.Status == "pending")
            .ToArrayAsync(cancellationToken);
        if (items.Length == 0)
        {
            return;
        }

        var round = await CreateRoundAsync(
            db,
            order,
            actor,
            mutationId: $"legacy-submit:{order.Id}",
            settings,
            cancellationToken);

        await DispatchRoundAsync(db, order, round, items, actor, cancellationToken);
    }

    public async Task<LocalKotRound> CreateRoundAsync(
        RestaurantDbContext db,
        LocalOrder order,
        LocalTerminalPrincipal actor,
        string mutationId,
        RestaurantWorkflowSettings settings,
        CancellationToken cancellationToken)
    {
        var existing = await db.KotRounds
            .SingleOrDefaultAsync(
                value => value.OrderId == order.Id && value.MutationId == mutationId,
                cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var branchId = await ResolveBranchIdAsync(db, order, cancellationToken);

        var nextRound = (await db.KotRounds
            .Where(value => value.OrderId == order.Id)
            .Select(value => (int?)value.RoundNumber)
            .MaxAsync(cancellationToken) ?? 0) + 1;

        var sequence = await NextKotNumberAsync(db, branchId, cancellationToken);
        var round = new LocalKotRound
        {
            Id = Guid.CreateVersion7().ToString("N"),
            OrderId = order.Id,
            BranchId = branchId,
            RoundNumber = nextRound,
            DisplayNumber = sequence.DisplayNumber,
            BusinessDate = sequence.BusinessDate,
            KotNumber = sequence.KotNumber,
            MutationId = mutationId,
            SubmittedByUserId = actor.UserId,
            QueueEnabled = settings.KitchenQueueEnabled,
            PreparingEnabled = settings.PreparingStageEnabled,
            ExpoEnabled = settings.ExpoEnabled,
            CoursesEnabled = settings.CoursesEnabled,
            SentAt = DateTimeOffset.UtcNow,
        };

        db.KotRounds.Add(round);
        await db.SaveChangesAsync(cancellationToken);
        return round;
    }

    public async Task DispatchRoundAsync(
        RestaurantDbContext db,
        LocalOrder order,
        LocalKotRound round,
        IReadOnlyCollection<LocalOrderItem> items,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        var alreadyDispatched = await db.KitchenTickets
            .AnyAsync(value => value.KotRoundId == round.Id, cancellationToken);
        if (alreadyDispatched)
        {
            return;
        }

        var branchId = await ResolveBranchIdAsync(db, order, cancellationToken);

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
            if (item.Status is "held")
            {
                continue;
            }

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
            var initialStatus = round.QueueEnabled ? "queued" : "active";
            var priority = pair.Value.Any(value => value.Priority == "rush") ? "rush" : "normal";

            var ticket = new LocalKitchenTicket
            {
                Id = Guid.CreateVersion7().ToString("N"),
                OrderId = order.Id,
                KotRoundId = round.Id,
                RoundNumber = round.RoundNumber,
                KitchenStationId = station.Id,
                SubmittedByUserId = actor.UserId,
                TicketNumber = $"{round.KotNumber}-{NormalizeCode(station.Code)}",
                KotNumber = round.KotNumber,
                Priority = priority,
                Status = initialStatus,
                QueuedAt = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };

            db.KitchenTickets.Add(ticket);
            var createdKitchenItems = new List<LocalKitchenTicketItem>();

            foreach (var orderItem in pair.Value)
            {
                var kitchenItem = new LocalKitchenTicketItem
                {
                    Id = Guid.CreateVersion7().ToString("N"),
                    KitchenTicketId = ticket.Id,
                    OrderItemId = orderItem.Id,
                    ItemName = orderItem.ItemName,
                    Quantity = orderItem.Quantity,
                    Notes = orderItem.Notes,
                    Status = initialStatus,
                    SeatNumber = orderItem.SeatNumber,
                    CourseNumber = orderItem.CourseNumber,
                    CourseName = orderItem.CourseName,
                    Priority = orderItem.Priority,
                    ModifiersJson = orderItem.ModifiersJson,
                    AllergyInstructions = orderItem.AllergyInstructions,
                    KitchenInstructions = orderItem.KitchenInstructions,
                };
                db.KitchenTicketItems.Add(kitchenItem);
                createdKitchenItems.Add(kitchenItem);

                orderItem.KotRoundId = round.Id;
                orderItem.RoundNumber = round.RoundNumber;
                orderItem.Status = initialStatus;
                orderItem.UpdatedAtUtc = now;
            }

            await db.SaveChangesAsync(cancellationToken);

            foreach (var kitchenItem in createdKitchenItems)
            {
                await _inventory.ReserveKitchenItemAsync(db, kitchenItem, actor, cancellationToken);

                if (!round.QueueEnabled && !round.PreparingEnabled)
                {
                    await _inventory.CommitKitchenItemAsync(db, kitchenItem, actor, cancellationToken);
                }
            }

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
                    DocumentName = $"{round.KotNumber} - {station.Name}",
                    PayloadText = await BuildKotTextAsync(db, ticket, station, order, cancellationToken),
                    Copies = Math.Clamp(printer.Copies, 1, 5),
                    Status = "pending",
                    Attempts = 0,
                    CreatedAtUtc = now,
                });
            }
        }

        LocalOperationsControlService.AddAudit(
            db,
            actor,
            "kitchen",
            "kot.round_sent",
            branchId,
            "kot_round",
            round.Id,
            new
            {
                order_id = order.Id,
                round_number = round.RoundNumber,
                kot_number = round.KotNumber,
                queue_enabled = round.QueueEnabled,
                preparing_enabled = round.PreparingEnabled,
            });

        AddChange(db, "kot_round", round.Id, order.WaiterId, RoundSnapshot(round));
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
                            value.Status == "active" ||
                            value.Status == "preparing" ||
                            value.Status == "ready")
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(stationId))
        {
            query = query.Where(value => value.KitchenStationId == stationId);
        }

        var tickets = (await query.ToArrayAsync(cancellationToken))
            .OrderByDescending(value => value.Priority == "rush")
            .ThenBy(value => value.QueuedAt)
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

        var ticket = await RequireTicketAsync(db, ticketId, cancellationToken);
        var round = await RequireRoundAsync(db, ticket, cancellationToken);
        if (!round.PreparingEnabled)
        {
            throw new LocalSyncConflictException(
                "order_state_conflict",
                "Preparing is disabled for this KOT round; use Mark Ready.");
        }

        var itemIds = await db.KitchenTicketItems
            .Where(value => value.KitchenTicketId == ticket.Id &&
                            (value.Status == "queued" || value.Status == "active"))
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);

        if (itemIds.Length == 0 && ticket.Status != "preparing")
        {
            throw new LocalSyncConflictException(
                "order_state_conflict",
                $"KOT {ticket.KotNumber ?? ticket.TicketNumber} has no items that can start.");
        }

        foreach (var itemId in itemIds)
        {
            await StartItemCoreAsync(db, ticket, round, itemId, actor, cancellationToken);
        }

        await RefreshTicketAndOrderAsync(db, ticket, cancellationToken);
        var snapshot = await TicketSnapshotAsync(db, ticket, cancellationToken);
        AddChange(db, "kitchen_ticket", ticket.Id, null, snapshot);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }

    public async Task<object> StartItemAsync(
        string ticketItemId,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureKitchenRole(actor);
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var item = await db.KitchenTicketItems
            .SingleOrDefaultAsync(value => value.Id == ticketItemId, cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "Kitchen item does not exist.");
        var ticket = await RequireTicketAsync(db, item.KitchenTicketId, cancellationToken);
        var round = await RequireRoundAsync(db, ticket, cancellationToken);

        await StartItemCoreAsync(db, ticket, round, item.Id, actor, cancellationToken);
        await RefreshTicketAndOrderAsync(db, ticket, cancellationToken);

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

        var ticket = await RequireTicketAsync(db, ticketId, cancellationToken);
        var round = await RequireRoundAsync(db, ticket, cancellationToken);
        var items = await db.KitchenTicketItems
            .Where(value => value.KitchenTicketId == ticket.Id &&
                            value.Status != "ready" &&
                            value.Status != "completed" &&
                            value.Status != "voided" &&
                            value.Status != "cancelled")
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);

        foreach (var itemId in items)
        {
            await ReadyItemCoreAsync(db, ticket, round, itemId, actor, cancellationToken);
        }

        await RefreshTicketAndOrderAsync(db, ticket, cancellationToken);
        var snapshot = await TicketSnapshotAsync(db, ticket, cancellationToken);
        AddChange(db, "kitchen_ticket", ticket.Id, null, snapshot);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }

    public async Task<object> ReadyItemAsync(
        string ticketItemId,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureKitchenRole(actor);
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var item = await db.KitchenTicketItems
            .SingleOrDefaultAsync(value => value.Id == ticketItemId, cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "Kitchen item does not exist.");
        var ticket = await RequireTicketAsync(db, item.KitchenTicketId, cancellationToken);
        var round = await RequireRoundAsync(db, ticket, cancellationToken);

        await ReadyItemCoreAsync(db, ticket, round, item.Id, actor, cancellationToken);
        await RefreshTicketAndOrderAsync(db, ticket, cancellationToken);

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

    public static async Task<object> TicketSnapshotAsync(
        RestaurantDbContext db,
        LocalKitchenTicket ticket,
        CancellationToken cancellationToken)
    {
        var station = await db.KitchenStations
            .AsNoTracking()
            .SingleAsync(value => value.Id == ticket.KitchenStationId, cancellationToken);

        var round = ticket.KotRoundId is null
            ? null
            : await db.KotRounds.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == ticket.KotRoundId, cancellationToken);

        var items = await db.KitchenTicketItems
            .Where(value => value.KitchenTicketId == ticket.Id)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        return new
        {
            id = ticket.Id,
            ticket_number = ticket.TicketNumber,
            kot_number = ticket.KotNumber ?? round?.KotNumber ?? ticket.TicketNumber,
            round_id = ticket.KotRoundId,
            round_number = ticket.RoundNumber,
            status = ticket.Status,
            priority = ticket.Priority,
            queued_at = ticket.QueuedAt,
            started_at = ticket.StartedAt,
            ready_at = ticket.ReadyAt,
            completed_at = ticket.CompletedAt,
            workflow = round is null ? null : new
            {
                kitchen_queue_enabled = round.QueueEnabled,
                preparing_stage_enabled = round.PreparingEnabled,
                expo_enabled = round.ExpoEnabled,
                courses_enabled = round.CoursesEnabled,
            },
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
                seat_number = item.SeatNumber,
                course_number = item.CourseNumber,
                course_name = item.CourseName,
                priority = item.Priority,
                modifiers = ParseJson(item.ModifiersJson),
                allergy_instructions = item.AllergyInstructions,
                kitchen_instructions = item.KitchenInstructions,
                started_at = item.StartedAt,
                ready_at = item.ReadyAt,
                completed_at = item.CompletedAt,
                voided_at = item.VoidedAt,
                void_reason = item.VoidReason,
                refire_of_kitchen_item_id = item.RefireOfKitchenItemId,
            }).ToArray(),
        };
    }

    public static object RoundSnapshot(LocalKotRound round) => new
    {
        id = round.Id,
        order_id = round.OrderId,
        branch_id = round.BranchId,
        round_number = round.RoundNumber,
        display_number = round.DisplayNumber,
        business_date = round.BusinessDate,
        kot_number = round.KotNumber,
        client_dispatch_id = round.MutationId,
        submitted_by_user_id = round.SubmittedByUserId,
        dispatched_at = round.SentAt,
        sent_at = round.SentAt,
        workflow = new
        {
            kitchen_queue_enabled = round.QueueEnabled,
            preparing_stage_enabled = round.PreparingEnabled,
            expo_enabled = round.ExpoEnabled,
            courses_enabled = round.CoursesEnabled,
        },
    };

    private async Task StartItemCoreAsync(
        RestaurantDbContext db,
        LocalKitchenTicket ticket,
        LocalKotRound round,
        string itemId,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        if (!round.PreparingEnabled)
        {
            throw new LocalSyncConflictException(
                "order_state_conflict",
                "Preparing is disabled for this KOT round.");
        }

        var item = await db.KitchenTicketItems
            .SingleAsync(value => value.Id == itemId, cancellationToken);

        if (item.Status == "preparing")
        {
            return;
        }

        if (item.Status is not ("queued" or "active"))
        {
            throw new LocalSyncConflictException(
                "order_state_conflict",
                $"Kitchen item cannot move from {item.Status} to preparing.");
        }

        var now = DateTimeOffset.UtcNow;
        item.Status = "preparing";
        item.StartedAt ??= now;

        var orderItem = await db.OrderItems
            .SingleAsync(value => value.Id == item.OrderItemId, cancellationToken);
        orderItem.Status = "preparing";
        orderItem.UpdatedAtUtc = now;

        await _inventory.CommitKitchenItemAsync(db, item, actor, cancellationToken);

        LocalOperationsControlService.AddAudit(
            db,
            actor,
            "kitchen",
            "kitchen.item_started",
            null,
            "kitchen_ticket_item",
            item.Id,
            new { ticket_id = ticket.Id, order_item_id = item.OrderItemId, round_number = ticket.RoundNumber });
    }

    private async Task ReadyItemCoreAsync(
        RestaurantDbContext db,
        LocalKitchenTicket ticket,
        LocalKotRound round,
        string itemId,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        var item = await db.KitchenTicketItems
            .SingleAsync(value => value.Id == itemId, cancellationToken);

        if (item.Status == "ready")
        {
            return;
        }

        if (item.Status is "completed" or "voided" or "cancelled")
        {
            throw new LocalSyncConflictException(
                "order_state_conflict",
                $"Kitchen item cannot move from {item.Status} to ready.");
        }

        if (round.PreparingEnabled && item.Status != "preparing")
        {
            throw new LocalSyncConflictException(
                "order_state_conflict",
                "This KOT round uses the Preparing stage. Start the item before marking it ready.");
        }

        if (!round.PreparingEnabled && item.Status is not ("queued" or "active"))
        {
            throw new LocalSyncConflictException(
                "order_state_conflict",
                $"Kitchen item cannot move from {item.Status} to ready.");
        }

        var now = DateTimeOffset.UtcNow;
        item.Status = "ready";
        item.ReadyAt ??= now;

        var orderItem = await db.OrderItems
            .SingleAsync(value => value.Id == item.OrderItemId, cancellationToken);
        orderItem.Status = "ready";
        orderItem.UpdatedAtUtc = now;

        if (!round.PreparingEnabled)
        {
            await _inventory.CommitKitchenItemAsync(db, item, actor, cancellationToken);
        }

        LocalOperationsControlService.AddAudit(
            db,
            actor,
            "kitchen",
            "kitchen.item_ready",
            null,
            "kitchen_ticket_item",
            item.Id,
            new { ticket_id = ticket.Id, order_item_id = item.OrderItemId, round_number = ticket.RoundNumber });
    }

    private static async Task RefreshTicketAndOrderAsync(
        RestaurantDbContext db,
        LocalKitchenTicket ticket,
        CancellationToken cancellationToken)
    {
        var items = await db.KitchenTicketItems
            .Where(value => value.KitchenTicketId == ticket.Id)
            .ToArrayAsync(cancellationToken);

        var previous = ticket.Status;
        if (items.Length > 0 && items.All(value => value.Status is "completed" or "voided" or "cancelled"))
        {
            ticket.Status = "completed";
            ticket.CompletedAt ??= DateTimeOffset.UtcNow;
        }
        else if (items.Length > 0 && items.All(value => value.Status is "ready" or "completed" or "voided" or "cancelled"))
        {
            ticket.Status = "ready";
            ticket.ReadyAt ??= DateTimeOffset.UtcNow;
        }
        else if (items.Any(value => value.Status == "preparing"))
        {
            ticket.Status = "preparing";
            ticket.StartedAt ??= items.Where(value => value.StartedAt.HasValue)
                .Select(value => value.StartedAt!.Value)
                .DefaultIfEmpty(DateTimeOffset.UtcNow)
                .Min();
        }
        else if (items.Any(value => value.Status == "active"))
        {
            ticket.Status = "active";
        }
        else
        {
            ticket.Status = "queued";
        }

        if (!string.Equals(previous, ticket.Status, StringComparison.Ordinal))
        {
            ticket.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await SynchronizeOrderStatusAsync(db, ticket.OrderId, cancellationToken);
    }

    private static async Task SynchronizeOrderStatusAsync(
        RestaurantDbContext db,
        string orderId,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.SingleAsync(value => value.Id == orderId, cancellationToken);
        var itemStatuses = await db.KitchenTicketItems
            .Where(value => db.KitchenTickets
                .Where(ticket => ticket.OrderId == orderId)
                .Select(ticket => ticket.Id)
                .Contains(value.KitchenTicketId))
            .Select(value => value.Status)
            .ToArrayAsync(cancellationToken);

        if (itemStatuses.Length == 0)
        {
            return;
        }

        var previous = order.Status;
        if (itemStatuses.All(status => status is "ready" or "completed" or "voided" or "cancelled"))
        {
            order.Status = "ready";
        }
        else if (itemStatuses.Any(status => status == "preparing"))
        {
            order.Status = "preparing";
        }
        else
        {
            order.Status = "submitted";
        }

        if (!string.Equals(previous, order.Status, StringComparison.Ordinal))
        {
            order.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddChange(db, "order", order.Id, order.WaiterId, await BasicOrderSnapshotAsync(db, order, cancellationToken));
        }
    }

    private static async Task<object> BasicOrderSnapshotAsync(
        RestaurantDbContext db,
        LocalOrder order,
        CancellationToken cancellationToken)
    {
        var tickets = await db.KitchenTickets
            .Where(value => value.OrderId == order.Id)
            .OrderBy(value => value.RoundNumber)
            .ThenBy(value => value.QueuedAt)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        var ticketSnapshots = new List<object>(tickets.Length);
        foreach (var ticket in tickets)
        {
            ticketSnapshots.Add(await TicketSnapshotAsync(db, ticket, cancellationToken));
        }

        var rounds = await db.KotRounds
            .Where(value => value.OrderId == order.Id)
            .OrderBy(value => value.RoundNumber)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        return new
        {
            id = order.Id,
            client_order_id = order.ClientOrderId,
            status = order.Status,
            subtotal = order.Subtotal.ToString("0.00"),
            total = order.Total.ToString("0.00"),
            kot_rounds = rounds.Select(RoundSnapshot).ToArray(),
            kitchen_tickets = ticketSnapshots.ToArray(),
        };
    }

    private static async Task<LocalKitchenTicket> RequireTicketAsync(
        RestaurantDbContext db,
        string ticketId,
        CancellationToken cancellationToken) =>
        await db.KitchenTickets.SingleOrDefaultAsync(value => value.Id == ticketId, cancellationToken)
        ?? throw new LocalSyncConflictException("dependency_missing", "Kitchen ticket does not exist.");

    private static async Task<LocalKotRound> RequireRoundAsync(
        RestaurantDbContext db,
        LocalKitchenTicket ticket,
        CancellationToken cancellationToken)
    {
        if (ticket.KotRoundId is not null)
        {
            var round = await db.KotRounds
                .SingleOrDefaultAsync(value => value.Id == ticket.KotRoundId, cancellationToken);
            if (round is not null)
            {
                return round;
            }
        }

        // Legacy KOTs keep their historical fixed queue+preparing behavior.
        return new LocalKotRound
        {
            Id = ticket.KotRoundId ?? $"legacy:{ticket.Id}",
            OrderId = ticket.OrderId,
            BranchId = string.Empty,
            RoundNumber = ticket.RoundNumber <= 0 ? 1 : ticket.RoundNumber,
            DisplayNumber = ParseDisplayNumber(ticket.KotNumber ?? ticket.TicketNumber),
            BusinessDate = DateOnly.FromDateTime(ticket.QueuedAt.LocalDateTime),
            KotNumber = ticket.KotNumber ?? ticket.TicketNumber,
            MutationId = $"legacy:{ticket.Id}",
            SubmittedByUserId = ticket.SubmittedByUserId,
            QueueEnabled = true,
            PreparingEnabled = true,
            ExpoEnabled = false,
            CoursesEnabled = false,
            SentAt = ticket.QueuedAt,
        };
    }

    private static async Task<KotSequence> NextKotNumberAsync(
        RestaurantDbContext db,
        string branchId,
        CancellationToken cancellationToken)
    {
        var businessDate = DateOnly.FromDateTime(DateTime.Now);
        var counter = await db.KotCounters
            .SingleOrDefaultAsync(
                value => value.BranchId == branchId && value.BusinessDate == businessDate,
                cancellationToken);

        if (counter is null)
        {
            counter = new LocalKotCounter
            {
                BranchId = branchId,
                BusinessDate = businessDate,
                LastNumber = 0,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            db.KotCounters.Add(counter);
        }

        counter.LastNumber += 1;
        counter.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new KotSequence(
            counter.LastNumber,
            businessDate,
            $"KOT-{counter.LastNumber:0000}");
    }

    private static int ParseDisplayNumber(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) ? number : 0;
    }

    private sealed record KotSequence(int DisplayNumber, DateOnly BusinessDate, string KotNumber);

    private static async Task<string> ResolveBranchIdAsync(
        RestaurantDbContext db,
        LocalOrder order,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(order.BranchId))
        {
            return order.BranchId;
        }

        if (string.IsNullOrWhiteSpace(order.DiningTableId))
        {
            throw new LocalSyncConflictException(
                "dependency_missing",
                "Order branch context is unavailable.");
        }

        var table = await db.DiningTables.SingleAsync(
            value => value.Id == order.DiningTableId,
            cancellationToken);
        var area = await db.DiningAreas.SingleAsync(
            value => value.Id == table.DiningAreaId,
            cancellationToken);
        order.BranchId = area.BranchId;
        return area.BranchId;
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
        LocalDiningTable? table = null;
        if (!string.IsNullOrWhiteSpace(order.DiningTableId))
        {
            table = await db.DiningTables
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == order.DiningTableId, cancellationToken);
        }

        var items = await db.KitchenTicketItems
            .Where(value => value.KitchenTicketId == ticket.Id)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        var builder = new StringBuilder();
        builder.AppendLine("BUSINESSOS RESTAURANT");
        builder.AppendLine($"KOT: {ticket.KotNumber ?? ticket.TicketNumber}");
        builder.AppendLine($"ROUND: {ticket.RoundNumber}");
        builder.AppendLine($"STATION: {station.Name}");
        builder.AppendLine(order.ServiceType switch
        {
            "takeaway" => $"TAKEAWAY: {order.ServiceReference ?? order.ClientOrderId}",
            "delivery" => $"DELIVERY: {order.ServiceReference ?? order.ClientOrderId}",
            "counter" => $"COUNTER: {order.ServiceReference ?? order.ClientOrderId}",
            _ when table is not null => $"DINE-IN: {table.Name} ({table.Code})",
            _ => "DINE-IN",
        });
        builder.AppendLine($"WAITER: {order.WaiterName}");
        builder.AppendLine($"PRIORITY: {ticket.Priority.ToUpperInvariant()}");
        builder.AppendLine($"TIME: {ticket.QueuedAt:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine("--------------------------------");

        foreach (var item in items)
        {
            builder.AppendLine($"{item.Quantity} x {item.ItemName}");
            if (item.SeatNumber.HasValue) builder.AppendLine($"  SEAT: {item.SeatNumber.Value}");
            if (item.CourseNumber.HasValue) builder.AppendLine($"  COURSE: {item.CourseNumber.Value} {item.CourseName}".TrimEnd());
            foreach (var modifier in ModifierLines(item.ModifiersJson)) builder.AppendLine($"  {modifier}");
            if (!string.IsNullOrWhiteSpace(item.KitchenInstructions)) builder.AppendLine($"  INSTRUCTION: {item.KitchenInstructions}");
            if (!string.IsNullOrWhiteSpace(item.Notes)) builder.AppendLine($"  NOTE: {item.Notes}");
            if (!string.IsNullOrWhiteSpace(item.AllergyInstructions)) builder.AppendLine($"  !!! ALLERGY: {item.AllergyInstructions} !!!");
        }

        builder.AppendLine("--------------------------------");
        if (!string.IsNullOrWhiteSpace(order.Notes)) builder.AppendLine($"ORDER NOTE: {order.Notes}");
        return builder.ToString();
    }

    private static IEnumerable<string> ModifierLines(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            yield break;
        }

        JsonElement root;
        try { root = JsonSerializer.Deserialize<JsonElement>(json); }
        catch (JsonException) { yield break; }

        if (root.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var value in root.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text)) yield return text;
                continue;
            }

            if (value.ValueKind == JsonValueKind.Object &&
                value.TryGetProperty("name", out var name) &&
                !string.IsNullOrWhiteSpace(name.GetString()))
            {
                yield return $"+ {name.GetString()}";
            }
        }
    }

    private static object? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch (JsonException) { return null; }
    }

    private static string NormalizeCode(string code)
    {
        var safe = new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return string.IsNullOrWhiteSpace(safe) ? "GENERAL" : safe;
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
