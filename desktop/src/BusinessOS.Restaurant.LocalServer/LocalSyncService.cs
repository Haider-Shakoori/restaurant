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
        LocalKitchenService kitchen)
    {
        _databaseFactory = databaseFactory;
        _catalogStore = catalogStore;
        _kitchen = kitchen;
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
        var tableId = RequiredString(payload, "dining_table_id", 40);
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

        var table = await db.DiningTables.FindAsync([tableId], cancellationToken);

        if (table is null || !table.IsActive ||
            table.Status is "disabled" or "reserved")
        {
            throw new LocalSyncConflictException(
                "table_busy",
                "This table is not currently available for walk-in ordering.");
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

        var now = DateTimeOffset.UtcNow;
        var order = new LocalOrder
        {
            Id = Guid.CreateVersion7().ToString("N"),
            ClientOrderId = clientOrderId,
            DiningTableId = tableId,
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
        table.Status = "occupied";
        await db.SaveChangesAsync(cancellationToken);

        var snapshot = await OrderSnapshotAsync(db, order, cancellationToken);
        AddChange(db, "order", order.Id, principal.UserId, snapshot);
        AddChange(db, "dining_table", table.Id, null, TableSnapshot(db, table));

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

        if (order.Status != "draft")
        {
            throw new LocalSyncConflictException(
                "order_state_conflict",
                "Items can only be edited while the order is in draft.");
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

        var now = DateTimeOffset.UtcNow;
        var line = new LocalOrderItem
        {
            Id = Guid.CreateVersion7().ToString("N"),
            OrderId = order.Id,
            MenuItemId = menuItem.Id,
            ClientLineId = clientLineId,
            ItemName = menuItem.Name,
            UnitPrice = menuItem.Price,
            Quantity = quantity,
            LineTotal = menuItem.Price * quantity,
            Notes = OptionalString(payload, "notes", 1000),
            Status = "pending",
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

        if (order.Status is "submitted" or "preparing" or "ready")
        {
            return Accepted(
                mutation,
                "order",
                order.Id,
                order.ClientOrderId,
                await OrderSnapshotAsync(db, order, cancellationToken));
        }

        if (order.Status != "draft")
        {
            throw new LocalSyncConflictException(
                "order_state_conflict",
                "Only draft orders can be submitted.");
        }

        var hasItems = await db.OrderItems
            .AnyAsync(value => value.OrderId == order.Id, cancellationToken);

        if (!hasItems)
        {
            throw new LocalSyncConflictException(
                "order_state_conflict",
                "Add at least one item before submitting the order.");
        }

        order.Status = "submitted";
        order.SubmittedAt = DateTimeOffset.UtcNow;
        order.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await _kitchen.DispatchAsync(db, order, principal, cancellationToken);

        var snapshot = await OrderSnapshotAsync(db, order, cancellationToken);
        AddChange(db, "order", order.Id, order.WaiterId, snapshot);

        return Accepted(mutation, "order", order.Id, order.ClientOrderId, snapshot);
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
        var table = await db.DiningTables
            .AsNoTracking()
            .SingleAsync(value => value.Id == order.DiningTableId, cancellationToken);
        var area = await db.DiningAreas
            .AsNoTracking()
            .SingleAsync(value => value.Id == table.DiningAreaId, cancellationToken);
        var branch = await db.Branches
            .AsNoTracking()
            .SingleAsync(value => value.Id == area.BranchId, cancellationToken);
        var items = (await db.OrderItems
            .Where(value => value.OrderId == order.Id)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken))
            .OrderBy(value => value.CreatedAtUtc)
            .ToArray();

        var tickets = await db.KitchenTickets
            .Where(value => value.OrderId == order.Id)
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
            status = order.Status,
            guest_count = order.GuestCount,
            notes = order.Notes,
            subtotal = order.Subtotal.ToString("0.00"),
            total = order.Total.ToString("0.00"),
            opened_at = order.OpenedAt,
            submitted_at = order.SubmittedAt,
            served_at = order.ServedAt,
            closed_at = order.ClosedAt,
            table = new
            {
                id = table.Id,
                code = table.Code,
                name = table.Name,
                capacity = table.Capacity,
                status = table.Status,
                is_active = table.IsActive,
                area = new { id = area.Id, name = area.Name },
                branch = new { id = branch.Id, name = branch.Name },
            },
            waiter = new
            {
                id = order.WaiterId,
                public_id = order.WaiterPublicId,
                name = order.WaiterName,
            },
            items = items.Select(LineSnapshot).ToArray(),
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
