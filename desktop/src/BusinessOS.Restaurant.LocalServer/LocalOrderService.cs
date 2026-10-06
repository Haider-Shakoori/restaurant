using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.LocalServer;

public sealed record LocalSyncMutation(
    [property: JsonPropertyName("mutation_id")] string MutationId,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("payload")] JsonElement Payload,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset? OccurredAt);

public sealed record LocalSyncPushRequest(
    [property: JsonPropertyName("batch_id")] string? BatchId,
    [property: JsonPropertyName("mutations")] IReadOnlyList<LocalSyncMutation> Mutations);

public sealed class LocalOrderService
{
    private readonly LocalDatabaseFactory _databaseFactory;
    private readonly OperationalSnapshotStore _catalogStore;

    public LocalOrderService(
        LocalDatabaseFactory databaseFactory,
        OperationalSnapshotStore catalogStore)
    {
        _databaseFactory = databaseFactory;
        _catalogStore = catalogStore;
    }

    public async Task<Dictionary<string, object?>> BootstrapAsync(
        LocalAuthContext auth,
        CancellationToken cancellationToken = default)
    {
        var catalog = await _catalogStore.LoadCatalogAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var cursor = await db.Changes
            .MaxAsync(value => (long?)value.Sequence, cancellationToken) ?? 0;

        var ordersQuery = db.Orders
            .Where(value => value.Status != "closed" && value.Status != "cancelled");

        if (auth.Staff.Role.Equals("waiter", StringComparison.OrdinalIgnoreCase))
        {
            ordersQuery = ordersQuery.Where(value => value.WaiterId == auth.Staff.Id);
        }

        var orders = await ordersQuery
            .OrderBy(value => value.OpenedAtUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var orderSnapshots = new List<object>(orders.Count);

        foreach (var order in orders)
        {
            orderSnapshots.Add(await OrderSnapshotAsync(db, order, cancellationToken));
        }

        return new Dictionary<string, object?>
        {
            ["schema_version"] = 1,
            ["server_time"] = DateTimeOffset.UtcNow,
            ["cursor"] = cursor,
            ["sync_batch_size"] = 100,
            ["tenant_id"] = auth.HostActivation.Snapshot.TenantId,
            ["device_id"] = auth.Device.Id,
            ["user"] = StaffSnapshot(auth.Staff),
            ["branches"] = BranchesSnapshot(catalog),
            ["staff"] = Array.Empty<object>(),
            ["menu"] = MenuSnapshot(catalog),
            ["tables"] = TablesSnapshot(catalog),
            ["orders"] = orderSnapshots,
        };
    }

    public async Task<Dictionary<string, object?>> PushAsync(
        LocalAuthContext auth,
        LocalSyncPushRequest request,
        CancellationToken cancellationToken = default)
    {
        var results = new List<object>();

        foreach (var mutation in request.Mutations)
        {
            results.Add(await ProcessMutationAsync(auth, mutation, cancellationToken));
        }

        await using var db = _databaseFactory.Create();
        var cursor = await db.Changes
            .MaxAsync(value => (long?)value.Sequence, cancellationToken) ?? 0;

        return new Dictionary<string, object?>
        {
            ["server_time"] = DateTimeOffset.UtcNow,
            ["results"] = results,
            ["pull_cursor"] = cursor,
        };
    }

    public async Task<Dictionary<string, object?>> PullAsync(
        LocalAuthContext auth,
        long cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        await using var db = _databaseFactory.Create();

        var changes = await db.Changes
            .Where(value => value.Sequence > cursor)
            .OrderBy(value => value.Sequence)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var visible = new List<object>();

        foreach (var change in changes)
        {
            if (change.EntityType == "order")
            {
                var order = await db.Orders
                    .AsNoTracking()
                    .SingleOrDefaultAsync(value => value.Id == change.EntityId, cancellationToken);

                if (order is null)
                {
                    visible.Add(new
                    {
                        sequence = change.Sequence,
                        entity_type = "order",
                        entity_id = change.EntityId,
                        operation = "delete",
                        data = (object?)null,
                    });
                    continue;
                }

                if (auth.Staff.Role.Equals("waiter", StringComparison.OrdinalIgnoreCase) &&
                    order.WaiterId != auth.Staff.Id)
                {
                    continue;
                }

                visible.Add(new
                {
                    sequence = change.Sequence,
                    entity_type = "order",
                    entity_id = order.Id,
                    operation = "upsert",
                    data = await OrderSnapshotAsync(db, order, cancellationToken),
                });
                continue;
            }

            if (change.EntityType == "dining_table")
            {
                var table = await TableSnapshotAsync(db, change.EntityId, cancellationToken);

                visible.Add(new
                {
                    sequence = change.Sequence,
                    entity_type = "dining_table",
                    entity_id = change.EntityId,
                    operation = table is null ? "delete" : "upsert",
                    data = table,
                });
            }
        }

        var scannedCursor = changes.Count == 0 ? cursor : changes[^1].Sequence;
        var hasMore = await db.Changes.AnyAsync(
            value => value.Sequence > scannedCursor,
            cancellationToken);

        return new Dictionary<string, object?>
        {
            ["server_time"] = DateTimeOffset.UtcNow,
            ["cursor"] = scannedCursor,
            ["has_more"] = hasMore,
            ["changes"] = visible,
        };
    }

    private async Task<object> ProcessMutationAsync(
        LocalAuthContext auth,
        LocalSyncMutation mutation,
        CancellationToken cancellationToken)
    {
        if (!CanOperateOrders(auth.Staff.Role))
        {
            return Failure(mutation, "rejected", "forbidden", "This restaurant role cannot create waiter orders.");
        }

        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var requestHash = RequestHash(mutation);

        var existing = await db.Mutations.SingleOrDefaultAsync(
            value => value.DeviceId == auth.Device.Id && value.MutationId == mutation.MutationId,
            cancellationToken);

        if (existing is not null)
        {
            if (!FixedEquals(existing.RequestHash, requestHash))
            {
                return Failure(
                    mutation,
                    "rejected",
                    "mutation_id_reused",
                    "This mutation ID was already used with different content.");
            }

            return JsonSerializer.Deserialize<JsonElement>(existing.ResponseJson);
        }

        object response;

        try
        {
            response = mutation.Operation switch
            {
                "order.open" => await OpenOrderAsync(db, auth, mutation, cancellationToken),
                "order.item.add" => await AddOrderItemAsync(db, auth, mutation, cancellationToken),
                "order.submit" => await SubmitOrderAsync(db, auth, mutation, cancellationToken),
                _ => Failure(
                    mutation,
                    "rejected",
                    "unsupported_operation",
                    "Unsupported local order operation."),
            };
        }
        catch (LocalOrderConflict exception)
        {
            response = Failure(mutation, "conflict", exception.Code, exception.Message);
        }

        db.Mutations.Add(new LocalMutation
        {
            DeviceId = auth.Device.Id,
            MutationId = mutation.MutationId,
            Operation = mutation.Operation,
            RequestHash = requestHash,
            Status = ResponseStatus(response),
            ResponseJson = JsonSerializer.Serialize(response),
            ProcessedAtUtc = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    private async Task<object> OpenOrderAsync(
        RestaurantDbContext db,
        LocalAuthContext auth,
        LocalSyncMutation mutation,
        CancellationToken cancellationToken)
    {
        var clientOrderId = RequiredString(mutation.Payload, "client_order_id");
        var tableId = RequiredString(mutation.Payload, "dining_table_id");
        var guestCount = OptionalInt(mutation.Payload, "guest_count", 1);
        var notes = OptionalString(mutation.Payload, "notes");

        if (await db.Orders.AnyAsync(value => value.ClientOrderId == clientOrderId, cancellationToken))
        {
            throw new LocalOrderConflict("order_state_conflict", "This client order already exists.");
        }

        var table = await db.DiningTables.SingleOrDefaultAsync(
            value => value.Id == tableId && value.IsActive,
            cancellationToken)
            ?? throw new LocalOrderConflict("dependency_missing", "The selected table does not exist locally.");

        var tableBusy = await db.Orders.AnyAsync(
            value =>
                value.DiningTableId == tableId &&
                value.Status != "closed" &&
                value.Status != "cancelled",
            cancellationToken);

        if (tableBusy)
        {
            throw new LocalOrderConflict("table_busy", "The selected table already has an active order.");
        }

        var order = new LocalOrder
        {
            Id = Guid.NewGuid().ToString("D"),
            ClientOrderId = clientOrderId,
            DiningTableId = tableId,
            WaiterId = auth.Staff.Id,
            Status = "draft",
            GuestCount = Math.Clamp(guestCount, 1, 100),
            Notes = notes,
            Subtotal = 0m,
            Total = 0m,
            OpenedAtUtc = DateTimeOffset.UtcNow,
            CloudSynced = false,
        };

        table.Status = "occupied";
        db.Orders.Add(order);
        RecordChange(db, "order", order.Id);
        RecordChange(db, "dining_table", table.Id);

        await db.SaveChangesAsync(cancellationToken);

        return Accepted(
            mutation,
            "order",
            order.Id,
            order.ClientOrderId,
            await OrderSnapshotAsync(db, order, cancellationToken));
    }

    private async Task<object> AddOrderItemAsync(
        RestaurantDbContext db,
        LocalAuthContext auth,
        LocalSyncMutation mutation,
        CancellationToken cancellationToken)
    {
        var clientOrderId = RequiredString(mutation.Payload, "client_order_id");
        var clientLineId = RequiredString(mutation.Payload, "client_line_id");
        var menuItemId = RequiredString(mutation.Payload, "menu_item_id");
        var quantity = Math.Clamp(OptionalInt(mutation.Payload, "quantity", 1), 1, 999);
        var notes = OptionalString(mutation.Payload, "notes");

        var order = await db.Orders.SingleOrDefaultAsync(
            value => value.ClientOrderId == clientOrderId,
            cancellationToken)
            ?? throw new LocalOrderConflict("dependency_missing", "The local order does not exist.");

        AuthorizeOrder(auth, order);

        if (order.Status != "draft")
        {
            throw new LocalOrderConflict("order_state_conflict", "Only draft orders can be edited.");
        }

        if (await db.OrderItems.AnyAsync(
                value => value.OrderId == order.Id && value.ClientLineId == clientLineId,
                cancellationToken))
        {
            throw new LocalOrderConflict("order_state_conflict", "This client order line already exists.");
        }

        var menuItem = await db.MenuItems
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == menuItemId && value.IsAvailable,
                cancellationToken)
            ?? throw new LocalOrderConflict("menu_unavailable", "The selected menu item is unavailable.");

        var line = new LocalOrderItem
        {
            Id = Guid.NewGuid().ToString("D"),
            ClientLineId = clientLineId,
            OrderId = order.Id,
            MenuItemId = menuItem.Id,
            ItemName = menuItem.Name,
            UnitPrice = menuItem.Price,
            Quantity = quantity,
            LineTotal = menuItem.Price * quantity,
            Notes = notes,
            Status = "pending",
        };

        db.OrderItems.Add(line);
        await db.SaveChangesAsync(cancellationToken);

        order.Subtotal = await db.OrderItems
            .Where(value => value.OrderId == order.Id)
            .SumAsync(value => value.LineTotal, cancellationToken);
        order.Total = order.Subtotal;
        order.CloudSynced = false;
        RecordChange(db, "order", order.Id);

        await db.SaveChangesAsync(cancellationToken);

        return Accepted(
            mutation,
            "order_item",
            line.Id,
            line.ClientLineId,
            new
            {
                line = new
                {
                    id = line.Id,
                    client_line_id = line.ClientLineId,
                    menu_item_id = line.MenuItemId,
                    item_name = line.ItemName,
                    unit_price = line.UnitPrice,
                    quantity = line.Quantity,
                    line_total = line.LineTotal,
                    notes = line.Notes,
                    status = line.Status,
                },
                order = await OrderSnapshotAsync(db, order, cancellationToken),
            });
    }

    private async Task<object> SubmitOrderAsync(
        RestaurantDbContext db,
        LocalAuthContext auth,
        LocalSyncMutation mutation,
        CancellationToken cancellationToken)
    {
        var clientOrderId = RequiredString(mutation.Payload, "client_order_id");
        var order = await db.Orders.SingleOrDefaultAsync(
            value => value.ClientOrderId == clientOrderId,
            cancellationToken)
            ?? throw new LocalOrderConflict("dependency_missing", "The local order does not exist.");

        AuthorizeOrder(auth, order);

        if (order.Status != "draft")
        {
            throw new LocalOrderConflict("order_state_conflict", "Only draft orders can be submitted.");
        }

        if (!await db.OrderItems.AnyAsync(value => value.OrderId == order.Id, cancellationToken))
        {
            throw new LocalOrderConflict("order_state_conflict", "Add at least one item before submitting.");
        }

        order.Status = "submitted";
        order.SubmittedAtUtc = DateTimeOffset.UtcNow;
        order.CloudSynced = false;
        RecordChange(db, "order", order.Id);

        await db.SaveChangesAsync(cancellationToken);

        return Accepted(
            mutation,
            "order",
            order.Id,
            order.ClientOrderId,
            await OrderSnapshotAsync(db, order, cancellationToken));
    }

    private async Task<object> OrderSnapshotAsync(
        RestaurantDbContext db,
        LocalOrder order,
        CancellationToken cancellationToken)
    {
        var table = await TableSnapshotAsync(db, order.DiningTableId, cancellationToken)
            ?? throw new InvalidOperationException("The local order table is missing.");
        var waiter = await db.StaffUsers
            .AsNoTracking()
            .SingleAsync(value => value.Id == order.WaiterId, cancellationToken);
        var items = await db.OrderItems
            .Where(value => value.OrderId == order.Id)
            .OrderBy(value => value.Id)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new
        {
            id = order.Id,
            client_order_id = order.ClientOrderId,
            status = order.Status,
            guest_count = order.GuestCount,
            notes = order.Notes,
            subtotal = order.Subtotal,
            total = order.Total,
            opened_at = order.OpenedAtUtc,
            submitted_at = order.SubmittedAtUtc,
            served_at = order.ServedAtUtc,
            closed_at = order.ClosedAtUtc,
            table,
            waiter = StaffSnapshot(waiter),
            items = items.Select(item => new
            {
                id = item.Id,
                client_line_id = item.ClientLineId,
                menu_item_id = item.MenuItemId,
                item_name = item.ItemName,
                unit_price = item.UnitPrice,
                quantity = item.Quantity,
                line_total = item.LineTotal,
                notes = item.Notes,
                status = item.Status,
            }).ToArray(),
            kitchen_tickets = Array.Empty<object>(),
        };
    }

    private static object StaffSnapshot(LocalStaffUser staff) => new
    {
        id = staff.Id,
        public_id = staff.PublicId,
        name = staff.Name,
        email = staff.Email,
        role = staff.Role,
    };

    private static object[] BranchesSnapshot(LocalCatalogSnapshot catalog) =>
        catalog.Branches.Select(value => (object)new
        {
            id = value.Id,
            code = value.Code,
            name = value.Name,
            is_active = value.IsActive,
        }).ToArray();

    private static object[] MenuSnapshot(LocalCatalogSnapshot catalog)
    {
        var linksByItem = catalog.MenuItemModifierGroups
            .GroupBy(value => value.MenuItemId)
            .ToDictionary(value => value.Key, value => value.ToArray(), StringComparer.Ordinal);
        var optionsByGroup = catalog.ModifierOptions
            .GroupBy(value => value.ModifierGroupId)
            .ToDictionary(value => value.Key, value => value.ToArray(), StringComparer.Ordinal);

        return catalog.Categories.Select(category => (object)new
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
                    price = item.Price,
                    currency = item.Currency,
                    sort_order = item.SortOrder,
                    modifier_groups = linksByItem.TryGetValue(item.Id, out var links)
                        ? links.Where(link => catalog.ModifierGroups.ContainsKey(link.ModifierGroupId))
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
                                    options = optionsByGroup.TryGetValue(group.Id, out var options)
                                        ? options.Select(option => new
                                        {
                                            id = option.Id,
                                            name = option.Name,
                                            price_delta = option.PriceDelta,
                                            sort_order = option.SortOrder,
                                        }).ToArray()
                                        : [],
                                };
                            }).ToArray()
                        : [],
                }).ToArray(),
        }).ToArray();
    }

    private static object[] TablesSnapshot(LocalCatalogSnapshot catalog)
    {
        var branches = catalog.Branches.ToDictionary(value => value.Id, StringComparer.Ordinal);

        return catalog.Tables.Select(table =>
        {
            var area = catalog.Areas[table.DiningAreaId];
            var branch = branches[area.BranchId];

            return (object)new
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
        }).ToArray();
    }

    private async Task<object?> TableSnapshotAsync(
        RestaurantDbContext db,
        string tableId,
        CancellationToken cancellationToken)
    {
        var table = await db.DiningTables.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == tableId,
            cancellationToken);

        if (table is null)
        {
            return null;
        }

        var area = await db.DiningAreas.AsNoTracking().SingleAsync(
            value => value.Id == table.DiningAreaId,
            cancellationToken);
        var branch = await db.Branches.AsNoTracking().SingleAsync(
            value => value.Id == area.BranchId,
            cancellationToken);

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

    private static object Accepted(
        LocalSyncMutation mutation,
        string entityType,
        string entityId,
        string clientEntityId,
        object data) => new
    {
        mutation_id = mutation.MutationId,
        status = "accepted",
        operation = mutation.Operation,
        entity_type = entityType,
        entity_id = entityId,
        client_entity_id = clientEntityId,
        data,
    };

    private static object Failure(
        LocalSyncMutation mutation,
        string status,
        string code,
        string message) => new
    {
        mutation_id = mutation.MutationId,
        status,
        operation = mutation.Operation,
        code,
        message,
    };

    private static string ResponseStatus(object response)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response));
        return json.RootElement.GetProperty("status").GetString() ?? "rejected";
    }

    private static void AuthorizeOrder(LocalAuthContext auth, LocalOrder order)
    {
        if (auth.Staff.Role.Equals("waiter", StringComparison.OrdinalIgnoreCase) &&
            order.WaiterId != auth.Staff.Id)
        {
            throw new LocalOrderConflict("order_state_conflict", "This order belongs to another waiter.");
        }
    }

    private static bool CanOperateOrders(string role) =>
        role.Equals("waiter", StringComparison.OrdinalIgnoreCase) ||
        role.Equals("cashier", StringComparison.OrdinalIgnoreCase) ||
        role.Equals("manager", StringComparison.OrdinalIgnoreCase) ||
        role.Equals("owner", StringComparison.OrdinalIgnoreCase);

    private static string RequiredString(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new LocalOrderConflict("invalid_payload", $"'{name}' is required.");

    private static string? OptionalString(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int OptionalInt(JsonElement payload, string name, int fallback) =>
        payload.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : fallback;

    private static void RecordChange(RestaurantDbContext db, string entityType, string entityId)
    {
        db.Changes.Add(new LocalChange
        {
            EntityType = entityType,
            EntityId = entityId,
            Operation = "upsert",
            OccurredAtUtc = DateTimeOffset.UtcNow,
        });
    }

    private static string RequestHash(LocalSyncMutation mutation)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("operation", mutation.Operation);
            writer.WritePropertyName("payload");
            WriteCanonical(writer, mutation.Payload);
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
                foreach (var value in element.EnumerateArray())
                {
                    WriteCanonical(writer, value);
                }
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool FixedEquals(string left, string right)
    {
        var a = Convert.FromHexString(left);
        var b = Convert.FromHexString(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private sealed class LocalOrderConflict(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
