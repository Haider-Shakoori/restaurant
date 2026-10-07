using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.LocalServer;

public sealed class LocalSyncService
{
    private static readonly string[] ActiveStatuses =
    [
        "draft",
        "submitted",
        "preparing",
        "ready",
        "served",
        "billed",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalDatabaseFactory _databaseFactory;
    private readonly OperationalSnapshotStore _catalogStore;
    private readonly LocalKitchenService _kitchen;

    public LocalSyncService(
        LocalDatabaseFactory databaseFactory,
        OperationalSnapshotStore catalogStore,
        LocalKitchenService? kitchen = null)
    {
        _databaseFactory = databaseFactory;
        _catalogStore = catalogStore;
        _kitchen = kitchen ?? new LocalKitchenService(databaseFactory);
    }

    public async Task<object> BootstrapAsync(
        LocalTerminalPrincipal principal,
        CancellationToken cancellationToken)
    {
        var catalog = await _catalogStore.LoadCatalogAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var cursor = await db.Changes
            .Select(value => (long?)value.Sequence)
            .MaxAsync(cancellationToken) ?? 0;

        var orders = await ActiveOrdersAsync(db, principal, cancellationToken);
        var workflowSettings = await LocalRestaurantSettingsService.GetAsync(db, cancellationToken);
        var menu = BuildMenu(catalog);
        var tables = BuildTables(catalog);
        var staff = await db.StaffUsers
            .Where(value => value.IsActive)
            .OrderBy(value => value.Name)
            .AsNoTracking()
            .Select(value => new
            {
                id = value.Id,
                public_id = value.PublicId,
                name = value.Name,
                email = value.Email,
                role = value.Role,
                is_active = value.IsActive,
            })
            .ToArrayAsync(cancellationToken);

        return new
        {
            schema_version = 1,
            server_time = DateTimeOffset.UtcNow,
            cursor,
            sync_batch_size = 100,
            tenant_id = principal.TenantId,
            device_id = principal.DeviceId,
            user = new
            {
                id = principal.UserId,
                public_id = principal.UserPublicId,
                name = principal.UserName,
                role = principal.UserRole,
            },
            branches = catalog.Branches.Select(value => new
            {
                id = value.Id,
                code = value.Code,
                name = value.Name,
                is_active = value.IsActive,
            }).ToArray(),
            staff,
            restaurant_settings = LocalRestaurantSettingsService.ToPayload(workflowSettings),
            menu,
            kitchen = new
            {
                stations = catalog.KitchenStations.Select(value => new
                {
                    id = value.Id,
                    branch_id = value.BranchId,
                    code = value.Code,
                    name = value.Name,
                    sort_order = value.SortOrder,
                    is_active = value.IsActive,
                }).ToArray(),
                routes = catalog.KitchenRoutes.Select(value => new
                {
                    id = value.Id,
                    menu_item_id = value.MenuItemId,
                    branch_id = value.BranchId,
                    kitchen_station_id = value.KitchenStationId,
                }).ToArray(),
            },
            tables,
            orders,
        };
    }

    public async Task<object> PushAsync(
        LocalTerminalPrincipal principal,
        LocalSyncPushRequest request,
        CancellationToken cancellationToken)
    {
        var results = new List<Dictionary<string, object?>>();

        foreach (var mutation in request.Mutations)
        {
            results.Add(await ProcessMutationAsync(principal, mutation, cancellationToken));
        }

        await using var db = _databaseFactory.Create();
        var cursor = await db.Changes
            .Select(value => (long?)value.Sequence)
            .MaxAsync(cancellationToken) ?? 0;

        return new
        {
            server_time = DateTimeOffset.UtcNow,
            results,
            pull_cursor = cursor,
        };
    }

    public async Task<object> PullAsync(
        LocalTerminalPrincipal principal,
        long cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 100);
        await using var db = _databaseFactory.Create();

        var scanned = await db.Changes
            .Where(value => value.Sequence > cursor)
            .OrderBy(value => value.Sequence)
            .Take(limit)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        var lastScanned = scanned.Length == 0 ? cursor : scanned[^1].Sequence;

        var visible = scanned
            .Where(change =>
                !string.Equals(principal.UserRole, "waiter", StringComparison.OrdinalIgnoreCase) ||
                change.OwnerUserId is null ||
                change.OwnerUserId == principal.UserId)
            .Select(change => new
            {
                sequence = change.Sequence,
                entity_type = change.EntityType,
                entity_id = change.EntityId,
                operation = change.Operation,
                data = change.DataJson is null
                    ? (JsonElement?)null
                    : JsonSerializer.Deserialize<JsonElement>(change.DataJson, JsonOptions),
            })
            .ToArray();

        var hasMore = await db.Changes
            .AnyAsync(value => value.Sequence > lastScanned, cancellationToken);

        return new
        {
            server_time = DateTimeOffset.UtcNow,
            cursor = lastScanned,
            has_more = hasMore,
            changes = visible,
        };
    }

    private async Task<Dictionary<string, object?>> ProcessMutationAsync(
        LocalTerminalPrincipal principal,
        LocalSyncMutationRequest mutation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mutation.MutationId) || mutation.MutationId.Length > 100)
        {
            return BasicResult(
                mutation.MutationId,
                "rejected",
                mutation.Operation,
                "invalid_payload",
                "A valid mutation ID is required.");
        }

        var requestHash = MutationHash(mutation.Operation, mutation.Payload);

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var existing = await db.Mutations.FindAsync(
            [principal.DeviceId, mutation.MutationId],
            cancellationToken);

        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return BasicResult(
                    mutation.MutationId,
                    "rejected",
                    mutation.Operation,
                    "mutation_id_reused",
                    "This mutation ID was already used with different content.");
            }

            return JsonSerializer.Deserialize<Dictionary<string, object?>>(
                existing.ResponseJson,
                JsonOptions) ?? BasicResult(
                    mutation.MutationId,
                    existing.Status,
                    mutation.Operation,
                    existing.ErrorCode,
                    existing.ErrorMessage);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        Dictionary<string, object?> result;

        try
        {
            result = mutation.Operation switch
            {
                "order.open" => await OpenOrderAsync(db, principal, mutation, cancellationToken),
                "order.item.add" => await AddOrderItemAsync(db, principal, mutation, cancellationToken),
                "order.submit" => await SubmitOrderAsync(db, principal, mutation, cancellationToken),
                "order.kot.send" => await SubmitOrderAsync(db, principal, mutation, cancellationToken),
                _ => throw new LocalSyncConflictException(
                    "unsupported_operation",
                    "Unsupported offline operation."),
            };
        }
        catch (LocalSyncConflictException conflict)
        {
            result = BasicResult(
                mutation.MutationId,
                conflict.Status,
                mutation.Operation,
                conflict.Code,
                conflict.Message);
        }

        if (string.Equals(result.GetValueOrDefault("status")?.ToString(), "accepted", StringComparison.Ordinal))
        {
            var entityType = result.GetValueOrDefault("entity_type")?.ToString() ?? "unknown";
            var localEntityId = result.GetValueOrDefault("entity_id")?.ToString() ?? mutation.MutationId;
            var cloudMutationId = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes($"{principal.DeviceId}:{mutation.MutationId}")));

            LocalCloudOutboxWriter.Enqueue(
                db,
                principal,
                mutation.Operation,
                entityType,
                localEntityId,
                new
                {
                    mutation_payload = mutation.Payload,
                    local_result = result,
                },
                mutation.OccurredAt,
                cloudMutationId);
        }

        var responseJson = JsonSerializer.Serialize(result, JsonOptions);

        db.Mutations.Add(new LocalMutation
        {
            DeviceId = principal.DeviceId,
            MutationId = mutation.MutationId,
            UserId = principal.UserId,
            Operation = mutation.Operation,
            RequestHash = requestHash,
            Status = result["status"]?.ToString() ?? "rejected",
            ErrorCode = result.GetValueOrDefault("code")?.ToString(),
            ErrorMessage = result.GetValueOrDefault("message")?.ToString(),
            ResponseJson = responseJson,
            ClientOccurredAt = mutation.OccurredAt,
            ProcessedAtUtc = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    private async Task<Dictionary<string, object?>> OpenOrderAsync(
        RestaurantDbContext db,
        LocalTerminalPrincipal principal,
        LocalSyncMutationRequest mutation,
        CancellationToken cancellationToken)
    {
        EnsureOrderingRole(principal);

        var payload = mutation.Payload;
        var clientOrderId = RequiredString(payload, "client_order_id", 40);
        var serviceType = (OptionalString(payload, "service_type", 24) ?? "dine_in")
            .Trim()
            .ToLowerInvariant()
            .Replace('-', '_');

        if (serviceType is not ("dine_in" or "takeaway" or "delivery" or "counter"))
        {
            throw new LocalSyncConflictException(
                "invalid_payload",
                "service_type must be dine_in, takeaway, delivery or counter.");
        }

        var guestCount = OptionalInt(payload, "guest_count", 1);
        if (guestCount is < 1 or > 100)
        {
            throw new LocalSyncConflictException("invalid_payload", "Guest count must be between 1 and 100.");
        }

        var existing = await db.Orders
            .SingleOrDefaultAsync(value => value.ClientOrderId == clientOrderId, cancellationToken);

        if (existing is not null)
        {
            AuthorizeOrder(principal, existing);
            return Accepted(
                mutation,
                "order",
                existing.Id,
                existing.ClientOrderId,
                await OrderSnapshotAsync(db, existing, cancellationToken));
        }

        LocalDiningTable? table = null;
        string branchId;
        var tableId = OptionalString(payload, "dining_table_id", 40);

        if (serviceType == "dine_in")
        {
            if (string.IsNullOrWhiteSpace(tableId))
            {
                throw new LocalSyncConflictException(
                    "invalid_payload",
                    "dining_table_id is required for dine-in orders.");
            }

            table = await db.DiningTables.FindAsync([tableId], cancellationToken);
            if (table is null || !table.IsActive || table.Status is "disabled" or "reserved")
            {
                throw new LocalSyncConflictException(
                    "table_busy",
                    "This table is not currently available for dine-in ordering.");
            }

            var busy = await db.Orders.AnyAsync(
                value => value.DiningTableId == tableId && ActiveStatuses.Contains(value.Status),
                cancellationToken);
            if (busy)
            {
                throw new LocalSyncConflictException(
                    "table_busy",
                    "This table already has an active order.");
            }

            var area = await db.DiningAreas.SingleAsync(
                value => value.Id == table.DiningAreaId,
                cancellationToken);
            branchId = area.BranchId;
        }
        else
        {
            branchId = RequiredString(payload, "branch_id", 40);
            var branchExists = await db.Branches.AnyAsync(
                value => value.Id == branchId && value.IsActive,
                cancellationToken);
            if (!branchExists)
            {
                throw new LocalSyncConflictException(
                    "dependency_missing",
                    "The selected branch is inactive or unavailable.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var order = new LocalOrder
        {
            Id = Guid.CreateVersion7().ToString("N"),
            ClientOrderId = clientOrderId,
            DiningTableId = table?.Id ?? string.Empty,
            BranchId = branchId,
            ServiceType = serviceType,
            ServiceReference = OptionalString(payload, "service_reference", 80) ??
                (serviceType == "dine_in" ? null : clientOrderId),
            WaiterId = principal.UserId,
            WaiterPublicId = principal.UserPublicId,
            WaiterName = principal.UserName,
            Status = "draft",
            GuestCount = guestCount,
            Notes = OptionalString(payload, "notes", 2000),
            Subtotal = 0m,
            Total = 0m,
            OpenedAt = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.Orders.Add(order);
        if (table is not null)
        {
            table.Status = "occupied";
        }

        await db.SaveChangesAsync(cancellationToken);

        var snapshot = await OrderSnapshotAsync(db, order, cancellationToken);
        AddChange(db, "order", order.Id, principal.UserId, snapshot);
        if (table is not null)
        {
            AddChange(db, "dining_table", table.Id, null, TableSnapshot(db, table));
        }

        return Accepted(mutation, "order", order.Id, order.ClientOrderId, snapshot);
    }

    private async Task<Dictionary<string, object?>> AddOrderItemAsync(
        RestaurantDbContext db,
        LocalTerminalPrincipal principal,
        LocalSyncMutationRequest mutation,
        CancellationToken cancellationToken)
    {
        EnsureOrderingRole(principal);

        var payload = mutation.Payload;
        var clientOrderId = RequiredString(payload, "client_order_id", 40);
        var clientLineId = RequiredString(payload, "client_line_id", 40);
        var menuItemId = RequiredString(payload, "menu_item_id", 40);
        var quantity = RequiredInt(payload, "quantity");

        if (quantity is < 1 or > 999)
        {
            throw new LocalSyncConflictException("invalid_payload", "Quantity must be between 1 and 999.");
        }

        var order = await db.Orders
            .SingleOrDefaultAsync(value => value.ClientOrderId == clientOrderId, cancellationToken)
            ?? throw new LocalSyncConflictException(
                "dependency_missing",
                "A referenced server record does not exist yet.");

        AuthorizeOrder(principal, order);

        if (order.Status is "billed" or "closed" or "cancelled")
        {
            throw new LocalSyncConflictException(
                "order_state_conflict",
                "Items cannot be added after the order is financially closed or cancelled.");
        }

        var existingLine = await db.OrderItems.SingleOrDefaultAsync(
            value => value.OrderId == order.Id && value.ClientLineId == clientLineId,
            cancellationToken);

        if (existingLine is not null)
        {
            return Accepted(
                mutation,
                "order_item",
                existingLine.Id,
                existingLine.ClientLineId,
                new
                {
                    line = LineSnapshot(existingLine),
                    order = await OrderSnapshotAsync(db, order, cancellationToken),
                });
        }

        var menuItem = await db.MenuItems
            .SingleOrDefaultAsync(
                value => value.Id == menuItemId && value.IsAvailable,
                cancellationToken)
            ?? throw new LocalSyncConflictException(
                "menu_unavailable",
                "The selected menu item is unavailable.");

        var modifiers = await ResolveModifiersAsync(db, menuItem.Id, payload, cancellationToken);
        var priority = (OptionalString(payload, "priority", 16) ?? "normal").Trim().ToLowerInvariant();
        if (priority is not ("normal" or "rush"))
        {
            throw new LocalSyncConflictException("invalid_payload", "priority must be normal or rush.");
        }

        var seatNumber = OptionalNullableInt(payload, "seat_number", 1, 999);
        var courseNumber = OptionalNullableInt(payload, "course_number", 1, 99);
        var courseName = OptionalString(payload, "course_name", 80);
        var workflowSettings = await LocalRestaurantSettingsService.GetAsync(db, cancellationToken);
        var held = OptionalBool(payload, "held", false);
        if (held && (!workflowSettings.CoursesEnabled || courseNumber is null))
        {
            throw new LocalSyncConflictException(
                "invalid_payload",
                "Held items require Courses to be enabled and a course_number.");
        }

        var now = DateTimeOffset.UtcNow;
        var unitPrice = menuItem.Price + modifiers.PriceDelta;
        var line = new LocalOrderItem
        {
            Id = Guid.CreateVersion7().ToString("N"),
            OrderId = order.Id,
            MenuItemId = menuItem.Id,
            ClientLineId = clientLineId,
            ItemName = menuItem.Name,
            UnitPrice = unitPrice,
            Quantity = quantity,
            LineTotal = unitPrice * quantity,
            Notes = OptionalString(payload, "notes", 1000),
            Status = held ? "held" : "pending",
            SeatNumber = seatNumber,
            CourseNumber = courseNumber,
            CourseName = courseName,
            Priority = priority,
            ModifiersJson = modifiers.Json,
            AllergyInstructions = OptionalString(payload, "allergy_instructions", 500),
            KitchenInstructions = OptionalString(payload, "kitchen_instructions", 500),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.OrderItems.Add(line);
        await db.SaveChangesAsync(cancellationToken);

        order.Subtotal = await db.OrderItems
            .Where(value => value.OrderId == order.Id)
            .SumAsync(value => value.LineTotal, cancellationToken);
        order.Total = order.Subtotal;
        order.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        var orderSnapshot = await OrderSnapshotAsync(db, order, cancellationToken);
        AddChange(db, "order", order.Id, order.WaiterId, orderSnapshot);

        return Accepted(
            mutation,
            "order_item",
            line.Id,
            line.ClientLineId,
            new
            {
                line = LineSnapshot(line),
                order = orderSnapshot,
            });
    }

    private async Task<Dictionary<string, object?>> SubmitOrderAsync(
        RestaurantDbContext db,
        LocalTerminalPrincipal principal,
        LocalSyncMutationRequest mutation,
        CancellationToken cancellationToken)
    {
        EnsureOrderingRole(principal);

        var clientOrderId = RequiredString(mutation.Payload, "client_order_id", 40);
        var order = await db.Orders
            .SingleOrDefaultAsync(value => value.ClientOrderId == clientOrderId, cancellationToken)
            ?? throw new LocalSyncConflictException(
                "dependency_missing",
                "A referenced server record does not exist yet.");

        AuthorizeOrder(principal, order);

        if (order.Status is "billed" or "closed" or "cancelled")
        {
            throw new LocalSyncConflictException(
                "order_state_conflict",
                "This order can no longer send kitchen production.");
        }

        var unsent = await db.OrderItems
            .Where(value => value.OrderId == order.Id && value.Status == "pending")
            .OrderBy(value => value.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);

        if (unsent.Length == 0)
        {
            throw new LocalSyncConflictException(
                "order_state_conflict",
                "There are no new unsent items to send to the kitchen.");
        }

        var settings = await LocalRestaurantSettingsService.GetAsync(db, cancellationToken);
        var round = await _kitchen.CreateRoundAsync(
            db,
            order,
            principal,
            mutation.MutationId,
            settings,
            cancellationToken);

        var now = DateTimeOffset.UtcNow;
        order.Status = "submitted";
        order.SubmittedAt ??= now;
        order.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        await _kitchen.DispatchRoundAsync(db, order, round, unsent, principal, cancellationToken);

        var snapshot = await OrderSnapshotAsync(db, order, cancellationToken);
        AddChange(db, "order", order.Id, order.WaiterId, snapshot);

        if (string.Equals(mutation.Operation, "order.submit", StringComparison.Ordinal))
        {
            return Accepted(mutation, "order", order.Id, order.ClientOrderId, snapshot);
        }

        return Accepted(
            mutation,
            "kot_round",
            round.Id,
            null,
            new
            {
                round = LocalKitchenService.RoundSnapshot(round),
                order = snapshot,
            });
    }

    private static Dictionary<string, object?> Accepted(
        LocalSyncMutationRequest mutation,
        string entityType,
        string entityId,
        string? clientEntityId,
        object data) =>
        new(StringComparer.Ordinal)
        {
            ["mutation_id"] = mutation.MutationId,
            ["status"] = "accepted",
            ["operation"] = mutation.Operation,
            ["entity_type"] = entityType,
            ["entity_id"] = entityId,
            ["client_entity_id"] = clientEntityId,
            ["data"] = data,
        };

    private static Dictionary<string, object?> BasicResult(
        string mutationId,
        string status,
        string operation,
        string? code,
        string? message) =>
        new(StringComparer.Ordinal)
        {
            ["mutation_id"] = mutationId,
            ["status"] = status,
            ["operation"] = operation,
            ["code"] = code,
            ["message"] = message,
        };

    private static void EnsureOrderingRole(LocalTerminalPrincipal principal)
    {
        if (principal.UserRole is not ("owner" or "manager" or "cashier" or "waiter"))
        {
            throw new LocalSyncConflictException(
                "forbidden",
                "This user role cannot create restaurant orders.",
                "rejected");
        }
    }

    private static void AuthorizeOrder(LocalTerminalPrincipal principal, LocalOrder order)
    {
        if (string.Equals(principal.UserRole, "waiter", StringComparison.OrdinalIgnoreCase) &&
            order.WaiterId != principal.UserId)
        {
            throw new LocalSyncConflictException(
                "order_state_conflict",
                "This order belongs to another waiter.");
        }
    }

    private async Task<object[]> ActiveOrdersAsync(
        RestaurantDbContext db,
        LocalTerminalPrincipal principal,
        CancellationToken cancellationToken)
    {
        var query = db.Orders
            .Where(value => ActiveStatuses.Contains(value.Status))
            .AsNoTracking();

        if (string.Equals(principal.UserRole, "waiter", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(value => value.WaiterId == principal.UserId);
        }

        var orders = (await query.ToArrayAsync(cancellationToken))
            .OrderBy(value => value.OpenedAt)
            .ToArray();
        var result = new List<object>(orders.Length);

        foreach (var order in orders)
        {
            result.Add(await OrderSnapshotAsync(db, order, cancellationToken));
        }

        return result.ToArray();
    }

    private static object[] BuildMenu(LocalCatalogSnapshot catalog)
    {
        var linksByItem = catalog.MenuItemModifierGroups
            .GroupBy(value => value.MenuItemId)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var optionsByGroup = catalog.ModifierOptions
            .GroupBy(value => value.ModifierGroupId)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        return catalog.Categories.Select(category => new
        {
            id = category.Id,
            name = category.Name,
            sort_order = category.SortOrder,
            items = catalog.Items
                .Where(item => item.MenuCategoryId == category.Id)
                .Select(item => new
                {
                    id = item.Id,
                    menu_category_id = item.MenuCategoryId,
                    sku = item.Sku,
                    name = item.Name,
                    description = item.Description,
                    image_url = item.ImageUrl,
                    price = item.Price.ToString("0.00"),
                    currency = item.Currency,
                    sort_order = item.SortOrder,
                    modifier_groups = linksByItem.TryGetValue(item.Id, out var itemLinks)
                        ? itemLinks
                            .Where(link => catalog.ModifierGroups.ContainsKey(link.ModifierGroupId))
                            .Select(link =>
                            {
                                var group = catalog.ModifierGroups[link.ModifierGroupId];
                                return new
                                {
                                    id = group.Id,
                                    name = group.Name,
                                    min_selections = group.MinSelections,
                                    max_selections = group.MaxSelections,
                                    sort_order = link.SortOrder,
                                    options = optionsByGroup.TryGetValue(group.Id, out var groupOptions)
                                        ? groupOptions.Select(option => new
                                        {
                                            id = option.Id,
                                            name = option.Name,
                                            price_delta = option.PriceDelta.ToString("0.00"),
                                            sort_order = option.SortOrder,
                                        }).ToArray()
                                        : [],
                                };
                            }).ToArray()
                        : [],
                }).ToArray(),
        }).Cast<object>().ToArray();
    }

    private static object[] BuildTables(LocalCatalogSnapshot catalog)
    {
        var branches = catalog.Branches.ToDictionary(value => value.Id, StringComparer.Ordinal);

        return catalog.Tables.Select(table =>
        {
            var area = catalog.Areas[table.DiningAreaId];
            var branch = branches[area.BranchId];

            return new
            {
                id = table.Id,
                code = table.Code,
                name = table.Name,
                capacity = table.Capacity,
                status = table.Status,
                is_active = table.IsActive,
                area = new { id = area.Id, name = area.Name },
                branch = new { id = branch.Id, name = branch.Name },
            };
        }).Cast<object>().ToArray();
    }

    private async Task<object> OrderSnapshotAsync(
        RestaurantDbContext db,
        LocalOrder order,
        CancellationToken cancellationToken)
    {
        object? tableSnapshot = null;
        LocalBranch? branch = null;

        if (!string.IsNullOrWhiteSpace(order.DiningTableId))
        {
            var table = await db.DiningTables
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == order.DiningTableId, cancellationToken);

            if (table is not null)
            {
                var area = await db.DiningAreas
                    .AsNoTracking()
                    .SingleAsync(value => value.Id == table.DiningAreaId, cancellationToken);
                branch = await db.Branches
                    .AsNoTracking()
                    .SingleAsync(value => value.Id == area.BranchId, cancellationToken);
                tableSnapshot = new
                {
                    id = table.Id,
                    code = table.Code,
                    name = table.Name,
                    capacity = table.Capacity,
                    status = table.Status,
                    is_active = table.IsActive,
                    area = new { id = area.Id, name = area.Name },
                    branch = new { id = branch.Id, name = branch.Name },
                };
            }
        }

        if (branch is null && !string.IsNullOrWhiteSpace(order.BranchId))
        {
            branch = await db.Branches
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == order.BranchId, cancellationToken);
        }

        var items = (await db.OrderItems
            .Where(value => value.OrderId == order.Id)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken))
            .OrderBy(value => value.CreatedAtUtc)
            .ToArray();

        var rounds = await db.KotRounds
            .Where(value => value.OrderId == order.Id)
            .OrderBy(value => value.RoundNumber)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        var tickets = await db.KitchenTickets
            .Where(value => value.OrderId == order.Id)
            .OrderBy(value => value.RoundNumber)
            .ThenBy(value => value.QueuedAt)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var ticketSnapshots = new List<object>(tickets.Length);

        foreach (var ticket in tickets)
        {
            ticketSnapshots.Add(await LocalKitchenService.TicketSnapshotAsync(db, ticket, cancellationToken));
        }

        return new
        {
            id = order.Id,
            client_order_id = order.ClientOrderId,
            branch_id = order.BranchId ?? branch?.Id,
            service_type = order.ServiceType,
            service_reference = order.ServiceReference,
            status = order.Status,
            guest_count = order.GuestCount,
            notes = order.Notes,
            subtotal = order.Subtotal.ToString("0.00"),
            total = order.Total.ToString("0.00"),
            opened_at = order.OpenedAt,
            submitted_at = order.SubmittedAt,
            served_at = order.ServedAt,
            closed_at = order.ClosedAt,
            table = tableSnapshot,
            waiter = new
            {
                id = order.WaiterId,
                public_id = order.WaiterPublicId,
                name = order.WaiterName,
            },
            items = items.Select(LineSnapshot).ToArray(),
            kot_rounds = rounds.Select(LocalKitchenService.RoundSnapshot).ToArray(),
            kitchen_tickets = ticketSnapshots.ToArray(),
        };
    }

    private static object LineSnapshot(LocalOrderItem line) => new
    {
        id = line.Id,
        client_line_id = line.ClientLineId,
        menu_item_id = line.MenuItemId,
        item_name = line.ItemName,
        unit_price = line.UnitPrice.ToString("0.00"),
        quantity = line.Quantity,
        line_total = line.LineTotal.ToString("0.00"),
        notes = line.Notes,
        status = line.Status,
        kot_round_id = line.KotRoundId,
        round_number = line.RoundNumber,
        seat_number = line.SeatNumber,
        course_number = line.CourseNumber,
        course_name = line.CourseName,
        priority = line.Priority,
        modifiers = ParseJson(line.ModifiersJson),
        allergy_instructions = line.AllergyInstructions,
        kitchen_instructions = line.KitchenInstructions,
        refire_of_order_item_id = line.RefireOfOrderItemId,
        voided_at = line.VoidedAt,
        void_reason = line.VoidReason,
    };

    private object TableSnapshot(RestaurantDbContext db, LocalDiningTable table)
    {
        var area = db.DiningAreas.AsNoTracking().Single(value => value.Id == table.DiningAreaId);
        var branch = db.Branches.AsNoTracking().Single(value => value.Id == area.BranchId);

        return new
        {
            id = table.Id,
            code = table.Code,
            name = table.Name,
            capacity = table.Capacity,
            status = table.Status,
            is_active = table.IsActive,
            area = new { id = area.Id, name = area.Name },
            branch = new { id = branch.Id, name = branch.Name },
        };
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

    private static string RequiredString(JsonElement payload, string name, int maxLength)
    {
        if (!payload.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            throw new LocalSyncConflictException("invalid_payload", $"{name} is required.");
        }

        var value = property.GetString()?.Trim() ?? string.Empty;

        if (value.Length == 0 || value.Length > maxLength)
        {
            throw new LocalSyncConflictException("invalid_payload", $"{name} is invalid.");
        }

        return value;
    }

    private static string? OptionalString(JsonElement payload, string name, int maxLength)
    {
        if (!payload.TryGetProperty(name, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new LocalSyncConflictException("invalid_payload", $"{name} is invalid.");
        }

        var value = property.GetString();

        if (value?.Length > maxLength)
        {
            throw new LocalSyncConflictException("invalid_payload", $"{name} is too long.");
        }

        return value;
    }

    private static int RequiredInt(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var property) ||
            !property.TryGetInt32(out var value))
        {
            throw new LocalSyncConflictException("invalid_payload", $"{name} is required.");
        }

        return value;
    }

    private static int OptionalInt(JsonElement payload, string name, int defaultValue)
    {
        if (!payload.TryGetProperty(name, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return defaultValue;
        }

        if (!property.TryGetInt32(out var value))
        {
            throw new LocalSyncConflictException("invalid_payload", $"{name} is invalid.");
        }

        return value;
    }

    private static async Task<ResolvedModifiers> ResolveModifiersAsync(
        RestaurantDbContext db,
        string menuItemId,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        if (!payload.TryGetProperty("modifiers", out var modifiers) ||
            modifiers.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new ResolvedModifiers(null, 0m);
        }

        if (modifiers.ValueKind != JsonValueKind.Array)
        {
            throw new LocalSyncConflictException("invalid_payload", "modifiers must be an array.");
        }

        var optionIds = new List<string>();
        foreach (var selected in modifiers.EnumerateArray())
        {
            if (selected.ValueKind != JsonValueKind.Object ||
                !selected.TryGetProperty("option_id", out var optionIdProperty) ||
                optionIdProperty.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(optionIdProperty.GetString()))
            {
                throw new LocalSyncConflictException("invalid_payload", "Each modifier requires option_id.");
            }

            var optionId = optionIdProperty.GetString()!.Trim();
            if (optionIds.Contains(optionId, StringComparer.Ordinal))
            {
                throw new LocalSyncConflictException("invalid_payload", "A modifier option cannot be selected twice.");
            }

            optionIds.Add(optionId);
        }

        if (optionIds.Count == 0)
        {
            return new ResolvedModifiers(null, 0m);
        }

        var options = await db.ModifierOptions
            .Where(value => optionIds.Contains(value.Id) && value.IsActive)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        if (options.Length != optionIds.Count)
        {
            throw new LocalSyncConflictException("invalid_payload", "One or more modifier options are unavailable.");
        }

        var groupIds = options.Select(value => value.ModifierGroupId).Distinct(StringComparer.Ordinal).ToArray();
        var allowedGroups = await db.MenuItemModifierGroups
            .Where(value => value.MenuItemId == menuItemId && groupIds.Contains(value.ModifierGroupId))
            .Select(value => value.ModifierGroupId)
            .ToArrayAsync(cancellationToken);

        if (allowedGroups.Distinct(StringComparer.Ordinal).Count() != groupIds.Length)
        {
            throw new LocalSyncConflictException("invalid_payload", "A selected modifier does not belong to this menu item.");
        }

        var byId = options.ToDictionary(value => value.Id, StringComparer.Ordinal);
        var snapshot = optionIds.Select(id =>
        {
            var option = byId[id];
            return new
            {
                option_id = option.Id,
                group_id = option.ModifierGroupId,
                name = option.Name,
                price_delta = option.PriceDelta.ToString("0.00"),
            };
        }).ToArray();

        return new ResolvedModifiers(
            JsonSerializer.Serialize(snapshot, JsonOptions),
            options.Sum(value => value.PriceDelta));
    }

    private static int? OptionalNullableInt(
        JsonElement payload,
        string name,
        int minimum,
        int maximum)
    {
        if (!payload.TryGetProperty(name, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (!property.TryGetInt32(out var value) || value < minimum || value > maximum)
        {
            throw new LocalSyncConflictException(
                "invalid_payload",
                $"{name} must be between {minimum} and {maximum}.");
        }

        return value;
    }

    private static bool OptionalBool(JsonElement payload, string name, bool defaultValue)
    {
        if (!payload.TryGetProperty(name, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return defaultValue;
        }

        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new LocalSyncConflictException("invalid_payload", $"{name} must be a boolean.");
        }

        return property.GetBoolean();
    }

    private static object? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ResolvedModifiers(string? Json, decimal PriceDelta);

    private static string MutationHash(string operation, JsonElement payload)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("operation", operation);
            writer.WritePropertyName("payload");
            WriteCanonical(writer, payload);
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(value => value.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;

            default:
                throw new LocalSyncConflictException("invalid_payload", "Mutation payload is invalid.");
        }
    }
}

public sealed class LocalSyncConflictException : Exception
{
    public LocalSyncConflictException(
        string code,
        string message,
        string status = "conflict")
        : base(message)
    {
        Code = code;
        Status = status;
    }

    public string Code { get; }

    public string Status { get; }
}
